using System.Text;

namespace PdfWhacker;

/// <summary>
/// TextWriter that mirrors every write to the original console (so the watch-mode
/// status prompt still works interactively) AND to a daily rolling log file under
/// the watch folder. Watch mode runs unattended; without a log file, a Ghostscript
/// crash at 3am is invisible after the next time you scroll the terminal.
///
/// Installed by <see cref="Install"/> via Console.SetOut. Disposing the returned
/// scope restores the original Console.Out and closes the file.
/// </summary>
internal sealed class FileLogger : TextWriter
{
	private readonly TextWriter _passthrough;
	private readonly string _logDirectory;
	private readonly object _gate = new();
	private DateOnly _currentDate;
	private StreamWriter? _file;

	public override Encoding Encoding => _passthrough.Encoding;

	private FileLogger(TextWriter passthrough, string logDirectory)
	{
		_passthrough = passthrough;
		_logDirectory = logDirectory;
		_currentDate = DateOnly.FromDateTime(DateTime.Now);
		OpenFileForToday();
	}

	/// <summary>
	/// Tees Console.Out into <paramref name="logDirectory"/>/pdfwhacker-YYYY-MM-DD.log.
	/// Returns a scope that, when disposed, restores the previous Console.Out and
	/// closes the log file. Best-effort: if the log directory can't be created or
	/// the file can't be opened, the original Console.Out is left untouched and
	/// a single line is printed to console.
	/// </summary>
	public static IDisposable? Install(string logDirectory)
	{
		try
		{
			Directory.CreateDirectory(logDirectory);
			var original = Console.Out;
			var tee = new FileLogger(original, logDirectory);
			Console.SetOut(tee);
			tee.WriteLine($"=== PdfWhacker watch session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
			return new Scope(tee, original);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"File logging disabled: {ex.Message}");
			return null;
		}
	}

	public override void Write(char value)
	{
		lock (_gate)
		{
			_passthrough.Write(value);
			RotateIfNeeded();
			_file?.Write(value);
		}
	}

	public override void Write(string? value)
	{
		if (value is null) return;
		lock (_gate)
		{
			_passthrough.Write(value);
			RotateIfNeeded();
			_file?.Write(value);
		}
	}

	public override void WriteLine(string? value)
	{
		lock (_gate)
		{
			_passthrough.WriteLine(value);
			RotateIfNeeded();
			_file?.WriteLine(value);
		}
	}

	public override void WriteLine()
	{
		lock (_gate)
		{
			_passthrough.WriteLine();
			RotateIfNeeded();
			_file?.WriteLine();
		}
	}

	public override void Flush()
	{
		lock (_gate)
		{
			_passthrough.Flush();
			_file?.Flush();
		}
	}

	private void RotateIfNeeded()
	{
		var today = DateOnly.FromDateTime(DateTime.Now);
		if (today == _currentDate && _file != null)
			return;
		_currentDate = today;
		CloseFile();
		OpenFileForToday();
	}

	private void OpenFileForToday()
	{
		try
		{
			string path = Path.Combine(_logDirectory, $"pdfwhacker-{_currentDate:yyyy-MM-dd}.log");
			_file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
			{
				AutoFlush = true,
			};
		}
		catch (Exception ex)
		{
			// Don't kill watch mode if the disk is full / log file is locked — fall
			// back to console-only and try again on the next rotation.
			_file = null;
			_passthrough.WriteLine($"File logging temporarily unavailable: {ex.Message}");
		}
	}

	private void CloseFile()
	{
		try { _file?.Dispose(); }
		catch { /* best effort */ }
		_file = null;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			lock (_gate) CloseFile();
		}
		base.Dispose(disposing);
	}

	private sealed class Scope : IDisposable
	{
		private readonly FileLogger _tee;
		private readonly TextWriter _original;
		private bool _disposed;

		public Scope(FileLogger tee, TextWriter original) { _tee = tee; _original = original; }

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			try { Console.SetOut(_original); } catch { /* best effort */ }
			_tee.Dispose();
		}
	}
}
