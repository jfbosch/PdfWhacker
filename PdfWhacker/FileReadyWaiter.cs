namespace PdfWhacker;

public static class FileReadyWaiter
{
	private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

	/// <summary>
	/// Waits until the file at <paramref name="filePath"/> exists and can be opened
	/// with exclusive read access. Returns false on timeout, missing file, or unrecoverable
	/// access failure. Never throws for those cases.
	/// </summary>
	public static bool TryWait(string filePath, TimeSpan timeout, TimeSpan? pollInterval = null)
	{
		var interval = pollInterval ?? DefaultPollInterval;
		var deadline = DateTime.UtcNow + timeout;

		while (true)
		{
			if (!File.Exists(filePath))
			{
				if (DateTime.UtcNow >= deadline) return false;
				Thread.Sleep(interval);
				continue;
			}

			try
			{
				using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
				if (stream.Length > 0)
					return true;
			}
			catch (IOException)
			{
				// File is still being written; retry below.
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}

			if (DateTime.UtcNow >= deadline) return false;
			Thread.Sleep(interval);
		}
	}
}
