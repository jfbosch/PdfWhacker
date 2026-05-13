namespace PdfWhacker;

public class RecursivePdfDecryptor
{
	private const string TempFileSuffix = ".pdfwhacker.tmp";
	private const string TempFilePattern = "*" + TempFileSuffix;

	public int DecryptTree(string rootDirectory, string ghostscriptPath, IReadOnlyList<string> passwords)
	{
		var stats = new DecryptionStats();

		Console.WriteLine($"Sweeping stale temp files under {rootDirectory}...");
		int swept = SweepStaleTempFiles(rootDirectory);
		if (swept > 0)
			Console.WriteLine($"Removed {swept} stale temp file(s).");

		Console.WriteLine($"Scanning for PDFs under {rootDirectory}...");
		foreach (var pdfPath in EnumeratePdfs(rootDirectory))
		{
			stats.Scanned++;
			DecryptSingleFileInPlace(pdfPath, ghostscriptPath, passwords, stats);
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
			})
			.Where(p => p.EndsWith(TempFileSuffix, StringComparison.OrdinalIgnoreCase));
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

	private static IEnumerable<string> EnumeratePdfs(string root) =>
		Directory.EnumerateFiles(root, "*.pdf", new EnumerationOptions
		{
			RecurseSubdirectories = true,
			AttributesToSkip = FileAttributes.ReparsePoint,
			IgnoreInaccessible = true,
		})
		.Where(p => Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase));

	private static void DecryptSingleFileInPlace(
		string originalPath,
		string ghostscriptPath,
		IReadOnlyList<string> passwords,
		DecryptionStats stats)
	{
		Console.WriteLine();
		Console.WriteLine("-------------------------");
		Console.WriteLine($"Examining: {originalPath}");

		string tempPath = originalPath + TempFileSuffix;

		try
		{
			if (!TryWaitForExclusiveAccess(originalPath, maxAttempts: 8, delayMs: 250))
			{
				Console.WriteLine("File is locked by another process; skipping.");
				stats.SkippedFileLocked++;
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

			if (!PdfEncryptionDetector.IsEncrypted(originalPath))
			{
				Console.WriteLine("Not encrypted; skipping.");
				stats.SkippedNotEncrypted++;
				return;
			}

			int triedCount = 0;
			foreach (var password in BuildAttemptList(passwords))
			{
				triedCount++;
				GhostscriptResult result;
				try
				{
					result = GhostscriptRunner.Decrypt(ghostscriptPath, originalPath, tempPath, password);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ghostscript invocation failed: {ex.Message}");
					stats.Errored++;
					stats.ErrorDetails.Add((originalPath, $"ghostscript invocation failed: {ex.Message}"));
					return;
				}

				if (result.PasswordProtected)
				{
					SafeDelete(tempPath);
					continue;
				}

				if (!result.Succeeded)
				{
					if (result.TimedOut)
						Console.WriteLine("Ghostscript timed out.");
					else
						Console.WriteLine($"Ghostscript exited with code {result.ExitCode}.");
					if (!string.IsNullOrWhiteSpace(result.StandardError))
						Console.WriteLine($"Stderr: {result.StandardError.Trim()}");
					stats.Errored++;
					stats.ErrorDetails.Add((originalPath,
						result.TimedOut
							? "ghostscript timed out"
							: $"ghostscript exit code {result.ExitCode}: {Truncate(result.StandardError, 200)}"));
					return;
				}

				if (!string.IsNullOrWhiteSpace(result.StandardError))
					Console.WriteLine($"Ghostscript notes: {Truncate(result.StandardError, 200)}");

				if (!File.Exists(tempPath))
				{
					Console.WriteLine("Ghostscript did not produce an output file.");
					stats.Errored++;
					stats.ErrorDetails.Add((originalPath, "no output file produced"));
					return;
				}

				long decryptedSize = new FileInfo(tempPath).Length;
				if (decryptedSize == 0)
				{
					Console.WriteLine("Ghostscript produced an empty output file.");
					stats.Errored++;
					stats.ErrorDetails.Add((originalPath, "output file is zero bytes"));
					return;
				}

				if (!GhostscriptRunner.IsValidPdfStructure(tempPath))
				{
					Console.WriteLine("Output file failed PDF structural validation.");
					stats.Errored++;
					stats.ErrorDetails.Add((originalPath, "output file failed PDF structure check"));
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

				stats.Decrypted++;
				Console.WriteLine(string.IsNullOrEmpty(password)
					? "Decrypted (owner-only encryption — no password required)."
					: $"Decrypted on attempt {triedCount}.");
				return;
			}

			Console.WriteLine($"Tried {triedCount} password(s); none matched. Leaving original in place.");
			stats.SkippedLocked++;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Unexpected error: {ex.Message}");
			stats.Errored++;
			stats.ErrorDetails.Add((originalPath, $"unexpected: {ex.Message}"));
		}
		finally
		{
			SafeDelete(tempPath);
		}
	}

	private static IEnumerable<string> BuildAttemptList(IReadOnlyList<string> passwords)
	{
		yield return string.Empty;
		foreach (var password in passwords)
			yield return password;
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

	private static void SafeDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to clean up temp file '{path}': {ex.Message}");
		}
	}

	private static string Truncate(string s, int maxLen)
	{
		if (string.IsNullOrEmpty(s))
			return string.Empty;
		s = s.Replace("\r", " ").Replace("\n", " ").Trim();
		return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
	}

	private static void PrintSummary(DecryptionStats stats)
	{
		Console.WriteLine();
		Console.WriteLine("=========================");
		Console.WriteLine("PDF decryption complete.");
		Console.WriteLine($"  Scanned:                 {stats.Scanned}");
		Console.WriteLine($"  Decrypted:               {stats.Decrypted}");
		Console.WriteLine($"  Skipped (not encrypted): {stats.SkippedNotEncrypted}");
		Console.WriteLine($"  Skipped (locked):        {stats.SkippedLocked}");
		Console.WriteLine($"  Skipped (file in use):   {stats.SkippedFileLocked}");
		Console.WriteLine($"  Errored:                 {stats.Errored}");

		if (stats.ErrorDetails.Count > 0)
		{
			Console.WriteLine();
			Console.WriteLine("Errored files:");
			foreach (var (path, reason) in stats.ErrorDetails)
				Console.WriteLine($"  {path}  —  {reason}");
		}
	}

	private class DecryptionStats
	{
		public int Scanned;
		public int Decrypted;
		public int SkippedNotEncrypted;
		public int SkippedLocked;
		public int SkippedFileLocked;
		public int Errored;
		public List<(string path, string reason)> ErrorDetails = new();
	}
}
