using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(nameof(GhostscriptCollection))]
public class PdfMergerIntegrationTests
{
	private readonly IntegrationFixture _fx;

	public PdfMergerIntegrationTests(IntegrationFixture fx)
	{
		_fx = fx;
	}

	private (string inputDir, string outputDir, string archiveDir) CreateFolders(string label)
	{
		string testRoot = _fx.MakeIsolatedDirectory(label);
		string inputDir = Path.Combine(testRoot, "MergeInput");
		string outputDir = Path.Combine(testRoot, "Output");
		string archiveDir = Path.Combine(testRoot, "Original", "Merge");
		Directory.CreateDirectory(inputDir);
		Directory.CreateDirectory(outputDir);
		Directory.CreateDirectory(archiveDir);
		return (inputDir, outputDir, archiveDir);
	}

	[Fact]
	public void Two_unencrypted_pdfs_merge_into_a_single_valid_output()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("merge-happy");
		int aPages = _fx.GetPageCount(_fx.BasePdf);
		int bPages = _fx.GetPageCount(_fx.LargeBasePdf);
		string a = _fx.CopyFixture(_fx.BasePdf, inputDir, "a.pdf");
		string b = _fx.CopyFixture(_fx.LargeBasePdf, inputDir, "b.pdf");

		new PdfMerger().MergeFiles(inputDir, outputDir, archiveDir, _fx.GhostscriptPath);

		var outputs = Directory.GetFiles(outputDir, "merged-*.pdf");
		Assert.Single(outputs);
		string mergedPath = outputs[0];
		Assert.True(GhostscriptRunner.IsValidPdfStructure(mergedPath));
		Assert.False(PdfEncryptionDetector.IsEncrypted(mergedPath));
		Assert.Equal(aPages + bPages, _fx.GetPageCount(mergedPath));

		// Inputs cleared, originals archived.
		Assert.False(File.Exists(a));
		Assert.False(File.Exists(b));
		Assert.True(File.Exists(Path.Combine(archiveDir, "a.pdf")));
		Assert.True(File.Exists(Path.Combine(archiveDir, "b.pdf")));
	}

	[Fact]
	public void Single_input_pdf_does_not_produce_a_merge()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("merge-single");
		string solo = _fx.CopyFixture(_fx.BasePdf, inputDir, "solo.pdf");

		new PdfMerger().MergeFiles(inputDir, outputDir, archiveDir, _fx.GhostscriptPath);

		Assert.Empty(Directory.GetFiles(outputDir, "merged-*.pdf"));
		Assert.True(File.Exists(solo), "Single input should remain untouched when merge is skipped.");
		Assert.Empty(Directory.GetFiles(archiveDir));
	}

	[Fact]
	public void Password_protected_input_blocks_merge_and_leaves_inputs_in_place()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("merge-locked");
		string plain = _fx.CopyFixture(_fx.BasePdf, inputDir, "plain.pdf");
		string locked = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, inputDir, "locked.pdf");

		new PdfMerger().MergeFiles(inputDir, outputDir, archiveDir, _fx.GhostscriptPath);

		Assert.Empty(Directory.GetFiles(outputDir, "merged-*.pdf"));
		Assert.True(File.Exists(plain), "Plain input should remain when a sibling is locked.");
		Assert.True(File.Exists(locked), "Locked input should remain.");
		Assert.Empty(Directory.GetFiles(archiveDir));
	}

	[Fact]
	public void Missing_input_folder_is_handled_gracefully()
	{
		string outputDir = _fx.MakeIsolatedDirectory("merge-missing-output");
		string archiveDir = _fx.MakeIsolatedDirectory("merge-missing-archive");
		string nonExistentInput = Path.Combine(_fx.RootDir, "no-such-merge-input-" + Guid.NewGuid().ToString("N"));

		var ex = Record.Exception(() => new PdfMerger().MergeFiles(nonExistentInput, outputDir, archiveDir, _fx.GhostscriptPath));
		Assert.Null(ex);
		Assert.Empty(Directory.GetFiles(outputDir));
	}

	[Fact]
	public void Two_back_to_back_merges_produce_two_distinct_outputs_and_overwrite_archives_safely()
	{
		// Verifies (a) merge-output filename collision avoidance and (b) that archiving
		// the same filename twice in a row does not corrupt the archived copy.
		var (inputDir, outputDir, archiveDir) = CreateFolders("merge-back-to-back");

		// Run 1
		_fx.CopyFixture(_fx.BasePdf, inputDir, "a.pdf");
		_fx.CopyFixture(_fx.LargeBasePdf, inputDir, "b.pdf");
		new PdfMerger().MergeFiles(inputDir, outputDir, archiveDir, _fx.GhostscriptPath);

		Assert.Single(Directory.GetFiles(outputDir, "merged-*.pdf"));
		Assert.True(File.Exists(Path.Combine(archiveDir, "a.pdf")));
		Assert.True(File.Exists(Path.Combine(archiveDir, "b.pdf")));

		// Run 2 — same filenames again, but different (swapped) content.
		_fx.CopyFixture(_fx.LargeBasePdf, inputDir, "a.pdf");
		_fx.CopyFixture(_fx.BasePdf, inputDir, "b.pdf");
		new PdfMerger().MergeFiles(inputDir, outputDir, archiveDir, _fx.GhostscriptPath);

		var merges = Directory.GetFiles(outputDir, "merged-*.pdf");
		Assert.Equal(2, merges.Length);
		foreach (var m in merges)
			Assert.True(GhostscriptRunner.IsValidPdfStructure(m), $"Merge output {m} should be a valid PDF.");

		// Archive must reflect the *latest* contents (overwrite is intentional behaviour).
		Assert.True(_fx.FilesContentEqual(Path.Combine(archiveDir, "a.pdf"), _fx.LargeBasePdf));
		Assert.True(_fx.FilesContentEqual(Path.Combine(archiveDir, "b.pdf"), _fx.BasePdf));
	}
}
