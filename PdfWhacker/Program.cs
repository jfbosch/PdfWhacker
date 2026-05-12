using PdfWhacker;

if (args.Length == 0)
{
	PrintUsage();
	return 1;
}

switch (args[0])
{
	case "watch":
		return await RunWatchMode(args);
	case "compress":
		return RunCompressMode(args);
	default:
		PrintUsage();
		return 1;
}

static void PrintUsage()
{
	Console.WriteLine("PdfWhacker — PDF compression and merging tool");
	Console.WriteLine();
	Console.WriteLine("Usage:");
	Console.WriteLine("  PdfWhacker watch    <workingFolderPath> <ghostscriptExecutablePath>");
	Console.WriteLine("  PdfWhacker compress <directoryPath>     <ghostscriptExecutablePath>");
	Console.WriteLine();
	Console.WriteLine("  watch    — Long-running mode. Watches <workingFolderPath>/CompressionInput");
	Console.WriteLine("             and <workingFolderPath>/MergeInput for PDFs and produces output");
	Console.WriteLine("             in <workingFolderPath>/Output.");
	Console.WriteLine("  compress — One-shot mode. Recursively compresses all PDFs in <directoryPath>");
	Console.WriteLine("             in place. Originals are only replaced when compression succeeds");
	Console.WriteLine("             and meets the minimum size-reduction threshold.");
}

static int RunCompressMode(string[] args)
{
	if (args.Length < 3)
	{
		Console.WriteLine("Usage: PdfWhacker compress <directoryPath> <ghostscriptExecutablePath>");
		return 1;
	}

	string directoryPath = args[1];
	string ghostscriptExecutablePath = args[2];

	if (!Directory.Exists(directoryPath))
	{
		Console.WriteLine($"Directory not found: {directoryPath}");
		return 1;
	}
	if (!File.Exists(ghostscriptExecutablePath))
	{
		Console.WriteLine($"Ghostscript executable not found: {ghostscriptExecutablePath}");
		return 1;
	}

	return new RecursivePdfCompressor().CompressTree(directoryPath, ghostscriptExecutablePath);
}

static async Task<int> RunWatchMode(string[] args)
{
	if (args.Length < 3)
	{
		Console.WriteLine("Usage: PdfWhacker watch <workingFolderPath> <ghostscriptExecutablePath>");
		return 1;
	}

	string workingFolderPath = args[1];
	string ghostscriptExecutablePath = args[2];

	if (!File.Exists(ghostscriptExecutablePath))
	{
		throw new FileNotFoundException("Ghostscript executable not found.", ghostscriptExecutablePath);
	}

	MigrateOldFolder(Path.Combine(workingFolderPath, "CompressionOriginal"), Path.Combine(workingFolderPath, "Original", "Compression"));
	MigrateOldFolder(Path.Combine(workingFolderPath, "MergeOriginal"), Path.Combine(workingFolderPath, "Original", "Merge"));
	MigrateOldFolder(Path.Combine(workingFolderPath, "CompressionOutput"), Path.Combine(workingFolderPath, "Output"));
	MigrateOldFolder(Path.Combine(workingFolderPath, "MergeOutput"), Path.Combine(workingFolderPath, "Output"));

	// PDF Compression
	string compressionInputFolderPath = Path.Combine(workingFolderPath, "CompressionInput");
	string compressionProcessedFolderPath = Path.Combine(workingFolderPath, "Original", "Compression");
	string compressionOutputFolderPath = Path.Combine(workingFolderPath, "Output");
	Directory.CreateDirectory(compressionInputFolderPath);
	Directory.CreateDirectory(compressionProcessedFolderPath);
	Directory.CreateDirectory(compressionOutputFolderPath);

	foreach (var filePath in Directory.EnumerateFiles(compressionInputFolderPath, "*.pdf"))
	{
		new PdfCompressor().CompressFile(filePath, compressionOutputFolderPath, compressionProcessedFolderPath, ghostscriptExecutablePath);
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

	// PDF Merge
	string mergeInputFolderPath = Path.Combine(workingFolderPath, "MergeInput");
	string mergeProcessedFolderPath = Path.Combine(workingFolderPath, "Original", "Merge");
	string mergeOutputFolderPath = Path.Combine(workingFolderPath, "Output");
	Directory.CreateDirectory(mergeInputFolderPath);
	Directory.CreateDirectory(mergeProcessedFolderPath);
	Directory.CreateDirectory(mergeOutputFolderPath);

	new PdfMerger().MergeFiles(mergeInputFolderPath, mergeOutputFolderPath, mergeProcessedFolderPath, ghostscriptExecutablePath);

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
		await Task.Delay(1000);
	}

	return 0;

	void PromptUser()
	{
		Console.WriteLine("");
		Console.WriteLine("Watching for new PDF files in input folders under" + Path.GetFullPath(workingFolderPath));

		var filesToMerge = Directory.EnumerateFiles(mergeInputFolderPath, "*.pdf").ToArray();
		Console.WriteLine($"Press (m) to merge any available files. {filesToMerge.Length} available.");

		Console.WriteLine("Press (q) )to quit.");
	}
}

static void MigrateOldFolder(string oldFolder, string newFolder)
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
