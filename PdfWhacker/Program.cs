using PdfWhacker;

if (args.Length < 2)
{
	Console.WriteLine("Incorrect arguments. Required:");
	Console.WriteLine("Usage: PdfCompressor <working folder path> <ghostscript executable path>");
	Console.WriteLine("Press any key to quit.");
	return;
}

string workingFolderPath = args[0];
string ghostscriptExecutablePath = args[1];

string compressionInputFolderPath = Path.Combine(workingFolderPath, "CompressionInput");
string compressionProcessedFolderPath = Path.Combine(workingFolderPath, "CompressionOriginal");
string compressionOutputFolderPath = Path.Combine(workingFolderPath, "CompressionOutput");

Directory.CreateDirectory(compressionInputFolderPath);
Directory.CreateDirectory(compressionProcessedFolderPath);
Directory.CreateDirectory(compressionOutputFolderPath);

if (!File.Exists(ghostscriptExecutablePath))
{
	throw new FileNotFoundException("Ghostscript executable not found.", ghostscriptExecutablePath);
}

CompressExistingFiles(compressionInputFolderPath, compressionOutputFolderPath, compressionProcessedFolderPath, ghostscriptExecutablePath);

var watcher = new FileSystemWatcher(compressionInputFolderPath)
{
	NotifyFilter = NotifyFilters.FileName,
	Filter = "*.pdf"
};

watcher.Created += (sender, e) =>
{
	e.FullPath.WaitForFileToBeReady();
	new PdfCompressor().CompressFile(e.FullPath, compressionOutputFolderPath, compressionProcessedFolderPath, ghostscriptExecutablePath);
};

watcher.EnableRaisingEvents = true;

Console.WriteLine("");
Console.WriteLine("Watching for new PDF files in " + Path.GetFullPath(compressionInputFolderPath));
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
		new PdfCompressor().CompressFile(filePath, outputFolder, processedFolder, gsPath);
	}
}

double GetPDFCompatibilityVersion(
	string pdfFilePath)
{
	throw new NotImplementedException();
}




