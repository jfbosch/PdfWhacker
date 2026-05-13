using System.Diagnostics;
using System.Text;

namespace PdfWhacker;

public sealed record GhostscriptResult(int ExitCode, string StandardError, bool TimedOut)
{
	// Ghostscript exits 0 even when it can't open an encrypted PDF; the only reliable
	// signal is one of these stderr markers.
	private static readonly string[] PasswordMarkers =
	{
		"This file requires a password for access", // emitted when no password is supplied
		"Password did not work",                    // emitted when -sPDFPassword=... does not match
	};

	public bool Succeeded => !TimedOut && ExitCode == 0;

	public bool PasswordProtected =>
		PasswordMarkers.Any(m => StandardError.Contains(m, StringComparison.InvariantCultureIgnoreCase));
}

public static class GhostscriptRunner
{
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

	public static GhostscriptResult Compress(
		string ghostscriptPath,
		string inputPath,
		string outputPath,
		TimeSpan? timeout = null)
	{
		var psi = BuildBaseStartInfo(ghostscriptPath);
		psi.ArgumentList.Add("-sDEVICE=pdfwrite");
		psi.ArgumentList.Add("-dCompatibilityLevel=1.7");
		psi.ArgumentList.Add("-dPDFSETTINGS=/ebook");
		psi.ArgumentList.Add("-dNOPAUSE");
		psi.ArgumentList.Add("-dQUIET");
		psi.ArgumentList.Add("-dBATCH");
		psi.ArgumentList.Add($"-sOutputFile={NormalizePath(outputPath)}");
		psi.ArgumentList.Add(NormalizePath(inputPath));
		return Run(psi, timeout ?? DefaultTimeout);
	}

	public static GhostscriptResult Decrypt(
		string ghostscriptPath,
		string inputPath,
		string outputPath,
		string password,
		TimeSpan? timeout = null)
	{
		var psi = BuildBaseStartInfo(ghostscriptPath);
		psi.ArgumentList.Add("-sDEVICE=pdfwrite");
		psi.ArgumentList.Add("-dCompatibilityLevel=1.7");
		psi.ArgumentList.Add("-dPDFSETTINGS=/default");
		psi.ArgumentList.Add("-dNOPAUSE");
		psi.ArgumentList.Add("-dQUIET");
		psi.ArgumentList.Add("-dBATCH");
		if (!string.IsNullOrEmpty(password))
			psi.ArgumentList.Add($"-sPDFPassword={password}");
		psi.ArgumentList.Add($"-sOutputFile={NormalizePath(outputPath)}");
		psi.ArgumentList.Add(NormalizePath(inputPath));
		return Run(psi, timeout ?? DefaultTimeout);
	}

	public static GhostscriptResult Merge(
		string ghostscriptPath,
		IReadOnlyList<string> inputPaths,
		string outputPath,
		TimeSpan? timeout = null)
	{
		var psi = BuildBaseStartInfo(ghostscriptPath);
		psi.ArgumentList.Add("-sDEVICE=pdfwrite");
		psi.ArgumentList.Add("-dNOPAUSE");
		psi.ArgumentList.Add("-dBATCH");
		psi.ArgumentList.Add($"-sOutputFile={NormalizePath(outputPath)}");
		foreach (var input in inputPaths)
			psi.ArgumentList.Add(NormalizePath(input));
		return Run(psi, timeout ?? DefaultTimeout);
	}

	public static bool IsValidPdfStructure(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			Span<byte> headerBuffer = stackalloc byte[5];
			int read = stream.Read(headerBuffer);
			if (read < 5)
				return false;
			if (headerBuffer[0] != (byte)'%' ||
				headerBuffer[1] != (byte)'P' ||
				headerBuffer[2] != (byte)'D' ||
				headerBuffer[3] != (byte)'F' ||
				headerBuffer[4] != (byte)'-')
				return false;

			long length = stream.Length;
			int tailSize = (int)Math.Min(1024, length);
			stream.Seek(-tailSize, SeekOrigin.End);
			byte[] tailBuffer = new byte[tailSize];
			int tailRead = stream.Read(tailBuffer, 0, tailSize);
			string tail = Encoding.ASCII.GetString(tailBuffer, 0, tailRead);
			return tail.Contains("%%EOF");
		}
		catch
		{
			return false;
		}
	}

	private static ProcessStartInfo BuildBaseStartInfo(string ghostscriptPath)
	{
		var psi = new ProcessStartInfo
		{
			FileName = ghostscriptPath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
		};
		// -dSAFER restricts PostScript file-system access. Default in Ghostscript 10+,
		// but pass explicitly so older installs are also hardened against malicious PDFs.
		psi.ArgumentList.Add("-dSAFER");
		return psi;
	}

	// Ghostscript has no `--` separator, so a path beginning with `-` would be parsed as
	// a flag. Forcing an absolute path also avoids relying on the runner's CWD.
	private static string NormalizePath(string path) =>
		string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path);

	private static GhostscriptResult Run(ProcessStartInfo psi, TimeSpan timeout)
	{
		using var process = Process.Start(psi)
			?? throw new InvalidOperationException(
				$"Failed to start Ghostscript process at '{psi.FileName}'.");

		var stderrTask = process.StandardError.ReadToEndAsync();

		bool exited = process.WaitForExit((int)timeout.TotalMilliseconds);
		if (!exited)
		{
			try { process.Kill(entireProcessTree: true); }
			catch { /* best effort */ }
			return new GhostscriptResult(-1, $"Ghostscript timed out after {timeout.TotalSeconds:F0}s and was killed.", true);
		}

		process.WaitForExit();
		string stderr = stderrTask.GetAwaiter().GetResult();
		return new GhostscriptResult(process.ExitCode, stderr, false);
	}
}
