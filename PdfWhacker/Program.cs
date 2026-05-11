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

if (!File.Exists(ghostscriptExecutablePath))
{
	throw new FileNotFoundException("Ghostscript executable not found.", ghostscriptExecutablePath);
}

MigrateOldFolder(Path.Combine(workingFolderPath, "CompressionOriginal"), Path.Combine(workingFolderPath, "Original", "Compression"));
MigrateOldFolder(Path.Combine(workingFolderPath, "MergeOriginal"), Path.Combine(workingFolderPath, "Original", "Merge"));
MigrateOldFolder(Path.Combine(workingFolderPath, "CompressionOutput"), Path.Combine(workingFolderPath, "Output"));
MigrateOldFolder(Path.Combine(workingFolderPath, "MergeOutput"), Path.Combine(workingFolderPath, "Output"));

{ // PDF Compression
	string compressionInputFolderPath = Path.Combine(workingFolderPath, "CompressionInput");
	string compressionProcessedFolderPath = Path.Combine(workingFolderPath, "Original", "Compression");
	string compressionOutputFolderPath = Path.Combine(workingFolderPath, "Output");
	Directory.CreateDirectory(compressionInputFolderPath);
	Directory.CreateDirectory(compressionProcessedFolderPath);
	Directory.CreateDirectory(compressionOutputFolderPath);

	CompressExistingFiles(compressionInputFolderPath, compressionOutputFolderPath, compressionProcessedFolderPath, ghostscriptExecutablePath);

	void CompressExistingFiles(string inputFolder, string outputFolder, string processedFolder, string gsPath)
	{
		foreach (var filePath in Directory.EnumerateFiles(inputFolder, "*.pdf"))
		{
			new PdfCompressor().CompressFile(filePath, outputFolder, processedFolder, gsPath);
		}
	}

	var compressionFileWatcher = new FileSystemWatcher(compressionInputFolderPath)
	{
		NotifyFilter = NotifyFilters.FileName,
		Filter = "*.pdf"
	};

	compressionFileWatcher.Created += (sender, e) =>
	{
		e.FullPath.WaitForFileToBeReady();
		new PdfCompressor().CompressFile(e.FullPath, compressionOutputFolderPath, compressionProcessedFolderPath, ghostscriptExecutablePath);
	};

	compressionFileWatcher.EnableRaisingEvents = true;
}


// PDF Merge
string mergeInputFolderPath = Path.Combine(workingFolderPath, "MergeInput");
string mergeProcessedFolderPath = Path.Combine(workingFolderPath, "Original", "Merge");
string mergeOutputFolderPath = Path.Combine(workingFolderPath, "Output");
{
	Directory.CreateDirectory(mergeInputFolderPath);
	Directory.CreateDirectory(mergeProcessedFolderPath);
	Directory.CreateDirectory(mergeOutputFolderPath);

	MergeExistingFiles(mergeInputFolderPath, mergeOutputFolderPath, mergeProcessedFolderPath, ghostscriptExecutablePath);

	void MergeExistingFiles(string inputFolder, string outputFolder, string processedFolder, string gsPath)
	{
		new PdfMerger().MergeFiles(inputFolder, outputFolder, processedFolder, gsPath);
	}

	var mergeFileWatcher = new FileSystemWatcher(mergeInputFolderPath)
	{
		NotifyFilter = NotifyFilters.FileName,
		Filter = "*.pdf"
	};

	mergeFileWatcher.Created += (sender, e) =>
	{
		e.FullPath.WaitForFileToBeReady();
		string fileName = Path.GetFileName(e.FullPath);
		string folderPath = Directory.GetParent(e.FullPath).FullName;
		var files = Directory.EnumerateFiles(folderPath, "*.pdf").ToArray();
		Console.WriteLine($"{fileName} --- available to merge. Total files to merge: {files.Length}");
	};

	mergeFileWatcher.EnableRaisingEvents = true;
}


// -----------------------------------
// Coms with user

void PromptUser()
{
	Console.WriteLine("");
	Console.WriteLine("Watching for new PDF files in input folders under" + Path.GetFullPath(workingFolderPath));

	var filesToMerge = Directory.EnumerateFiles(mergeInputFolderPath, "*.pdf").ToArray();
	Console.WriteLine($"Press (m) to merge any available files. {filesToMerge.Length} available.");

	Console.WriteLine("Press (q) )to quit.");

}
PromptUser();

bool exitApp = false;
while (!exitApp)
{
	if (Console.KeyAvailable)
	{
		string? line = Console.ReadLine();
		switch (line)
		{
			case "m":
				new PdfMerger().MergeFiles(mergeInputFolderPath, mergeOutputFolderPath, mergeProcessedFolderPath, ghostscriptExecutablePath);
				PromptUser();
				break;

			case "q":
			case "Q":
				exitApp = true;
				break;

			default:
				PromptUser();
				break;
		}

	}
	await Task.Delay(1000); // Sleep for a while before checking again
}

double GetPDFCompatibilityVersion(
	string pdfFilePath)
{
	throw new NotImplementedException();
}

void MigrateOldFolder(string oldFolder, string newFolder)
{
	if (!Directory.Exists(oldFolder))
		return;

	Directory.CreateDirectory(newFolder);

	int moved = 0;
	int skipped = 0;
	foreach (var sourcePath in Directory.EnumerateFiles(oldFolder))
	{
		string fileName = Path.GetFileName(sourcePath);
		string destPath = Path.Combine(newFolder, fileName);
		if (File.Exists(destPath))
		{
			Console.WriteLine($"Skipping migration of '{fileName}' from '{oldFolder}' — file already exists at '{destPath}'.");
			skipped++;
			continue;
		}
		File.Move(sourcePath, destPath);
		moved++;
	}

	string suffix = skipped > 0 ? $" ({skipped} skipped due to existing files)" : "";
	Console.WriteLine($"Migrated {moved} file(s) from '{oldFolder}' to '{newFolder}'{suffix}.");

	bool isEmpty = !Directory.EnumerateFileSystemEntries(oldFolder).Any();
	if (isEmpty)
		Directory.Delete(oldFolder);
}

