using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(nameof(GhostscriptCollection))]
public class RecursivePdfDecryptorIntegrationTests
{
	private readonly IntegrationFixture _fx;

	public RecursivePdfDecryptorIntegrationTests(IntegrationFixture fx)
	{
		_fx = fx;
	}

	[Fact]
	public void Mixed_tree_decrypts_what_it_can_and_skips_the_rest()
	{
		string root = _fx.MakeIsolatedDirectory("recursive-decrypt-mixed");
		string subdir = Path.Combine(root, "subdir");
		Directory.CreateDirectory(subdir);

		string plainPath = _fx.CopyFixture(_fx.BasePdf, root, "plain.pdf");
		string userPath = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, root, "user-locked.pdf");
		string ownerOnlyPath = _fx.CopyFixture(_fx.OwnerOnlyEncryptedPdf, subdir, "owner-only.pdf");
		string undecryptablePath = _fx.CopyFixture(_fx.OtherUserPwdEncryptedPdf, subdir, "stubborn.pdf");

		DateTime plainOriginalTime = File.GetLastWriteTime(plainPath);
		DateTime undecryptableOriginalTime = File.GetLastWriteTime(undecryptablePath);

		var (exitCode, summary) = RunDecryptCapturingSummary(root, new[] { IntegrationFixture.KnownUserPassword });

		Assert.Equal(0, exitCode);

		Assert.Contains("Scanned:                 4", summary);
		Assert.Contains("Decrypted:               2", summary);
		Assert.Contains("Skipped (not encrypted): 1", summary);
		Assert.Contains("Skipped (locked):        1", summary);
		Assert.Contains("Errored:                 0", summary);

		// Plain file untouched: same content, same timestamp.
		Assert.True(_fx.FilesContentEqual(plainPath, _fx.BasePdf));
		Assert.Equal(plainOriginalTime, File.GetLastWriteTime(plainPath));

		// User-encrypted file replaced in place with a decrypted version.
		_fx.AssertDecrypted(userPath);
		Assert.False(_fx.FilesContentEqual(userPath, _fx.UserPwdEncryptedPdf));

		// Owner-only encrypted file decrypted via implicit empty-password attempt.
		_fx.AssertDecrypted(ownerOnlyPath);

		// Undecryptable file left in place, byte-for-byte identical to the original fixture.
		_fx.AssertStillEncrypted(undecryptablePath);
		Assert.True(_fx.FilesContentEqual(undecryptablePath, _fx.OtherUserPwdEncryptedPdf));
		Assert.Equal(undecryptableOriginalTime, File.GetLastWriteTime(undecryptablePath));
	}

	[Fact]
	public void Decrypted_files_preserve_original_timestamps()
	{
		string root = _fx.MakeIsolatedDirectory("recursive-decrypt-timestamps");
		string target = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, root, "preserve-me.pdf");

		var stamp = new DateTime(2010, 6, 15, 12, 34, 56, DateTimeKind.Local);
		File.SetCreationTime(target, stamp);
		File.SetLastWriteTime(target, stamp);

		var (exitCode, _) = RunDecryptCapturingSummary(root, new[] { IntegrationFixture.KnownUserPassword });

		Assert.Equal(0, exitCode);
		_fx.AssertDecrypted(target);
		Assert.Equal(stamp, File.GetLastWriteTime(target));
	}

	[Fact]
	public void Empty_tree_succeeds_with_zero_counts()
	{
		string root = _fx.MakeIsolatedDirectory("recursive-decrypt-empty");

		var (exitCode, summary) = RunDecryptCapturingSummary(root, Array.Empty<string>());

		Assert.Equal(0, exitCode);
		Assert.Contains("Scanned:                 0", summary);
		Assert.Contains("Decrypted:               0", summary);
	}

	[Fact]
	public void Tree_with_no_configured_passwords_only_unlocks_owner_only_files()
	{
		string root = _fx.MakeIsolatedDirectory("recursive-decrypt-no-passwords");
		string ownerOnly = _fx.CopyFixture(_fx.OwnerOnlyEncryptedPdf, root, "owner.pdf");
		string userLocked = _fx.CopyFixture(_fx.UserPwdEncryptedPdf, root, "user.pdf");

		var (exitCode, summary) = RunDecryptCapturingSummary(root, Array.Empty<string>());

		Assert.Equal(0, exitCode);
		Assert.Contains("Decrypted:               1", summary);
		Assert.Contains("Skipped (locked):        1", summary);

		_fx.AssertDecrypted(ownerOnly);
		_fx.AssertStillEncrypted(userLocked);
	}

	private (int exitCode, string summary) RunDecryptCapturingSummary(string rootDirectory, IReadOnlyList<string> passwords)
	{
		var originalOut = Console.Out;
		using var capture = new StringWriter();
		Console.SetOut(capture);
		try
		{
			int exitCode = new RecursivePdfDecryptor().DecryptTree(rootDirectory, _fx.GhostscriptPath, passwords);
			return (exitCode, capture.ToString());
		}
		finally
		{
			Console.SetOut(originalOut);
		}
	}
}
