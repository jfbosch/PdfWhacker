using System.Diagnostics;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
public class ProgramCliTests
{
	// Walks up from the test bin folder until it finds PdfWhacker\bin\<config>\<tfm>\PdfWhacker.dll
	// (or .exe on Windows). Built lazily once per test run.
	private static readonly Lazy<string> PdfWhackerEntry = new(LocatePdfWhackerEntry);

	private static string LocatePdfWhackerEntry()
	{
		string testAsmDir = AppContext.BaseDirectory; // .../PdfWhacker.Tests/bin/<config>/<tfm>/
		string? config = new DirectoryInfo(testAsmDir).Parent?.Name;          // Debug | Release
		string? tfm = new DirectoryInfo(testAsmDir).Name;                      // net10.0
		string? probe = testAsmDir;
		for (int i = 0; i < 8 && probe is not null; i++)
		{
			if (!string.IsNullOrEmpty(config) && !string.IsNullOrEmpty(tfm))
			{
				string candidate = Path.Combine(probe, "PdfWhacker", "bin", config, tfm, "PdfWhacker.dll");
				if (File.Exists(candidate))
					return candidate;
			}
			probe = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}
		Assert.Fail($"Could not locate PdfWhacker.dll relative to {testAsmDir}.");
		throw new InvalidOperationException("unreachable");
	}

	private static (int exitCode, string stdout, string stderr) RunCli(params string[] args)
	{
		var psi = new ProcessStartInfo
		{
			FileName = "dotnet",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		psi.ArgumentList.Add(PdfWhackerEntry.Value);
		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		using var p = Process.Start(psi)!;
		var stdoutTask = p.StandardOutput.ReadToEndAsync();
		var stderrTask = p.StandardError.ReadToEndAsync();
		if (!p.WaitForExit(60_000))
		{
			try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
			Assert.Fail("CLI invocation timed out.");
		}
		return (p.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
	}

	[Fact]
	public void No_args_prints_usage_and_returns_nonzero()
	{
		var (exitCode, stdout, _) = RunCli();
		Assert.Equal(1, exitCode);
		Assert.Contains("Usage:", stdout);
		Assert.Contains("watch", stdout);
		Assert.Contains("compress", stdout);
		Assert.Contains("decrypt", stdout);
	}

	[Fact]
	public void Unknown_subcommand_prints_usage_and_returns_nonzero()
	{
		var (exitCode, stdout, _) = RunCli("does-not-exist");
		Assert.Equal(1, exitCode);
		Assert.Contains("Usage:", stdout);
	}

	[Fact]
	public void Compress_with_missing_args_returns_nonzero()
	{
		var (exitCode, stdout, _) = RunCli("compress");
		Assert.Equal(1, exitCode);
		Assert.Contains("Usage: PdfWhacker compress", stdout);
	}

	[Fact]
	public void Compress_with_missing_directory_returns_nonzero()
	{
		string bogusDir = Path.Combine(Path.GetTempPath(), "pdfwhacker-cli-bogus-" + Guid.NewGuid().ToString("N"));
		var (exitCode, stdout, _) = RunCli("compress", bogusDir, "some-gs.exe");
		Assert.Equal(1, exitCode);
		Assert.Contains("Directory not found", stdout);
	}

	[Fact]
	public void Compress_with_missing_ghostscript_returns_nonzero()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pdfwhacker-cli-ok-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			string bogusGs = Path.Combine(dir, "no-such-gs.exe");
			var (exitCode, stdout, _) = RunCli("compress", dir, bogusGs);
			Assert.Equal(1, exitCode);
			Assert.Contains("Ghostscript executable not found", stdout);
		}
		finally
		{
			try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
		}
	}

	[Fact]
	public void Decrypt_with_missing_args_returns_nonzero()
	{
		var (exitCode, stdout, _) = RunCli("decrypt");
		Assert.Equal(1, exitCode);
		Assert.Contains("Usage: PdfWhacker decrypt", stdout);
	}

	[Fact]
	public void Watch_with_missing_ghostscript_returns_nonzero()
	{
		string bogusGs = Path.Combine(Path.GetTempPath(), "no-such-gs-" + Guid.NewGuid().ToString("N") + ".exe");
		var (exitCode, stdout, _) = RunCli("watch", Path.GetTempPath(), bogusGs);
		Assert.Equal(1, exitCode);
		Assert.Contains("Ghostscript executable not found", stdout);
	}
}
