using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(nameof(GhostscriptCollection))]
public class RecursivePdfCompressorIntegrationTests
{
	private readonly IntegrationFixture _fx;

	public RecursivePdfCompressorIntegrationTests(IntegrationFixture fx)
	{
		_fx = fx;
	}

	[Fact]
	public void Mixed_tree_compresses_what_it_can_and_skips_the_rest()
	{
		string root = _fx.MakeIsolatedDirectory("recursive-compress-mixed");
		string subdir = Path.Combine(root, "subdir");
		Directory.CreateDirectory(subdir);

		string bigPath = _fx.CopyFixture(_fx.LargeBasePdf, root, "big.pdf");
		string smallPath = _fx.CopyFixture(_fx.BasePdf, subdir, "tiny.pdf");
		string lockedPath = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, subdir, "locked.pdf");

		long bigOriginalSize = new FileInfo(bigPath).Length;
		byte[] smallOriginalBytes = File.ReadAllBytes(smallPath);
		byte[] lockedOriginalBytes = File.ReadAllBytes(lockedPath);

		var (exitCode, summary) = RunCompressCapturingSummary(root);

		Assert.Equal(0, exitCode);
		Assert.Contains("Scanned:             3", summary);
		Assert.Contains("Replaced:            1", summary);
		Assert.Contains("Skipped (encrypted): 1", summary);
		Assert.Contains("Errored:             0", summary);

		// Big PDF replaced with a smaller version in place.
		long bigCompressedSize = new FileInfo(bigPath).Length;
		Assert.True(bigCompressedSize < bigOriginalSize * 0.95,
			$"Expected in-place compression to shrink the file below 95% of {bigOriginalSize}, got {bigCompressedSize}.");
		Assert.True(GhostscriptRunner.IsValidPdfStructure(bigPath));

		// Tiny PDF untouched (kept-no-benefit).
		Assert.Equal(smallOriginalBytes, File.ReadAllBytes(smallPath));

		// Locked PDF untouched.
		Assert.Equal(lockedOriginalBytes, File.ReadAllBytes(lockedPath));
		_fx.AssertStillEncrypted(lockedPath);
	}

	[Fact]
	public void Empty_tree_succeeds_with_zero_counts()
	{
		string root = _fx.MakeIsolatedDirectory("recursive-compress-empty");

		var (exitCode, summary) = RunCompressCapturingSummary(root);

		Assert.Equal(0, exitCode);
		Assert.Contains("Scanned:             0", summary);
		Assert.Contains("Replaced:            0", summary);
	}

	[Fact]
	public void Compressed_files_preserve_original_timestamps()
	{
		string root = _fx.MakeIsolatedDirectory("recursive-compress-timestamps");
		string target = _fx.CopyFixture(_fx.LargeBasePdf, root, "preserve-me.pdf");

		var stamp = new DateTime(2008, 4, 1, 9, 8, 7, DateTimeKind.Local);
		File.SetCreationTime(target, stamp);
		File.SetLastWriteTime(target, stamp);

		var (exitCode, summary) = RunCompressCapturingSummary(root);

		Assert.Equal(0, exitCode);
		Assert.Contains("Replaced:            1", summary);
		Assert.Equal(stamp, File.GetLastWriteTime(target));
	}

	private (int exitCode, string summary) RunCompressCapturingSummary(string rootDirectory)
	{
		var originalOut = Console.Out;
		using var capture = new StringWriter();
		Console.SetOut(capture);
		try
		{
			int exitCode = new RecursivePdfCompressor().CompressTree(rootDirectory, _fx.GhostscriptPath);
			return (exitCode, capture.ToString());
		}
		finally
		{
			Console.SetOut(originalOut);
		}
	}
}
