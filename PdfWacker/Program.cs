using System.Diagnostics;

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
	WaitForFileToBeReady(e.FullPath);
	CompressFile(e.FullPath, compressionOutputFolderPath, compressionProcessedFolderPath, ghostscriptExecutablePath);
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
		CompressFile(filePath, outputFolder, processedFolder, gsPath);
	}
}

double GetPDFCompatibilityVersion(
	string pdfFilePath)
{
	using org.pdfclown.files.File file = new(pdfFilePath);
	var document = file.Document;
	var info = document.Information;
	return double.Parse(file.Version.ToString());
}


void CompressFile(
	string inputFilePath,
	string outputFolderPath,
	string processedOriginalFolderPath,
	string ghostscriptPath)
{
	try
	{
		if (!File.Exists(inputFilePath))
		{
			Console.WriteLine($"Input file not found: {inputFilePath}");
			return;
		}

		string inputFileName = Path.GetFileName(inputFilePath);
		string outputFilePath = Path.Combine(outputFolderPath, inputFileName);
		string processedOriginalFilePath = Path.Combine(processedOriginalFolderPath, inputFileName);

		Console.WriteLine("");
		Console.WriteLine("-------------------------");
		Console.WriteLine($"Compressing file: {inputFileName}");

		WaitForFileToBeReady(inputFilePath);


		//double originalPdfVersion = 0;
		//try
		//{
		//	originalPdfVersion = GetPDFCompatibilityVersion(filePath);
		//}
		//catch (NotImplementedException ex) when (ex.Message.Contains("Encrypted files are currently not supported", StringComparison.InvariantCultureIgnoreCase))
		//{
		//	Console.WriteLine("Unable to compress because PDF is password protected; copying original file to output.");
		//MoveAndReplaceFile(filePath, outputFilePath);
		//}
		//Console.WriteLine($"PDF compatibility version: {originalPdfVersion}");

		var originalSize = new FileInfo(inputFilePath).Length;

		File.Copy(inputFilePath, processedOriginalFilePath, true);

		bool pdfIsPasswordProtected = false;

		// Set up and start the Ghostscript process
		ProcessStartInfo psi = new ProcessStartInfo
		{
			FileName = ghostscriptPath,
			Arguments = $"-sDEVICE=pdfwrite -dCompatibilityLevel=1.7 -dPDFSETTINGS=/ebook -dNOPAUSE -dQUIET -dBATCH -sOutputFile=\"{outputFilePath}\" \"{inputFilePath}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
		};
		using (Process compressionProcess = Process.Start(psi))
		{
			string errorOutput = compressionProcess.StandardError.ReadToEnd();

			if (!string.IsNullOrEmpty(errorOutput))
			{
				Console.WriteLine($"Error: {errorOutput}");
				if (errorOutput.Contains("This file requires a password for access", StringComparison.InvariantCultureIgnoreCase))
					pdfIsPasswordProtected = true;
			}
			compressionProcess.WaitForExit();
		}


		if (pdfIsPasswordProtected)
		{
			Console.WriteLine("Unable to compress because PDF is password protected; copying original file to output.");
			File.Copy(processedOriginalFilePath, outputFilePath, true);
			File.Delete(inputFilePath);
			return;
		}
		else if (!File.Exists(outputFilePath))
		{
			Console.WriteLine("Unable to compress due to unexpected error; copying original file to output.");
			File.Copy(processedOriginalFilePath, outputFilePath, true);
			File.Delete(inputFilePath);
			return;
		}

		var compressedSize = new FileInfo(outputFilePath).Length;
		double compressionRatio = (double)compressedSize / originalSize * 100;

		// Check if effective compression was possible
		if (compressionRatio > 95.0)
		{
			Console.WriteLine("Effective compression not possible, copying original file to output.");
			File.Copy(processedOriginalFilePath, outputFilePath, true);
		}
		else
		{
			Console.WriteLine($"{originalSize} bytes - Original Size");
			Console.WriteLine($"{compressedSize} bytes - Compressed Size");
			Console.WriteLine($"{compressionRatio:F2} % of original size.");
		}

		if (File.Exists(inputFilePath))
			File.Delete(inputFilePath);
	}
	catch (Exception ex)
	{
		Console.WriteLine($"Error processing file {Path.GetFileName(inputFilePath)}");
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

static void MoveAndReplaceFile(string filePath, string targetFilePath)
{
	if (File.Exists(targetFilePath))
		File.Delete(targetFilePath);
	File.Move(filePath, targetFilePath);
}