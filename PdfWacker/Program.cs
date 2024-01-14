﻿using System.Diagnostics;

if (args.Length < 3)
{
	Console.WriteLine("Incorrect arguments. Required:");
	Console.WriteLine("Usage: PdfCompressor <input folder path> <output folder path> <ghostscript executable path>");
	Console.WriteLine("Press any key to quit.");
	return;
}

string inputFolderPath = args[0];
string outputFolderPath = args[1];
string ghostscriptExecutablePath = args[2];

string inputToCompressFolderPath = Path.Combine(inputFolderPath, "ToCompress");
string inputToCompressProcessedFolderPath = Path.Combine(inputToCompressFolderPath, "Processed");
string outputCompressedFolderPath = Path.Combine(outputFolderPath, "Compressed");

Directory.CreateDirectory(inputToCompressProcessedFolderPath);
Directory.CreateDirectory(outputCompressedFolderPath);

if (!File.Exists(ghostscriptExecutablePath))
{
	throw new FileNotFoundException("Ghostscript executable not found.", ghostscriptExecutablePath);
}

CompressExistingFiles(inputToCompressFolderPath, outputCompressedFolderPath, inputToCompressProcessedFolderPath, ghostscriptExecutablePath);

var watcher = new FileSystemWatcher(inputToCompressFolderPath)
{
	NotifyFilter = NotifyFilters.FileName,
	Filter = "*.pdf"
};

watcher.Created += (sender, e) =>
{
	WaitForFileToBeReady(e.FullPath);
	CompressFile(e.FullPath, outputCompressedFolderPath, inputToCompressProcessedFolderPath, ghostscriptExecutablePath);
};

watcher.EnableRaisingEvents = true;

Console.WriteLine("");
Console.WriteLine("Watching for new PDF files in " + inputToCompressFolderPath);
Console.WriteLine("Press any key to quit.");

while (true)
{
	if (Console.KeyAvailable)
	{
		Console.ReadKey();
		break;
	}
	Thread.Sleep(1000); // Sleep for a while before checking again
}

void CompressExistingFiles(string inputFolder, string outputFolder, string processedFolder, string gsPath)
{
	foreach (var filePath in Directory.EnumerateFiles(inputFolder, "*.pdf"))
	{
		CompressFile(filePath, outputFolder, processedFolder, gsPath);
	}
}

void CompressFile(
	string filePath,
	string outputFolderPath,
	string processedFolderPath,
	string ghostscriptPath)
{
	try
	{
		if (!File.Exists(filePath))
		{
			Console.WriteLine($"File not found: {filePath}");
			return;
		}

		string fileName = Path.GetFileName(filePath);
		Console.WriteLine("");
		Console.WriteLine("-------------------------");
		Console.WriteLine($"Compressing file: {fileName}");

		WaitForFileToBeReady(filePath);

		string outputFilePath = Path.Combine(outputFolderPath, fileName);
		string processedFilePath = Path.Combine(processedFolderPath, fileName);

		var originalSize = new FileInfo(filePath).Length;

		// Set up and start the Ghostscript process
		ProcessStartInfo psi = new ProcessStartInfo
		{
			FileName = ghostscriptPath,
			Arguments = $"-sDEVICE=pdfwrite -dCompatibilityLevel=2.0 -dPDFSETTINGS=/ebook -dNOPAUSE -dQUIET -dBATCH -sOutputFile=\"{outputFilePath}\" \"{filePath}\"",
			UseShellExecute = false,
			CreateNoWindow = true
		};
		using (Process process = Process.Start(psi))
		{
			process.WaitForExit();
		}

		// Get compressed file size
		var compressedSize = new FileInfo(outputFilePath).Length;
		double compressionRatio = (double)compressedSize / originalSize * 100;

		// Check if effective compression was possible
		if (compressionRatio > 95.0)
		{
			Console.WriteLine("Effective compression not possible, copying original file.");
			File.Copy(filePath, outputFilePath, true); // Replace the file in the output folder
		}
		else
		{
			Console.WriteLine($"{originalSize} bytes - Original Size");
			Console.WriteLine($"{compressedSize} bytes - Compressed Size");
			Console.WriteLine($"{compressionRatio:F2} % of original size.");
		}

		if (File.Exists(processedFilePath))
		{
			File.Delete(processedFilePath);
		}
		File.Move(filePath, processedFilePath);
	}
	catch (Exception ex)
	{
		Console.WriteLine($"Error processing file {Path.GetFileName(filePath)}");
		Console.WriteLine($"Stack Trace: {ex.ToString()}");
	}
}

void WaitForFileToBeReady(string filePath)
{
	while (!(IsFileReady(filePath)))
	{
		Thread.Sleep(250);
	}
}

bool IsFileReady(string filePath)
{
	if (!File.Exists(filePath))
		throw new FileNotFoundException("The file to process cannot be found.", filePath);

	// If the file can be opened for exclusive access it means that the file
	// is no longer locked by another process.
	try
	{
		using (FileStream inputStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			return inputStream.Length > 0;
		}
	}
	catch (Exception)
	{
		// The file is unavailable because it is:
		// still being written to
		// or being processed by another thread.
		return false;
	}
}