using PdfWhacker;

if (args.Length == 0)
{
	PrintUsage();
	return 1;
}

switch (args[0])
{
	case "watch":
		return RunWatchMode(args);
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

static int RunWatchMode(string[] args)
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
		Console.WriteLine($"Ghostscript executable not found: {ghostscriptExecutablePath}");
		return 1;
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

	// Serialize all compression work onto a single worker so a flurry of dropped
	// files doesn't spawn N concurrent Ghostscript processes and shred the box.
	var compressionQueue = new System.Collections.Concurrent.BlockingCollection<string>();
	var compressionWorker = new Thread(() =>
	{
		var compressor = new PdfCompressor();
		foreach (var filePath in compressionQueue.GetConsumingEnumerable())
		{
			try
			{
				compressor.CompressFile(filePath, compressionOutputFolderPath, compressionProcessedFolderPath, ghostscriptExecutablePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Compression worker error for '{filePath}': {ex.Message}");
			}
		}
	})
	{ IsBackground = true, Name = "PdfWhacker-Compression" };
	compressionWorker.Start();

	foreach (var filePath in EnumeratePdfs(compressionInputFolderPath))
		compressionQueue.Add(filePath);

	var compressionFileWatcher = new FileSystemWatcher(compressionInputFolderPath)
	{
		NotifyFilter = NotifyFilters.FileName,
		Filter = "*.pdf"
	};
	compressionFileWatcher.Created += (sender, e) =>
	{
		if (!Path.GetExtension(e.FullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
			return;
		compressionQueue.Add(e.FullPath);
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
		try
		{
			if (!Path.GetExtension(e.FullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
				return;
			string fileName = Path.GetFileName(e.FullPath);
			int count = EnumeratePdfs(mergeInputFolderPath).Count();
			Console.WriteLine($"{fileName} --- available to merge. Total files to merge: {count}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Merge watcher error: {ex.Message}");
		}
	};
	mergeFileWatcher.EnableRaisingEvents = true;

	// Capture Ctrl-C as a keypress instead of letting the runtime kill us mid-compression.
	bool canInterceptCtrlC = true;
	try { Console.TreatControlCAsInput = true; }
	catch (IOException) { canInterceptCtrlC = false; } // e.g. redirected stdin

	PromptUser();

	while (true)
	{
		ConsoleKeyInfo key;
		try
		{
			key = Console.ReadKey(intercept: true);
		}
		catch (InvalidOperationException)
		{
			// Console input is redirected; fall back to a blocking line read.
			string? line = Console.ReadLine();
			if (line is null) break;
			char c = line.Length > 0 ? line[0] : '\0';
			if (c == 'q' || c == 'Q') break;
			if (c == 'm' || c == 'M') new PdfMerger().MergeFiles(mergeInputFolderPath, mergeOutputFolderPath, mergeProcessedFolderPath, ghostscriptExecutablePath);
			PromptUser();
			continue;
		}

		if (canInterceptCtrlC && key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
		{
			Console.WriteLine();
			Console.WriteLine("Ctrl-C received; shutting down...");
			break;
		}

		if (key.KeyChar == 'q' || key.KeyChar == 'Q')
			break;

		if (key.KeyChar == 'm' || key.KeyChar == 'M')
			new PdfMerger().MergeFiles(mergeInputFolderPath, mergeOutputFolderPath, mergeProcessedFolderPath, ghostscriptExecutablePath);

		PromptUser();
	}

	if (canInterceptCtrlC)
	{
		try { Console.TreatControlCAsInput = false; } catch (IOException) { /* ignore */ }
	}

	compressionQueue.CompleteAdding();
	compressionFileWatcher.Dispose();
	mergeFileWatcher.Dispose();
	compressionWorker.Join(TimeSpan.FromSeconds(5));

	return 0;

	void PromptUser()
	{
		Console.WriteLine();
		Console.WriteLine($"Watching for new PDF files in input folders under {Path.GetFullPath(workingFolderPath)}");
		int count = EnumeratePdfs(mergeInputFolderPath).Count();
		Console.WriteLine($"Press (m) to merge any available files. {count} available.");
		Console.WriteLine("Press (q) to quit.");
	}
}

static IEnumerable<string> EnumeratePdfs(string folder) =>
	Directory.EnumerateFiles(folder, "*.pdf")
		.Where(p => Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase));

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

	if (moved > 0 || skipped > 0)
	{
		string suffix = skipped > 0 ? $" ({skipped} skipped due to existing files)" : "";
		Console.WriteLine($"Migrated {moved} file(s) from '{oldFolder}' to '{newFolder}'{suffix}.");
	}

	if (!Directory.EnumerateFileSystemEntries(oldFolder).Any())
		Directory.Delete(oldFolder);
}
