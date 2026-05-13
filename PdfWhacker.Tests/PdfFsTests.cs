using PdfWhacker;

namespace PdfWhacker.Tests;

public class PdfFsTests : IDisposable
{
	private readonly string _tempDir;

	public PdfFsTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "pdfwhacker-pdffs-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
	}

	private (string archive, string output) MakeFolders()
	{
		string a = Path.Combine(_tempDir, "archive");
		string o = Path.Combine(_tempDir, "output");
		Directory.CreateDirectory(a);
		Directory.CreateDirectory(o);
		return (a, o);
	}

	[Fact]
	public void BuildUniquePathPair_returns_plain_name_when_neither_folder_has_collision()
	{
		var (archive, output) = MakeFolders();

		var (a, o) = PdfFs.BuildUniquePathPair(archive, output, "doc.pdf");

		Assert.Equal(Path.Combine(archive, "doc.pdf"), a);
		Assert.Equal(Path.Combine(output, "doc.pdf"), o);
	}

	[Fact]
	public void BuildUniquePathPair_picks_suffix_2_when_archive_collides()
	{
		var (archive, output) = MakeFolders();
		File.WriteAllText(Path.Combine(archive, "doc.pdf"), "x");

		var (a, o) = PdfFs.BuildUniquePathPair(archive, output, "doc.pdf");

		Assert.Equal(Path.Combine(archive, "doc (2).pdf"), a);
		Assert.Equal(Path.Combine(output, "doc (2).pdf"), o);
	}

	[Fact]
	public void BuildUniquePathPair_picks_same_suffix_in_both_folders()
	{
		// If archive has "doc.pdf" but output also has "doc.pdf" AND "doc (2).pdf",
		// the pair must share a suffix that's free in both — so (3).
		var (archive, output) = MakeFolders();
		File.WriteAllText(Path.Combine(archive, "doc.pdf"), "x");
		File.WriteAllText(Path.Combine(output, "doc.pdf"), "x");
		File.WriteAllText(Path.Combine(output, "doc (2).pdf"), "x");

		var (a, o) = PdfFs.BuildUniquePathPair(archive, output, "doc.pdf");

		Assert.Equal(Path.Combine(archive, "doc (3).pdf"), a);
		Assert.Equal(Path.Combine(output, "doc (3).pdf"), o);
	}

	[Fact]
	public void BuildUniquePathPair_preserves_extension_casing()
	{
		var (archive, output) = MakeFolders();
		File.WriteAllText(Path.Combine(archive, "doc.PDF"), "x");

		var (a, _) = PdfFs.BuildUniquePathPair(archive, output, "doc.PDF");

		Assert.EndsWith(".PDF", a);
	}
}
