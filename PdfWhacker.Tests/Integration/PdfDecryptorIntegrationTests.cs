using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(nameof(GhostscriptCollection))]
public class PdfDecryptorIntegrationTests
{
	private readonly IntegrationFixture _fx;

	public PdfDecryptorIntegrationTests(IntegrationFixture fx)
	{
		_fx = fx;
	}

	private (string inputDir, string outputDir, string archiveDir) CreateFolders(string label)
	{
		string testRoot = _fx.MakeIsolatedDirectory(label);
		string inputDir = Path.Combine(testRoot, "DecryptInput");
		string outputDir = Path.Combine(testRoot, "Output");
		string archiveDir = Path.Combine(testRoot, "Original", "Decrypt");
		Directory.CreateDirectory(inputDir);
		Directory.CreateDirectory(outputDir);
		Directory.CreateDirectory(archiveDir);
		return (inputDir, outputDir, archiveDir);
	}

	[Fact]
	public void Unencrypted_pdf_passes_through_to_output_unchanged()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("unencrypted-passthrough");
		string inputPath = _fx.CopyFixture(_fx.BasePdf, inputDir, "doc.pdf");

		new PdfDecryptor().DecryptFile(
			inputPath,
			outputDir,
			archiveDir,
			_fx.GhostscriptPath,
			passwords: Array.Empty<string>());

		string outputPath = Path.Combine(outputDir, "doc.pdf");
		string archivePath = Path.Combine(archiveDir, "doc.pdf");

		Assert.False(File.Exists(inputPath), "Input should be removed after processing.");
		Assert.True(File.Exists(outputPath), "Output file should exist.");
		Assert.True(File.Exists(archivePath), "Archive copy should exist.");
		Assert.True(_fx.FilesContentEqual(outputPath, _fx.BasePdf), "Unencrypted passthrough should be byte-identical to the original.");
		Assert.False(PdfEncryptionDetector.IsEncrypted(outputPath), "Output should not be encrypted.");
	}

	[Fact]
	public void User_password_encrypted_pdf_is_decrypted_when_password_is_configured()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("user-decrypt-success");
		string inputPath = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, inputDir, "locked.pdf");

		new PdfDecryptor().DecryptFile(
			inputPath,
			outputDir,
			archiveDir,
			_fx.GhostscriptPath,
			passwords: new[] { "wrong-first", IntegrationFixture.KnownUserPassword, "wrong-after" });

		string outputPath = Path.Combine(outputDir, "locked.pdf");
		string archivePath = Path.Combine(archiveDir, "locked.pdf");

		Assert.False(File.Exists(inputPath), "Input should be removed after successful decryption.");
		Assert.True(File.Exists(outputPath), "Decrypted output should exist.");
		Assert.True(File.Exists(archivePath), "Archive copy should exist.");
		_fx.AssertDecrypted(outputPath);
		_fx.AssertStillEncrypted(archivePath);
	}

	[Fact]
	public void User_password_encrypted_pdf_falls_through_when_no_password_matches()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("user-decrypt-no-match");
		string inputPath = _fx.CopyFixture(_fx.OtherUserPwdEncryptedPdf, inputDir, "stubborn.pdf");

		new PdfDecryptor().DecryptFile(
			inputPath,
			outputDir,
			archiveDir,
			_fx.GhostscriptPath,
			passwords: new[] { "nope1", "nope2", IntegrationFixture.KnownUserPassword });

		string outputPath = Path.Combine(outputDir, "stubborn.pdf");

		Assert.False(File.Exists(inputPath), "Input should be removed even when no password matches.");
		Assert.True(File.Exists(outputPath), "Output file should exist (original passed through).");
		_fx.AssertStillEncrypted(outputPath);
		Assert.True(_fx.FilesContentEqual(outputPath, _fx.OtherUserPwdEncryptedPdf), "Passthrough output should be byte-identical to the original locked fixture.");
	}

	[Fact]
	public void Owner_only_encrypted_pdf_is_decrypted_via_implicit_empty_password()
	{
		var (inputDir, outputDir, archiveDir) = CreateFolders("owner-only-decrypt");
		string inputPath = _fx.CopyFixture(_fx.OwnerOnlyEncryptedPdf, inputDir, "owner-only.pdf");

		new PdfDecryptor().DecryptFile(
			inputPath,
			outputDir,
			archiveDir,
			_fx.GhostscriptPath,
			passwords: Array.Empty<string>());

		string outputPath = Path.Combine(outputDir, "owner-only.pdf");

		Assert.False(File.Exists(inputPath), "Input should be removed after successful decryption.");
		Assert.True(File.Exists(outputPath), "Decrypted output should exist.");
		_fx.AssertDecrypted(outputPath);
	}

	[Fact]
	public void Repeated_same_named_input_does_not_overwrite_prior_archive_or_output()
	{
		// Same invariant as the compressor: dropping "locked.pdf" twice must preserve
		// both archived originals and both output copies.
		var (inputDir, outputDir, archiveDir) = CreateFolders("decrypt-collision");

		string firstInput = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, inputDir, "dupe.pdf");
		new PdfDecryptor().DecryptFile(
			firstInput, outputDir, archiveDir, _fx.GhostscriptPath,
			passwords: new[] { IntegrationFixture.KnownUserPassword });

		string secondInput = _fx.CopyFixture(_fx.OtherUserPwdEncryptedPdf, inputDir, "dupe.pdf");
		new PdfDecryptor().DecryptFile(
			secondInput, outputDir, archiveDir, _fx.GhostscriptPath,
			passwords: new[] { IntegrationFixture.OtherUserPassword });

		Assert.True(File.Exists(Path.Combine(archiveDir, "dupe.pdf")), "First archive should still exist.");
		Assert.True(File.Exists(Path.Combine(archiveDir, "dupe (2).pdf")), "Second archive should be suffixed.");
		Assert.True(File.Exists(Path.Combine(outputDir, "dupe.pdf")), "First output should still exist.");
		Assert.True(File.Exists(Path.Combine(outputDir, "dupe (2).pdf")), "Second output should be suffixed.");

		Assert.True(_fx.FilesContentEqual(Path.Combine(archiveDir, "dupe.pdf"), _fx.UserPwdEncryptedPdf),
			"First archive should still match the first input bytes.");
	}

	[Fact]
	public void Missing_ghostscript_binary_passes_original_through()
	{
		// Decrypt path exception branch: Process.Start fails, the archived original
		// must still reach Output and the input must still be removed.
		var (inputDir, outputDir, archiveDir) = CreateFolders("decrypt-no-gs");
		string inputPath = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, inputDir, "locked.pdf");
		string bogusGsPath = Path.Combine(_fx.RootDir, "no-such-ghostscript-" + Guid.NewGuid().ToString("N") + ".exe");

		new PdfDecryptor().DecryptFile(
			inputPath,
			outputDir,
			archiveDir,
			bogusGsPath,
			passwords: new[] { IntegrationFixture.KnownUserPassword });

		string outputPath = Path.Combine(outputDir, "locked.pdf");
		string archivePath = Path.Combine(archiveDir, "locked.pdf");

		Assert.False(File.Exists(inputPath));
		Assert.True(File.Exists(outputPath));
		Assert.True(File.Exists(archivePath));
		Assert.True(_fx.FilesContentEqual(outputPath, _fx.UserPwdEncryptedPdf),
			"Passthrough should byte-equal the original encrypted fixture.");
		_fx.AssertStillEncrypted(outputPath);
	}
}
