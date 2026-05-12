using System.Text;
using PdfWhacker;

namespace PdfWhacker.Tests;

public class IsValidPdfStructureTests : IDisposable
{
	private readonly string _tempDir;

	public IsValidPdfStructureTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "pdfwhacker-tests-" + Guid.NewGuid().ToString("N"));
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
	public void Returns_true_for_minimal_valid_pdf_shape()
	{
		var body = new StringBuilder();
		body.Append("%PDF-1.7\n");
		body.Append("some pdf body bytes\n");
		body.Append("%%EOF\n");
		string path = WriteFile("valid.pdf", Bytes(body.ToString()));

		Assert.True(GhostscriptRunner.IsValidPdfStructure(path));
	}

	[Fact]
	public void Returns_false_for_empty_file()
	{
		string path = WriteFile("empty.pdf", Array.Empty<byte>());
		Assert.False(GhostscriptRunner.IsValidPdfStructure(path));
	}

	[Fact]
	public void Returns_false_when_header_is_wrong()
	{
		string path = WriteFile("not-pdf.pdf", Bytes("NOT A PDF FILE\n%%EOF\n"));
		Assert.False(GhostscriptRunner.IsValidPdfStructure(path));
	}

	[Fact]
	public void Returns_false_when_eof_marker_is_missing()
	{
		string path = WriteFile("truncated.pdf", Bytes("%PDF-1.7\nsome body but no eof marker\n"));
		Assert.False(GhostscriptRunner.IsValidPdfStructure(path));
	}

	[Fact]
	public void Returns_false_for_file_shorter_than_header()
	{
		string path = WriteFile("tiny.pdf", Bytes("%PD"));
		Assert.False(GhostscriptRunner.IsValidPdfStructure(path));
	}

	[Fact]
	public void Returns_false_when_file_is_missing()
	{
		string path = Path.Combine(_tempDir, "does-not-exist.pdf");
		Assert.False(GhostscriptRunner.IsValidPdfStructure(path));
	}

	[Fact]
	public void Returns_true_when_eof_is_within_last_1024_bytes_of_a_large_file()
	{
		var body = new StringBuilder();
		body.Append("%PDF-1.7\n");
		body.Append(new string('x', 5000));
		body.Append("\n%%EOF\n");
		string path = WriteFile("big.pdf", Bytes(body.ToString()));

		Assert.True(GhostscriptRunner.IsValidPdfStructure(path));
	}
}
