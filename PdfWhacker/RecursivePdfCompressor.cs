using System.Diagnostics;
using System.Text;

namespace PdfWhacker;

public class RecursivePdfCompressor
{
	private const double SizeRatioThreshold = 0.95;
	private const string TempFileSuffix = ".pdfwhacker.tmp";
	private const string TempFilePattern = "*" + TempFileSuffix;

	public int CompressTree(string rootDirectory, string ghostscriptPath)
	{
		var stats = new CompressionStats();

		Console.WriteLine($"Sweeping stale temp files under {rootDirectory}...");
		int swept = SweepStaleTempFiles(rootDirectory);
		if (swept > 0)
			Console.WriteLine($"Removed {swept} stale temp file(s).");

		Console.WriteLine($"Scanning for PDFs under {rootDirectory}...");
		foreach (var pdfPath in EnumeratePdfs(rootDirectory))
		{
			stats.Scanned++;
			CompressSingleFileInPlace(pdfPath, ghostscriptPath, stats);
		}

		PrintSummary(stats);
		return stats.Errored > 0 ? 1 : 0;
	}

	private static int SweepStaleTempFiles(string root)
	{
		IEnumerable<string> staleTempFiles;
		try
		{
			staleTempFiles = Directory.EnumerateFiles(root, TempFilePattern, new EnumerationOptions
			{
				RecurseSubdirectories = true,
				AttributesToSkip = FileAttributes.ReparsePoint,
				IgnoreInaccessible = true,
			});
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error scanning for stale temp files: {ex.Message}");
			return 0;
		}

		int count = 0;
		foreach (var path in staleTempFiles)
		{
			try
			{
				File.Delete(path);
				count++;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to delete stale temp file '{path}': {ex.Message}");
			}
		}
		return count;
	}

	private static IEnumerable<string> EnumeratePdfs(string root)
	{
		return Directory.EnumerateFiles(root, "*.pdf", new EnumerationOptions
		{
			RecurseSubdirectories = true,
			AttributesToSkip = FileAttributes.ReparsePoint,
			IgnoreInaccessible = true,
		});
	}

	private static void CompressSingleFileInPlace(string originalPath, string ghostscriptPath, CompressionStats stats)
	{
		Console.WriteLine();
		Console.WriteLine("-------------------------");
		Console.WriteLine($"Compressing: {originalPath}");

		string tempPath = originalPath + TempFileSuffix;

		try
		{
			if (!TryWaitForExclusiveAccess(originalPath, maxAttempts: 8, delayMs: 250))
			{
				Console.WriteLine("File is locked by another process; skipping.");
				stats.SkippedLocked++;
				return;
			}

			var originalInfo = new FileInfo(originalPath);
			long originalSize = originalInfo.Length;
			DateTime originalCreationTime = originalInfo.CreationTime;
			DateTime originalLastWriteTime = originalInfo.LastWriteTime;

			if (originalSize == 0)
			{
				Console.WriteLine("Empty file; classifying as error.");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, "file is zero bytes"));
				return;
			}

			int exitCode;
			string errorOutput;
			try
			{
				(exitCode, errorOutput) = RunGhostscript(ghostscriptPath, originalPath, tempPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ghostscript invocation failed: {ex.Message}");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, $"ghostscript invocation failed: {ex.Message}"));
				return;
			}

			if (errorOutput.Contains("This file requires a password for access", StringComparison.InvariantCultureIgnoreCase))
			{
				Console.WriteLine("PDF is password-protected; keeping original.");
				stats.SkippedEncrypted++;
				return;
			}

			if (exitCode != 0)
			{
				Console.WriteLine($"Ghostscript exited with code {exitCode}.");
				if (!string.IsNullOrWhiteSpace(errorOutput))
					Console.WriteLine($"Stderr: {errorOutput}");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, $"ghostscript exit code {exitCode}: {Truncate(errorOutput, 200)}"));
				return;
			}

			if (!string.IsNullOrWhiteSpace(errorOutput))
			{
				Console.WriteLine($"Ghostscript produced unexpected stderr: {errorOutput}");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, $"ghostscript stderr: {Truncate(errorOutput, 200)}"));
				return;
			}

			if (!File.Exists(tempPath))
			{
				Console.WriteLine("Ghostscript did not produce an output file.");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, "no output file produced"));
				return;
			}

			long compressedSize = new FileInfo(tempPath).Length;
			if (compressedSize == 0)
			{
				Console.WriteLine("Ghostscript produced an empty output file.");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, "output file is zero bytes"));
				return;
			}

			if (!IsValidPdfStructure(tempPath))
			{
				Console.WriteLine("Output file failed PDF structural validation.");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, "output file failed PDF structure check"));
				return;
			}

			double ratio = (double)compressedSize / originalSize;
			Console.WriteLine($"Original: {originalSize:N0} bytes  Compressed: {compressedSize:N0} bytes  Ratio: {ratio * 100:F2}%");

			if (ratio > SizeRatioThreshold)
			{
				Console.WriteLine("Compression did not meet minimum benefit threshold; keeping original.");
				stats.KeptNoBenefit++;
				return;
			}

			try
			{
				File.SetCreationTime(tempPath, originalCreationTime);
				File.SetLastWriteTime(tempPath, originalLastWriteTime);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to preserve timestamps: {ex.Message}");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, $"timestamp preservation failed: {ex.Message}"));
				return;
			}

			try
			{
				File.Move(tempPath, originalPath, overwrite: true);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to replace original file: {ex.Message}");
				stats.Errored++;
				stats.ErrorDetails.Add((originalPath, $"replacement failed: {ex.Message}"));
				return;
			}

			stats.Replaced++;
			stats.TotalOriginalBytes += originalSize;
			stats.TotalCompressedBytes += compressedSize;
			Console.WriteLine("Replaced.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Unexpected error: {ex.Message}");
			stats.Errored++;
			stats.ErrorDetails.Add((originalPath, $"unexpected: {ex.Message}"));
		}
		finally
		{
			try
			{
				if (File.Exists(tempPath))
					File.Delete(tempPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to clean up temp file '{tempPath}': {ex.Message}");
			}
		}
	}

	private static bool TryWaitForExclusiveAccess(string filePath, int maxAttempts, int delayMs)
	{
		for (int i = 0; i < maxAttempts; i++)
		{
			try
			{
				using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
				return stream.Length > 0;
			}
			catch (IOException)
			{
				Thread.Sleep(delayMs);
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}
		return false;
	}

	private static (int exitCode, string errorOutput) RunGhostscript(string ghostscriptPath, string inputPath, string outputPath)
	{
		var psi = new ProcessStartInfo
		{
			FileName = ghostscriptPath,
			Arguments = $"-sDEVICE=pdfwrite -dCompatibilityLevel=1.7 -dPDFSETTINGS=/ebook -dNOPAUSE -dQUIET -dBATCH -sOutputFile=\"{outputPath}\" \"{inputPath}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
		};

		using var process = Process.Start(psi);
		if (process == null)
			throw new InvalidOperationException("Failed to start Ghostscript process.");

		string errorOutput = process.StandardError.ReadToEnd();
		process.WaitForExit();
		return (process.ExitCode, errorOutput);
	}

	private static bool IsValidPdfStructure(string path)
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

	private static string Truncate(string s, int maxLen)
	{
		if (string.IsNullOrEmpty(s))
			return string.Empty;
		s = s.Replace("\r", " ").Replace("\n", " ").Trim();
		return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
	}

	private static void PrintSummary(CompressionStats stats)
	{
		Console.WriteLine();
		Console.WriteLine("=========================");
		Console.WriteLine("PDF compression complete.");
		Console.WriteLine($"  Scanned:             {stats.Scanned}");
		Console.WriteLine($"  Replaced:            {stats.Replaced}");
		Console.WriteLine($"  Kept (no benefit):   {stats.KeptNoBenefit}");
		Console.WriteLine($"  Skipped (locked):    {stats.SkippedLocked}");
		Console.WriteLine($"  Skipped (encrypted): {stats.SkippedEncrypted}");
		Console.WriteLine($"  Errored:             {stats.Errored}");
		Console.WriteLine();

		if (stats.Replaced > 0)
		{
			long saved = stats.TotalOriginalBytes - stats.TotalCompressedBytes;
			double reductionPercent = (double)saved / stats.TotalOriginalBytes * 100;
			Console.WriteLine($"  Total original size:   {FormatBytes(stats.TotalOriginalBytes)}");
			Console.WriteLine($"  Total compressed size: {FormatBytes(stats.TotalCompressedBytes)}");
			Console.WriteLine($"  Total saved:           {FormatBytes(saved)}  ({reductionPercent:F1}% reduction across replaced files)");
		}
		else
		{
			Console.WriteLine("  No files were replaced.");
		}

		if (stats.ErrorDetails.Count > 0)
		{
			Console.WriteLine();
			Console.WriteLine("Errored files:");
			foreach (var (path, reason) in stats.ErrorDetails)
			{
				Console.WriteLine($"  {path}  —  {reason}");
			}
		}
	}

	private static string FormatBytes(long bytes)
	{
		const long KB = 1024;
		const long MB = KB * 1024;
		const long GB = MB * 1024;

		if (bytes >= GB) return $"{(double)bytes / GB:F2} GB";
		if (bytes >= MB) return $"{(double)bytes / MB:F2} MB";
		if (bytes >= KB) return $"{(double)bytes / KB:F1} KB";
		return $"{bytes} B";
	}

	private class CompressionStats
	{
		public int Scanned;
		public int Replaced;
		public int KeptNoBenefit;
		public int SkippedLocked;
		public int SkippedEncrypted;
		public int Errored;
		public long TotalOriginalBytes;
		public long TotalCompressedBytes;
		public List<(string path, string reason)> ErrorDetails = new();
	}
}
