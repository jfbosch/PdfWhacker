namespace PdfWhacker;

public class PdfMerger
{
	public void MergeFiles(
		string inputFolderPath,
		string outputFolderPath,
		string processedOriginalFolderPath,
		string ghostscriptPath)
	{
		try
		{
			if (!Directory.Exists(inputFolderPath))
			{
				Console.WriteLine($"Input Folder not found: {inputFolderPath}");
				return;
			}

			var filesToMerge = PdfFs.EnumeratePdfs(inputFolderPath, recursive: false).ToArray();
			if (filesToMerge.Length < 2)
			{
				Console.WriteLine($"A minimum of 2 files are needed before they can be merged. Found {filesToMerge.Length} in {inputFolderPath}");
				return;
			}

			string outputFilePath = BuildUniqueMergeOutputPath(outputFolderPath);
			string outputFileName = Path.GetFileName(outputFilePath);

			Console.WriteLine();
			Console.WriteLine("-------------------------");
			Console.WriteLine("Merging files:");
			foreach (var filePath in filesToMerge)
				Console.WriteLine($"  {Path.GetFileName(filePath)}");

			GhostscriptResult result;
			try
			{
				result = GhostscriptRunner.Merge(ghostscriptPath, filesToMerge, outputFilePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ghostscript invocation failed: {ex.Message}");
				return;
			}

			var outcome = PdfPipeline.Classify(result, outputFilePath);
			switch (outcome)
			{
				case GhostscriptOutcome.EncryptedNoMatch:
					Console.WriteLine("Unable to merge because one of the PDF files is password protected; leaving things as is.");
					PdfFs.SafeDelete(outputFilePath);
					return;

				case GhostscriptOutcome.TimedOut:
					Console.WriteLine("Ghostscript timed out; leaving things as is.");
					PdfFs.SafeDelete(outputFilePath);
					return;

				case GhostscriptOutcome.Failed:
					Console.WriteLine($"Ghostscript exited with code {result.ExitCode}; leaving things as is.");
					if (!string.IsNullOrWhiteSpace(result.StandardError))
						Console.WriteLine($"Stderr: {result.StandardError.Trim()}");
					PdfFs.SafeDelete(outputFilePath);
					return;

				case GhostscriptOutcome.MissingOutput:
				case GhostscriptOutcome.EmptyOutput:
				case GhostscriptOutcome.InvalidStructure:
					Console.WriteLine("Merge output missing or failed structural validation; leaving things as is.");
					PdfFs.SafeDelete(outputFilePath);
					return;
			}

			long mergedSize = new FileInfo(outputFilePath).Length;
			Console.WriteLine($"Merged {filesToMerge.Length} files into {outputFileName}. Size: {mergedSize:N0} bytes.");

			// Only after a successful merge do we archive originals and clean inputs.
			foreach (var filePath in filesToMerge)
			{
				string fileName = Path.GetFileName(filePath);
				string archivePath = Path.Combine(processedOriginalFolderPath, fileName);
				try
				{
					File.Copy(filePath, archivePath, overwrite: true);
					File.Delete(filePath);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Failed to archive/remove '{fileName}': {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error processing merge in {inputFolderPath}");
			Console.WriteLine($"Stack trace: {ex}");
		}
	}

	private static string BuildUniqueMergeOutputPath(string outputFolderPath)
	{
		// Millisecond precision plus a defensive collision check makes back-to-back
		// merges (and tests that exercise the same second) safe.
		string baseStamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
		string candidate = Path.Combine(outputFolderPath, $"merged-{baseStamp}.pdf");
		if (!File.Exists(candidate))
			return candidate;
		for (int suffix = 2; suffix < 1000; suffix++)
		{
			candidate = Path.Combine(outputFolderPath, $"merged-{baseStamp}-{suffix}.pdf");
			if (!File.Exists(candidate))
				return candidate;
		}
		// Astronomically unlikely; fall back to a GUID so we never overwrite.
		return Path.Combine(outputFolderPath, $"merged-{baseStamp}-{Guid.NewGuid():N}.pdf");
	}
}
