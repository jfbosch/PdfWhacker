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

			var filesToMerge = EnumeratePdfs(inputFolderPath).ToArray();
			if (filesToMerge.Length < 2)
			{
				Console.WriteLine($"A minimum of 2 files are needed before they can be merged. Found {filesToMerge.Length} in {inputFolderPath}");
				return;
			}

			string outputFileName = $"merged-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";
			string outputFilePath = Path.Combine(outputFolderPath, outputFileName);

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

			if (result.PasswordProtected)
			{
				Console.WriteLine("Unable to merge because one of the PDF files is password protected; leaving things as is.");
				SafeDelete(outputFilePath);
				return;
			}

			if (!result.Succeeded)
			{
				Console.WriteLine(result.TimedOut
					? "Ghostscript timed out; leaving things as is."
					: $"Ghostscript exited with code {result.ExitCode}; leaving things as is.");
				if (!string.IsNullOrWhiteSpace(result.StandardError))
					Console.WriteLine($"Stderr: {result.StandardError.Trim()}");
				SafeDelete(outputFilePath);
				return;
			}

			if (!File.Exists(outputFilePath) || !GhostscriptRunner.IsValidPdfStructure(outputFilePath))
			{
				Console.WriteLine("Merge output missing or failed structural validation; leaving things as is.");
				SafeDelete(outputFilePath);
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

	private static IEnumerable<string> EnumeratePdfs(string folder) =>
		Directory.EnumerateFiles(folder, "*.pdf")
			.Where(p => Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase));

	private static void SafeDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch
		{
			// best effort
		}
	}
}
