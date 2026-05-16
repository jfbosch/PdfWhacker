using System.Diagnostics;
using System.Text;
using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

// DisableParallelization keeps tests in this collection from running concurrently
// with non-integration tests. The recursive-pipeline tests swap Console.Out to
// capture summaries, and Console.Out is global — any other test that logs while
// the capture is active would corrupt the assertions.
[CollectionDefinition(nameof(GhostscriptCollection), DisableParallelization = true)]
public sealed class GhostscriptCollection : ICollectionFixture<IntegrationFixture>
{
	// Marker only — collection assembly + class fixture wiring per xUnit v3 convention.
}

public sealed class IntegrationFixture : IDisposable
{
	public const string GhostscriptEnvVar = "PDFWHACKER_GHOSTSCRIPT";
	public const string KnownUserPassword = "hunter2";
	public const string KnownOwnerPassword = "owner1";
	public const string OtherUserPassword = "different-password";

	public string GhostscriptPath { get; }
	public string RootDir { get; }
	public string SourcePostScript { get; }
	public string BasePdf { get; }
	public string LargeBasePdf { get; }
	public string UserPwdEncryptedPdf { get; }
	public string OwnerOnlyEncryptedPdf { get; }
	public string OtherUserPwdEncryptedPdf { get; }

	public IntegrationFixture()
	{
		GhostscriptPath = DiscoverGhostscript();

		RootDir = Path.Combine(
			Path.GetTempPath(),
			"pdfwhacker-integration-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(RootDir);

		SourcePostScript = LocateSamplePostScript();
		BasePdf = Path.Combine(RootDir, "base.pdf");
		LargeBasePdf = Path.Combine(RootDir, "large-base.pdf");
		UserPwdEncryptedPdf = Path.Combine(RootDir, "user-encrypted.pdf");
		OwnerOnlyEncryptedPdf = Path.Combine(RootDir, "owner-only-encrypted.pdf");
		OtherUserPwdEncryptedPdf = Path.Combine(RootDir, "other-user-encrypted.pdf");

		RenderBasePdf();
		File.Copy(LocateSamplePdf("text_graphic_image.pdf"), LargeBasePdf, overwrite: true);
		RenderEncryptedVariant(BasePdf, UserPwdEncryptedPdf, userPassword: KnownUserPassword, ownerPassword: KnownOwnerPassword);
		RenderEncryptedVariant(BasePdf, OwnerOnlyEncryptedPdf, userPassword: null, ownerPassword: KnownOwnerPassword);
		RenderEncryptedVariant(BasePdf, OtherUserPwdEncryptedPdf, userPassword: OtherUserPassword, ownerPassword: KnownOwnerPassword);

		AssertFixtureSanity();
	}

	public void Dispose()
	{
		try { Directory.Delete(RootDir, recursive: true); } catch { /* best effort */ }
	}

	public string MakeIsolatedDirectory(string label)
	{
		string path = Path.Combine(RootDir, $"test-{label}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	public string CopyFixture(string fixturePath, string destDir, string? newName = null)
	{
		string fileName = newName ?? Path.GetFileName(fixturePath);
		string destPath = Path.Combine(destDir, fileName);
		File.Copy(fixturePath, destPath, overwrite: true);
		return destPath;
	}

	public void AssertDecrypted(string path)
	{
		Assert.True(File.Exists(path), $"Expected output file at '{path}'.");
		Assert.False(PdfEncryptionDetector.IsEncrypted(path), $"PdfEncryptionDetector reports '{path}' as still encrypted.");
		Assert.True(GhostscriptRunner.IsValidPdfStructure(path), $"Output '{path}' is not a structurally valid PDF.");

		var probe = ProbeWithGhostscript(path, password: null);
		Assert.False(probe.PasswordProtected, $"Ghostscript probe reports '{path}' as still password-protected. Stderr: {probe.StandardError}");
		Assert.True(probe.Succeeded, $"Ghostscript probe failed on '{path}'. ExitCode={probe.ExitCode} Stderr={probe.StandardError}");
	}

	public void AssertStillEncrypted(string path)
	{
		Assert.True(File.Exists(path), $"Expected file at '{path}'.");
		Assert.True(PdfEncryptionDetector.IsEncrypted(path), $"PdfEncryptionDetector reports '{path}' as not encrypted.");

		var probe = ProbeWithGhostscript(path, password: null);
		Assert.True(probe.PasswordProtected, $"Ghostscript probe reports '{path}' as not password-protected. Stderr: {probe.StandardError}");
	}

	public bool FilesContentEqual(string a, string b)
	{
		var bytesA = File.ReadAllBytes(a);
		var bytesB = File.ReadAllBytes(b);
		return bytesA.AsSpan().SequenceEqual(bytesB);
	}

	/// <summary>
	/// Probes a PDF's page count via Ghostscript's PostScript runtime. Lets tests
	/// assert that a compressed or merged output preserves content shape — not just
	/// that the file has %%EOF and parses. Throws if Ghostscript can't open the
	/// file or returns a non-integer result.
	/// </summary>
	public int GetPageCount(string pdfPath)
	{
		string psPath = pdfPath.Replace('\\', '/');
		var psi = new ProcessStartInfo
		{
			FileName = GhostscriptPath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		};
		psi.ArgumentList.Add("-q");
		psi.ArgumentList.Add("-dNODISPLAY");
		psi.ArgumentList.Add("-dNOSAFER");
		psi.ArgumentList.Add("-c");
		psi.ArgumentList.Add($"({psPath}) (r) file runpdfbegin pdfpagecount = quit");

		using var process = Process.Start(psi)
			?? throw new InvalidOperationException($"Failed to start Ghostscript page-count probe for '{pdfPath}'.");

		var stdoutTask = process.StandardOutput.ReadToEndAsync();
		var stderrTask = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(30_000))
		{
			try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
			throw new InvalidOperationException($"Ghostscript page-count probe timed out for '{pdfPath}'.");
		}
		string stdout = stdoutTask.GetAwaiter().GetResult();
		string stderr = stderrTask.GetAwaiter().GetResult();

		if (process.ExitCode != 0)
			throw new InvalidOperationException(
				$"Ghostscript page-count probe failed for '{pdfPath}'. ExitCode={process.ExitCode}\nStdout: {stdout}\nStderr: {stderr}");

		if (!int.TryParse(stdout.Trim(), out int count))
			throw new InvalidOperationException(
				$"Ghostscript page-count probe returned non-integer for '{pdfPath}'. Stdout: '{stdout}' Stderr: '{stderr}'");

		return count;
	}

	private GhostscriptResult ProbeWithGhostscript(string inputPath, string? password)
	{
		string probeOutput = Path.Combine(RootDir, $"probe-{Guid.NewGuid():N}.pdf");
		try
		{
			return GhostscriptRunner.Decrypt(
				GhostscriptPath,
				inputPath,
				probeOutput,
				password ?? string.Empty);
		}
		finally
		{
			try { if (File.Exists(probeOutput)) File.Delete(probeOutput); } catch { /* best effort */ }
		}
	}

	private static string DiscoverGhostscript()
	{
		string? overridePath = Environment.GetEnvironmentVariable(GhostscriptEnvVar);
		if (!string.IsNullOrWhiteSpace(overridePath))
		{
			if (!File.Exists(overridePath))
				throw new InvalidOperationException(
					$"Environment variable {GhostscriptEnvVar} is set to '{overridePath}' but the file does not exist.");
			return overridePath!;
		}

		string? probe = AppContext.BaseDirectory;
		for (int i = 0; i < 8 && probe is not null; i++)
		{
			string candidate = Path.Combine(probe, "Ghostscript", "bin", "gswin64c.exe");
			if (File.Exists(candidate))
				return candidate;
			probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}

		Assert.Fail(
			$"Ghostscript binary not found. Either set the {GhostscriptEnvVar} environment variable " +
			"to the absolute path of gswin64c.exe, or ensure the Ghostscript/bin/gswin64c.exe folder " +
			"is present at the repository root. Starting search dir: " + AppContext.BaseDirectory);
		throw new InvalidOperationException("unreachable");
	}

	private static string LocateSamplePostScript() => LocateSampleAsset("snowflak.ps");

	private static string LocateSamplePdf(string fileName) => LocateSampleAsset(fileName);

	private static string LocateSampleAsset(string fileName)
	{
		string? probe = AppContext.BaseDirectory;
		for (int i = 0; i < 8 && probe is not null; i++)
		{
			string candidate = Path.Combine(probe, "Ghostscript", "examples", fileName);
			if (File.Exists(candidate))
				return candidate;
			probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}

		Assert.Fail($"Could not locate Ghostscript/examples/{fileName} for fixture generation.");
		throw new InvalidOperationException("unreachable");
	}

	private void RenderBasePdf()
	{
		RunGhostscriptOrFail(new[]
		{
			"-sDEVICE=pdfwrite",
			"-dNOPAUSE",
			"-dBATCH",
			"-dQUIET",
			$"-sOutputFile={BasePdf}",
			SourcePostScript,
		}, $"render base PDF from {SourcePostScript}");
	}

	private void RenderEncryptedVariant(
		string inputPdf,
		string outputPdf,
		string? userPassword,
		string ownerPassword)
	{
		var args = new List<string>
		{
			"-sDEVICE=pdfwrite",
			"-dNOPAUSE",
			"-dBATCH",
			"-dQUIET",
			"-dEncryptionR=3",
			"-dKeyLength=128",
			"-dPermissions=-4",
			$"-sOwnerPassword={ownerPassword}",
		};
		if (!string.IsNullOrEmpty(userPassword))
			args.Add($"-sUserPassword={userPassword}");
		args.Add($"-sOutputFile={outputPdf}");
		args.Add(inputPdf);

		RunGhostscriptOrFail(args, $"render encrypted variant {Path.GetFileName(outputPdf)}");
	}

	private void RunGhostscriptOrFail(IEnumerable<string> args, string description)
	{
		var psi = new ProcessStartInfo
		{
			FileName = GhostscriptPath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		};
		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		using var process = Process.Start(psi)
			?? throw new InvalidOperationException($"Failed to start Ghostscript to {description}.");

		var stderrTask = process.StandardError.ReadToEndAsync();
		var stdoutTask = process.StandardOutput.ReadToEndAsync();
		if (!process.WaitForExit(60_000))
		{
			try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
			throw new InvalidOperationException($"Ghostscript timed out while trying to {description}.");
		}

		string stderr = stderrTask.GetAwaiter().GetResult();
		string stdout = stdoutTask.GetAwaiter().GetResult();
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"Ghostscript failed to {description}. ExitCode={process.ExitCode}\nStdout: {stdout}\nStderr: {stderr}");
		}
	}

	private void AssertFixtureSanity()
	{
		Assert.True(File.Exists(BasePdf), "Base PDF was not generated.");
		Assert.False(PdfEncryptionDetector.IsEncrypted(BasePdf), "Base PDF should not be encrypted.");

		Assert.True(File.Exists(LargeBasePdf), "Large base PDF was not copied.");
		Assert.False(PdfEncryptionDetector.IsEncrypted(LargeBasePdf), "Large base PDF should not be encrypted.");

		Assert.True(File.Exists(UserPwdEncryptedPdf), "User-encrypted PDF was not generated.");
		Assert.True(PdfEncryptionDetector.IsEncrypted(UserPwdEncryptedPdf), "User-encrypted fixture is missing /Encrypt entry.");

		Assert.True(File.Exists(OwnerOnlyEncryptedPdf), "Owner-only encrypted PDF was not generated.");
		Assert.True(PdfEncryptionDetector.IsEncrypted(OwnerOnlyEncryptedPdf), "Owner-only fixture is missing /Encrypt entry.");

		Assert.True(File.Exists(OtherUserPwdEncryptedPdf), "Other-user-encrypted PDF was not generated.");
		Assert.True(PdfEncryptionDetector.IsEncrypted(OtherUserPwdEncryptedPdf), "Other-user-encrypted fixture is missing /Encrypt entry.");
	}
}
