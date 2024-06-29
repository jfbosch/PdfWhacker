using System.Diagnostics;

namespace PdfWhacker;

public class PdfMerger
{
	public void MergeFiles
	(
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

			var filesToMerge = Directory.EnumerateFiles(inputFolderPath, "*.pdf").ToArray();
			if (filesToMerge.Length < 2)
			{
				Console.WriteLine($"A minimum of 2 files are needed before they can be merged. Found {filesToMerge.Length} in {inputFolderPath}");
				return;
			}

			string outputFilePath = Path.Combine(outputFolderPath, "merged.pdf");

			Console.WriteLine("");
			Console.WriteLine("-------------------------");
			Console.WriteLine($"Merging files:");
			foreach (var filePath in filesToMerge)
			{
				string fileName = Path.GetFileName(filePath);
				Console.WriteLine($"	{fileName}");
				string processedOriginalFilePath = Path.Combine(processedOriginalFolderPath, fileName);
				File.Copy(filePath, processedOriginalFilePath, true);
			}


			bool pdfIsPasswordProtected = false;

			// Concatenate all file paths from filesToMerge array into a single string
			string inputFiles = string.Join(" ", filesToMerge.Select(file => $"\"{file}\""));

			// Set up and start the Ghostscript process
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = ghostscriptPath,
				Arguments = $"-dNOPAUSE -sDEVICE=pdfwrite -sOutputFile=\"{outputFilePath}\" -dBATCH {inputFiles}",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
			};

			using (Process mergeProcess = Process.Start(psi))
			{
				string errorOutput = mergeProcess.StandardError.ReadToEnd();

				if (!string.IsNullOrEmpty(errorOutput))
				{
					Console.WriteLine($"Error: {errorOutput}");
					if (errorOutput.Contains("This file requires a password for access", StringComparison.InvariantCultureIgnoreCase))
						pdfIsPasswordProtected = true;
				}
				mergeProcess.WaitForExit();
			}

			if (pdfIsPasswordProtected)
			{
				Console.WriteLine("Unable to merge because one of the PDF files is password protected; leaving things as is.");
				return;
			}
			else if (!File.Exists(outputFilePath))
			{
				Console.WriteLine("Unable to merge due to unexpected error; leaving things as is.");
				return;
			}

			var mergedSize = new FileInfo(outputFilePath).Length;
			Console.WriteLine($"Merged {filesToMerge.Length} files into {Path.GetFileName(outputFilePath)}. Size: {mergedSize} bytes.");

			foreach (var filePath in filesToMerge)
				if (File.Exists(filePath))
					File.Delete(filePath);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error processing file  for merge {Path.GetFileName(inputFolderPath)}");
			Console.WriteLine($"Stack Trace: {ex.ToString()}");
		}
	}
}
