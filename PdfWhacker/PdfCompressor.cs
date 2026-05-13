namespace PdfWhacker;

public class PdfCompressor
{
	private const double SizeRatioThreshold = 0.95;
	private static readonly TimeSpan FileReadyTimeout = TimeSpan.FromMinutes(2);

	public void CompressFile(
		string inputFilePath,
		string outputFolderPath,
		string processedOriginalFolderPath,
		string ghostscriptPath)
	{
		string inputFileName = Path.GetFileName(inputFilePath);
		try
		{
			Console.WriteLine();
			Console.WriteLine("-------------------------");
			Console.WriteLine($"Compressing file: {inputFileName}");

			if (!FileReadyWaiter.TryWait(inputFilePath, FileReadyTimeout))
			{
				Console.WriteLine($"Input file not available or never became readable: {inputFilePath}");
				return;
			}

			string outputFilePath = Path.Combine(outputFolderPath, inputFileName);
			string processedOriginalFilePath = Path.Combine(processedOriginalFolderPath, inputFileName);

			long originalSize = new FileInfo(inputFilePath).Length;
			if (originalSize == 0)
			{
				Console.WriteLine("Empty input file; leaving in place.");
				return;
			}

			// Invariant: archive a copy of the original before invoking Ghostscript so
			// any failure path (gs crash, bad output, encrypted, no benefit) can fall
			// back to a known-good copy without touching the input file again.
			File.Copy(inputFilePath, processedOriginalFilePath, overwrite: true);

			GhostscriptResult result;
			try
			{
				result = GhostscriptRunner.Compress(ghostscriptPath, inputFilePath, outputFilePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ghostscript invocation failed: {ex.Message}");
				CopyOriginalToOutput(processedOriginalFilePath, outputFilePath, "ghostscript invocation failed");
				PdfFs.SafeDelete(inputFilePath);
				return;
			}

			var outcome = PdfPipeline.Classify(result, outputFilePath);
			switch (outcome)
			{
				case GhostscriptOutcome.EncryptedNoMatch:
					Console.WriteLine("PDF is password-protected; copying original to output.");
					CopyOriginalToOutput(processedOriginalFilePath, outputFilePath, reason: null);
					PdfFs.SafeDelete(inputFilePath);
					return;

				case GhostscriptOutcome.TimedOut:
					Console.WriteLine("Ghostscript timed out; copying original to output.");
					CopyOriginalToOutput(processedOriginalFilePath, outputFilePath, reason: null);
					PdfFs.SafeDelete(inputFilePath);
					return;

				case GhostscriptOutcome.Failed:
					Console.WriteLine($"Ghostscript exited with code {result.ExitCode}; copying original to output.");
					if (!string.IsNullOrWhiteSpace(result.StandardError))
						Console.WriteLine($"Stderr: {result.StandardError.Trim()}");
					CopyOriginalToOutput(processedOriginalFilePath, outputFilePath, reason: null);
					PdfFs.SafeDelete(inputFilePath);
					return;

				case GhostscriptOutcome.MissingOutput:
				case GhostscriptOutcome.EmptyOutput:
				case GhostscriptOutcome.InvalidStructure:
					Console.WriteLine("Ghostscript output missing or failed structural validation; copying original to output.");
					CopyOriginalToOutput(processedOriginalFilePath, outputFilePath, reason: null);
					PdfFs.SafeDelete(inputFilePath);
					return;
			}

			long compressedSize = new FileInfo(outputFilePath).Length;
			double ratio = (double)compressedSize / originalSize;

			if (ratio > SizeRatioThreshold)
			{
				Console.WriteLine($"Compression saved <{(1 - SizeRatioThreshold) * 100:F0}%; copying original to output.");
				CopyOriginalToOutput(processedOriginalFilePath, outputFilePath, reason: null);
			}
			else
			{
				Console.WriteLine($"Original:   {originalSize:N0} bytes");
				Console.WriteLine($"Compressed: {compressedSize:N0} bytes ({ratio * 100:F2}% of original)");
			}

			PdfFs.SafeDelete(inputFilePath);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error processing file for compression: {inputFileName}");
			Console.WriteLine($"Stack trace: {ex}");
		}
	}

	private static void CopyOriginalToOutput(string originalCopyPath, string outputFilePath, string? reason)
	{
		if (!string.IsNullOrEmpty(reason))
			Console.WriteLine($"Falling back to original ({reason}).");
		File.Copy(originalCopyPath, outputFilePath, overwrite: true);
	}
}
