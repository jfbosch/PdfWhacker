namespace PdfWhacker;

public class PdfDecryptor
{
	private static readonly TimeSpan FileReadyTimeout = TimeSpan.FromMinutes(2);

	public void DecryptFile(
		string inputFilePath,
		string outputFolderPath,
		string processedOriginalFolderPath,
		string ghostscriptPath,
		IReadOnlyList<string> passwords)
	{
		string inputFileName = Path.GetFileName(inputFilePath);
		try
		{
			Console.WriteLine();
			Console.WriteLine("-------------------------");
			Console.WriteLine($"Decrypting file: {inputFileName}");

			if (!FileReadyWaiter.TryWait(inputFilePath, FileReadyTimeout))
			{
				Console.WriteLine($"Input file not available or never became readable: {inputFilePath}");
				return;
			}

			var (processedOriginalFilePath, outputFilePath) =
				PdfFs.BuildUniquePathPair(processedOriginalFolderPath, outputFolderPath, inputFileName);

			long originalSize = new FileInfo(inputFilePath).Length;
			if (originalSize == 0)
			{
				Console.WriteLine("Empty input file; leaving in place.");
				return;
			}

			File.Copy(inputFilePath, processedOriginalFilePath, overwrite: true);

			if (!PdfEncryptionDetector.IsEncrypted(inputFilePath))
			{
				Console.WriteLine("PDF is not encrypted; passing original through.");
				PassOriginalThrough(processedOriginalFilePath, outputFilePath);
				PdfFs.SafeDelete(inputFilePath);
				return;
			}

			var attempts = BuildAttemptList(passwords);
			int triedCount = 0;
			foreach (var password in attempts)
			{
				triedCount++;
				GhostscriptResult result;
				try
				{
					result = GhostscriptRunner.Decrypt(ghostscriptPath, inputFilePath, outputFilePath, password);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Ghostscript invocation failed: {ex.Message}");
					PassOriginalThrough(processedOriginalFilePath, outputFilePath, "ghostscript invocation failed");
					PdfFs.SafeDelete(inputFilePath);
					return;
				}

				var outcome = PdfPipeline.Classify(result, outputFilePath);

				if (outcome == GhostscriptOutcome.EncryptedNoMatch)
					continue; // try next password

				if (outcome == GhostscriptOutcome.Ok)
				{
					Console.WriteLine(string.IsNullOrEmpty(password)
						? "Decrypted with no password (owner-only encryption)."
						: $"Decrypted on attempt {triedCount}.");
					PdfFs.SafeDelete(inputFilePath);
					return;
				}

				WatchOutcomeReporter.TryApplyFallback(
					outcome, result,
					archivePath: processedOriginalFilePath,
					outputPath: outputFilePath,
					inputPath: inputFilePath,
					WatchOutcomeReporter.FallbackVerb.PassThrough);
				return;
			}

			Console.WriteLine($"Tried {triedCount} password(s); none matched. Passing original through.");
			PassOriginalThrough(processedOriginalFilePath, outputFilePath);
			PdfFs.SafeDelete(inputFilePath);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error processing file for decryption: {inputFileName}");
			Console.WriteLine($"Stack trace: {ex}");
		}
	}

	private static IEnumerable<string> BuildAttemptList(IReadOnlyList<string> passwords)
	{
		yield return string.Empty;
		foreach (var password in passwords)
			yield return password;
	}

	private static void PassOriginalThrough(string originalCopyPath, string outputFilePath, string? reason = null)
	{
		if (!string.IsNullOrEmpty(reason))
			Console.WriteLine($"Falling back to original ({reason}).");
		File.Copy(originalCopyPath, outputFilePath, overwrite: true);
	}
}
