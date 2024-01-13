using System.Diagnostics;

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

if (!File.Exists(ghostscriptExecutablePath))
{
	throw new FileNotFoundException("Ghostscript executable not found.", ghostscriptExecutablePath);
}

string processedFolderPath = Path.Combine(inputFolderPath, "Processed");

Directory.CreateDirectory(processedFolderPath);
Directory.CreateDirectory(outputFolderPath);

ProcessExistingFiles(inputFolderPath, outputFolderPath, processedFolderPath, ghostscriptExecutablePath);

var watcher = new FileSystemWatcher(inputFolderPath)
{
	NotifyFilter = NotifyFilters.FileName,
	Filter = "*.pdf"
};

watcher.Created += (sender, e) =>
{
	// Wait for the file to be fully available
	Thread.Sleep(500);
	ProcessFile(e.FullPath, outputFolderPath, processedFolderPath, ghostscriptExecutablePath);
};

watcher.EnableRaisingEvents = true;

Console.WriteLine("");
Console.WriteLine("Watching for new PDF files in " + inputFolderPath);
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

void ProcessExistingFiles(string inputFolder, string outputFolder, string processedFolder, string gsPath)
{
	foreach (var filePath in Directory.EnumerateFiles(inputFolder, "*.pdf"))
	{
		ProcessFile(filePath, outputFolder, processedFolder, gsPath);
	}
}

void ProcessFile(string filePath, string outputFolderPath, string processedFolderPath, string ghostscriptPath)
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
		Console.WriteLine("-----------------------");
		Console.WriteLine($"Processing file: {fileName}");

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

		// Move original file to processed folder, replace if exists
		Thread.Sleep(50);
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
