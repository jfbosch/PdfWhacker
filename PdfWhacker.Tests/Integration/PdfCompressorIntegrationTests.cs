using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(nameof(GhostscriptCollection))]
public class PdfCompressorIntegrationTests
{
	private readonly IntegrationFixture _fx;

	public PdfCompressorIntegrationTests(IntegrationFixture fx)
	{
		_fx = fx;
	}

	private (string inputDir, string outputDir, string archiveDir) CreateFolders(string label)
	{
		string testRoot = _fx.MakeIsolatedDirectory(label);
		string inputDir = Path.Combine(testRoot, "CompressionInput");
		string outputDir = Path.Combine(testRoot, "Output");
		string archiveDir = Path.Combine(testRoot, "Original", "Compression");
		Directory.CreateDirectory(inputDir);
		Directory.CreateDirectory(outputDir);
		Directory.CreateDirectory(archiveDir);
		return (inputDir, outputDir, archiveDir);
	}

	[Fact]
	public void Large_compressible_pdf_produces_smaller_output()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("compress-large");
		string inputPath = _fx.CopyFixture(_fx.LargeBasePdf, inputDir, "big.pdf");
		long originalSize = new FileInfo(inputPath).Length;

		new PdfCompressor().CompressFile(inputPath, outputDir, archiveDir, _fx.GhostscriptPath);

		string outputPath = Path.Combine(outputDir, "big.pdf");
		string archivePath = Path.Combine(archiveDir, "big.pdf");

		Assert.False(File.Exists(inputPath), "Input should be deleted after processing.");
		Assert.True(File.Exists(outputPath), "Compressed output should exist.");
		Assert.True(File.Exists(archivePath), "Archive copy should exist.");

		long compressedSize = new FileInfo(outputPath).Length;
		Assert.True(compressedSize < originalSize * 0.95,
			$"Expected output to be at least 5% smaller than {originalSize} bytes, got {compressedSize} bytes.");
		Assert.True(GhostscriptRunner.IsValidPdfStructure(outputPath), "Compressed output should be a valid PDF.");
	}

	[Fact]
	public void Incompressible_small_pdf_passes_original_through()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("compress-small");
		string inputPath = _fx.CopyFixture(_fx.BasePdf, inputDir, "tiny.pdf");

		new PdfCompressor().CompressFile(inputPath, outputDir, archiveDir, _fx.GhostscriptPath);

		string outputPath = Path.Combine(outputDir, "tiny.pdf");

		Assert.False(File.Exists(inputPath), "Input should be deleted after processing.");
		Assert.True(File.Exists(outputPath), "Output file should exist (original passed through).");
		Assert.True(_fx.FilesContentEqual(outputPath, _fx.BasePdf),
			"Below-threshold compression should leave the original byte-identical in the Output folder.");
	}

	[Fact]
	public void Password_protected_pdf_passes_original_through()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("compress-locked");
		string inputPath = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, inputDir, "locked.pdf");

		new PdfCompressor().CompressFile(inputPath, outputDir, archiveDir, _fx.GhostscriptPath);

		string outputPath = Path.Combine(outputDir, "locked.pdf");

		Assert.False(File.Exists(inputPath), "Input should be deleted even when compression is skipped.");
		Assert.True(File.Exists(outputPath), "Output file should exist (original passed through).");
		_fx.AssertStillEncrypted(outputPath);
		Assert.True(_fx.FilesContentEqual(outputPath, _fx.UserPwdEncryptedPdf),
			"Encrypted passthrough should be byte-identical to the original locked fixture.");
	}

	[Fact]
	public void Invalid_pdf_content_falls_back_to_original()
	{
		// Garbage in a .pdf file makes the real Ghostscript exit non-zero; we exercise
		// the "Ghostscript exited with code N" branch and assert the watch-mode pipeline
		// copies the archived original back to Output without losing data.
		var (inputDir, outputDir, archiveDir) = CreateFolders("compress-invalid-pdf");
		string inputPath = Path.Combine(inputDir, "broken.pdf");
		const string garbageContent = "this is not actually a pdf, just bytes pretending to be one";
		File.WriteAllText(inputPath, garbageContent);

		new PdfCompressor().CompressFile(inputPath, outputDir, archiveDir, _fx.GhostscriptPath);

		string outputPath = Path.Combine(outputDir, "broken.pdf");
		string archivePath = Path.Combine(archiveDir, "broken.pdf");

		Assert.False(File.Exists(inputPath), "Input should be deleted even on Ghostscript failure.");
		Assert.True(File.Exists(outputPath), "Original should pass through to Output on Ghostscript failure.");
		Assert.True(File.Exists(archivePath), "Archive copy of the input should remain.");
		Assert.Equal(garbageContent, File.ReadAllText(outputPath));
		Assert.Equal(garbageContent, File.ReadAllText(archivePath));
	}

	[Fact]
	public void Missing_ghostscript_binary_falls_back_to_original()
	{
		// Process.Start throws Win32Exception when the FileName doesn't exist;
		// PdfCompressor's catch block must still preserve the input data via the
		// archive copy.
		var (inputDir, outputDir, archiveDir) = CreateFolders("compress-no-gs");
		string inputPath = _fx.CopyFixture(_fx.BasePdf, inputDir, "doc.pdf");
		string bogusGsPath = Path.Combine(_fx.RootDir, "no-such-ghostscript-" + Guid.NewGuid().ToString("N") + ".exe");

		new PdfCompressor().CompressFile(inputPath, outputDir, archiveDir, bogusGsPath);

		string outputPath = Path.Combine(outputDir, "doc.pdf");
		string archivePath = Path.Combine(archiveDir, "doc.pdf");

		Assert.False(File.Exists(inputPath));
		Assert.True(File.Exists(outputPath));
		Assert.True(File.Exists(archivePath));
		Assert.True(_fx.FilesContentEqual(outputPath, _fx.BasePdf));
	}
}
