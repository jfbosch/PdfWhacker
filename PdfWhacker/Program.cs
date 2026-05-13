using System.Collections.Concurrent;
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
	case "decrypt":
		return RunDecryptMode(args);
	default:
		PrintUsage();
		return 1;
}

static void PrintUsage()
{
	Console.WriteLine("PdfWhacker — PDF compression, merging, and decryption tool");
	Console.WriteLine();
	Console.WriteLine("Usage:");
	Console.WriteLine("  PdfWhacker watch    <workingFolderPath> <ghostscriptExecutablePath>");
	Console.WriteLine("  PdfWhacker compress <directoryPath>     <ghostscriptExecutablePath>");
	Console.WriteLine("  PdfWhacker decrypt  <directoryPath>     <ghostscriptExecutablePath>");
	Console.WriteLine();
	Console.WriteLine("  watch    — Long-running mode. Watches <workingFolderPath>/CompressionInput,");
	Console.WriteLine("             <workingFolderPath>/DecryptInput, and <workingFolderPath>/MergeInput");
	Console.WriteLine("             for PDFs and produces output in <workingFolderPath>/Output.");
	Console.WriteLine("  compress — One-shot mode. Recursively compresses all PDFs in <directoryPath>");
	Console.WriteLine("             in place. Originals are only replaced when compression succeeds");
	Console.WriteLine("             and meets the minimum size-reduction threshold.");
	Console.WriteLine("  decrypt  — One-shot mode. Recursively decrypts password-protected PDFs in");
	Console.WriteLine("             <directoryPath> in place using passwords from appsettings.json");
	Console.WriteLine("             next to the executable.");
}

static int RunCompressMode(string[] args)
{
	if (args.Length < 3)
	{
		Console.WriteLine("Usage: PdfWhacker compress <directoryPath> <ghostscriptExecutablePath>");
		return 1;
	}

	string directoryPath = args[1];
	if (!Directory.Exists(directoryPath))
	{
		Console.WriteLine($"Directory not found: {directoryPath}");
		return 1;
	}
	if (!TryResolveGhostscript(args[2], out string ghostscriptExecutablePath))
		return 1;

	return new RecursivePdfCompressor().CompressTree(directoryPath, ghostscriptExecutablePath);
}

static int RunDecryptMode(string[] args)
{
	if (args.Length < 3)
	{
		Console.WriteLine("Usage: PdfWhacker decrypt <directoryPath> <ghostscriptExecutablePath>");
		return 1;
	}

	string directoryPath = args[1];
	if (!Directory.Exists(directoryPath))
	{
		Console.WriteLine($"Directory not found: {directoryPath}");
		return 1;
	}
	if (!TryResolveGhostscript(args[2], out string ghostscriptExecutablePath))
		return 1;

	var passwordStore = PasswordStore.LoadFromBaseDirectory();
	return new RecursivePdfDecryptor().DecryptTree(directoryPath, ghostscriptExecutablePath, passwordStore.Passwords);
}

// Single home for the "does this gs path exist?" check that used to live in three
// places. Cheap step toward a typed GhostscriptBinary — keeps signatures unchanged.
static bool TryResolveGhostscript(string candidate, out string path)
{
	path = candidate;
	if (File.Exists(candidate))
		return true;
	Console.WriteLine($"Ghostscript executable not found: {candidate}");
	return false;
}

static int RunWatchMode(string[] args)
{
	if (args.Length < 3)
	{
		Console.WriteLine("Usage: PdfWhacker watch <workingFolderPath> <ghostscriptExecutablePath>");
		return 1;
	}

	string workingFolderPath = args[1];
	if (!TryResolveGhostscript(args[2], out string ghostscriptExecutablePath))
		return 1;

	using var _logScope = FileLogger.Install(Path.Combine(workingFolderPath, "logs"));

	LegacyFolderMigrator.Migrate(Path.Combine(workingFolderPath, "CompressionOriginal"), Path.Combine(workingFolderPath, "Original", "Compression"));
	LegacyFolderMigrator.Migrate(Path.Combine(workingFolderPath, "MergeOriginal"), Path.Combine(workingFolderPath, "Original", "Merge"));
	LegacyFolderMigrator.Migrate(Path.Combine(workingFolderPath, "CompressionOutput"), Path.Combine(workingFolderPath, "Output"));
	LegacyFolderMigrator.Migrate(Path.Combine(workingFolderPath, "MergeOutput"), Path.Combine(workingFolderPath, "Output"));

	// PDF Compression
	string compressionInputFolderPath = Path.Combine(workingFolderPath, "CompressionInput");
	string compressionProcessedFolderPath = Path.Combine(workingFolderPath, "Original", "Compression");
	string compressionOutputFolderPath = Path.Combine(workingFolderPath, "Output");
	Directory.CreateDirectory(compressionInputFolderPath);
	Directory.CreateDirectory(compressionProcessedFolderPath);
	Directory.CreateDirectory(compressionOutputFolderPath);

	// Serialize all compression work onto a single worker so a flurry of dropped
	// files doesn't spawn N concurrent Ghostscript processes and shred the box.
	var compressionQueue = new BlockingCollection<string>();
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

	var compressionFileWatcher = new FileSystemWatcher(compressionInputFolderPath)
	{
		NotifyFilter = NotifyFilters.FileName,
		Filter = "*.pdf",
		InternalBufferSize = 64 * 1024,
	};
	compressionFileWatcher.Created += (sender, e) =>
	{
		if (!Path.GetExtension(e.FullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
			return;
		compressionQueue.Add(e.FullPath);
	};
	compressionFileWatcher.Error += (sender, e) =>
	{
		var ex = e.GetException();
		Console.WriteLine($"Compression watcher error (input buffer may have overflowed): {ex.Message}. Re-scanning input folder.");
		foreach (var filePath in PdfFs.EnumeratePdfs(compressionInputFolderPath, recursive: false))
			compressionQueue.Add(filePath);
	};
	// Enable the watcher BEFORE the initial sweep so any file dropped during
	// startup is queued by either the sweep or the Created event. The worker
	// pipeline is idempotent — the input file is deleted on success, so a
	// brief overlap is harmless.
	compressionFileWatcher.EnableRaisingEvents = true;
	foreach (var filePath in PdfFs.EnumeratePdfs(compressionInputFolderPath, recursive: false))
		compressionQueue.Add(filePath);

	// PDF Decrypt
	var passwordStore = PasswordStore.LoadFromBaseDirectory();

	string decryptInputFolderPath = Path.Combine(workingFolderPath, "DecryptInput");
	string decryptProcessedFolderPath = Path.Combine(workingFolderPath, "Original", "Decrypt");
	string decryptOutputFolderPath = Path.Combine(workingFolderPath, "Output");
	Directory.CreateDirectory(decryptInputFolderPath);
	Directory.CreateDirectory(decryptProcessedFolderPath);
	Directory.CreateDirectory(decryptOutputFolderPath);

	var decryptQueue = new BlockingCollection<string>();
	var decryptWorker = new Thread(() =>
	{
		var decryptor = new PdfDecryptor();
		foreach (var filePath in decryptQueue.GetConsumingEnumerable())
		{
			try
			{
				decryptor.DecryptFile(filePath, decryptOutputFolderPath, decryptProcessedFolderPath, ghostscriptExecutablePath, passwordStore.Passwords);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Decryption worker error for '{filePath}': {ex.Message}");
			}
		}
	})
	{ IsBackground = true, Name = "PdfWhacker-Decryption" };
	decryptWorker.Start();

	var decryptFileWatcher = new FileSystemWatcher(decryptInputFolderPath)
	{
		NotifyFilter = NotifyFilters.FileName,
		Filter = "*.pdf",
		InternalBufferSize = 64 * 1024,
	};
	decryptFileWatcher.Created += (sender, e) =>
	{
		if (!Path.GetExtension(e.FullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
			return;
		decryptQueue.Add(e.FullPath);
	};
	decryptFileWatcher.Error += (sender, e) =>
	{
		var ex = e.GetException();
		Console.WriteLine($"Decryption watcher error (input buffer may have overflowed): {ex.Message}. Re-scanning input folder.");
		foreach (var filePath in PdfFs.EnumeratePdfs(decryptInputFolderPath, recursive: false))
			decryptQueue.Add(filePath);
	};
	// See compression-watcher comment above: enable before the initial sweep.
	decryptFileWatcher.EnableRaisingEvents = true;
	foreach (var filePath in PdfFs.EnumeratePdfs(decryptInputFolderPath, recursive: false))
		decryptQueue.Add(filePath);

	// PDF Merge
	string mergeInputFolderPath = Path.Combine(workingFolderPath, "MergeInput");
	string mergeProcessedFolderPath = Path.Combine(workingFolderPath, "Original", "Merge");
	string mergeOutputFolderPath = Path.Combine(workingFolderPath, "Output");
	Directory.CreateDirectory(mergeInputFolderPath);
	Directory.CreateDirectory(mergeProcessedFolderPath);
	Directory.CreateDirectory(mergeOutputFolderPath);

	// Merge runs on its own single-slot worker so a long Ghostscript invocation
	// can't block the console-key thread (i.e. q / Ctrl-C stay responsive).
	// Each enqueued item is just a signal — the worker always re-enumerates the
	// folder on wake-up, so back-to-back `m` presses coalesce naturally.
	var mergeQueue = new BlockingCollection<object?>();
	var mergeWorker = new Thread(() =>
	{
		var merger = new PdfMerger();
		foreach (var _ in mergeQueue.GetConsumingEnumerable())
		{
			try
			{
				merger.MergeFiles(mergeInputFolderPath, mergeOutputFolderPath, mergeProcessedFolderPath, ghostscriptExecutablePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Merge worker error: {ex.Message}");
			}
		}
	})
	{ IsBackground = true, Name = "PdfWhacker-Merge" };
	mergeWorker.Start();

	// Run the startup merge through the worker so its console output stays
	// interleaved with the live status prompt the same way the keystroke-driven
	// merges do.
	mergeQueue.Add(null);

	var mergeFileWatcher = new FileSystemWatcher(mergeInputFolderPath)
	{
		NotifyFilter = NotifyFilters.FileName,
		Filter = "*.pdf",
		InternalBufferSize = 64 * 1024,
	};
	mergeFileWatcher.Created += (sender, e) =>
	{
		try
		{
			if (!Path.GetExtension(e.FullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
				return;
			string fileName = Path.GetFileName(e.FullPath);
			int count = PdfFs.EnumeratePdfs(mergeInputFolderPath, recursive: false).Count();
			Console.WriteLine($"{fileName} --- available to merge. Total files to merge: {count}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Merge watcher error: {ex.Message}");
		}
	};
	mergeFileWatcher.Error += (sender, e) =>
	{
		Console.WriteLine($"Merge watcher error (input buffer may have overflowed): {e.GetException().Message}.");
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
			if (c == 'm' || c == 'M') mergeQueue.Add(null);
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
			mergeQueue.Add(null);

		PromptUser();
	}

	if (canInterceptCtrlC)
	{
		try { Console.TreatControlCAsInput = false; } catch (IOException) { /* ignore */ }
	}

	compressionQueue.CompleteAdding();
	decryptQueue.CompleteAdding();
	mergeQueue.CompleteAdding();
	compressionFileWatcher.Dispose();
	decryptFileWatcher.Dispose();
	mergeFileWatcher.Dispose();
	compressionWorker.Join(TimeSpan.FromSeconds(5));
	decryptWorker.Join(TimeSpan.FromSeconds(5));
	mergeWorker.Join(TimeSpan.FromSeconds(5));
	compressionQueue.Dispose();
	decryptQueue.Dispose();
	mergeQueue.Dispose();

	return 0;

	void PromptUser()
	{
		Console.WriteLine();
		Console.WriteLine($"Watching for new PDF files in input folders under {Path.GetFullPath(workingFolderPath)}");
		int count = PdfFs.EnumeratePdfs(mergeInputFolderPath, recursive: false).Count();
		Console.WriteLine($"Press (m) to merge any available files. {count} available.");
		Console.WriteLine("Press (q) to quit.");
	}
}
