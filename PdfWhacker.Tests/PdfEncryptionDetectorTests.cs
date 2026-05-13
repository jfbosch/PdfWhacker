using System.Text;
using PdfWhacker;

namespace PdfWhacker.Tests;

public class PdfEncryptionDetectorTests : IDisposable
{
	private readonly string _tempDir;

	public PdfEncryptionDetectorTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "pdfwhacker-detector-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
	}

	private string WriteFile(string name, byte[] bytes)
	{
		string path = Path.Combine(_tempDir, name);
		File.WriteAllBytes(path, bytes);
		return path;
	}

	private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

	[Fact]
	public void Returns_false_for_minimal_valid_pdf_without_encrypt_entry()
	{
		var body = new StringBuilder();
		body.Append("%PDF-1.7\n");
		body.Append("trailer << /Size 5 /Root 1 0 R >>\n");
		body.Append("%%EOF\n");
		string path = WriteFile("plain.pdf", Bytes(body.ToString()));

		Assert.False(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Returns_true_when_trailer_has_encrypt_indirect_reference()
	{
		var body = new StringBuilder();
		body.Append("%PDF-1.7\n");
		body.Append("trailer << /Size 5 /Root 1 0 R /Encrypt 7 0 R >>\n");
		body.Append("%%EOF\n");
		string path = WriteFile("encrypted.pdf", Bytes(body.ToString()));

		Assert.True(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Returns_false_for_empty_file()
	{
		string path = WriteFile("empty.pdf", Array.Empty<byte>());
		Assert.False(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Returns_false_when_header_is_wrong()
	{
		string path = WriteFile("not-pdf.pdf", Bytes("NOT A PDF FILE\n/Encrypt 1 0 R\n%%EOF\n"));
		Assert.False(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Returns_false_when_file_is_missing()
	{
		string path = Path.Combine(_tempDir, "does-not-exist.pdf");
		Assert.False(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Returns_false_for_file_shorter_than_header()
	{
		string path = WriteFile("tiny.pdf", Bytes("%PD"));
		Assert.False(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Returns_true_when_encrypt_is_within_last_4kb_of_a_large_file()
	{
		var body = new StringBuilder();
		body.Append("%PDF-1.7\n");
		body.Append(new string('x', 8000));
		body.Append("\ntrailer << /Size 5 /Root 1 0 R /Encrypt 9 0 R >>\n");
		body.Append("%%EOF\n");
		string path = WriteFile("big-encrypted.pdf", Bytes(body.ToString()));

		Assert.True(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Returns_false_when_encrypt_only_appears_far_from_trailer()
	{
		var body = new StringBuilder();
		body.Append("%PDF-1.7\n");
		body.Append("body content mentioning /Encrypt 1 0 R as plain text\n");
		body.Append(new string('x', 8000));
		body.Append("\ntrailer << /Size 5 /Root 1 0 R >>\n");
		body.Append("%%EOF\n");
		string path = WriteFile("mention-in-body.pdf", Bytes(body.ToString()));

		Assert.False(PdfEncryptionDetector.IsEncrypted(path));
	}

	[Fact]
	public void Does_not_match_encrypt_without_indirect_reference_syntax()
	{
		var body = new StringBuilder();
		body.Append("%PDF-1.7\n");
		body.Append("trailer << /Size 5 /Root 1 0 R /EncryptionKey foo >>\n");
		body.Append("%%EOF\n");
		string path = WriteFile("similar-key.pdf", Bytes(body.ToString()));

		Assert.False(PdfEncryptionDetector.IsEncrypted(path));
	}
}
