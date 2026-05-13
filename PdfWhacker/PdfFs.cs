namespace PdfWhacker;

/// <summary>
/// Filesystem helpers shared between the watch-mode and recursive pipelines. Single
/// home for things that used to live in three or four files.
/// </summary>
internal static class PdfFs
{
	/// <summary>
	/// Yields every PDF (case-insensitive extension match) under <paramref name="root"/>.
	/// Recursive walks skip reparse points and inaccessible directories so they never
	/// fail mid-scan because of a single bad entry.
	/// </summary>
	public static IEnumerable<string> EnumeratePdfs(string root, bool recursive)
	{
		var options = new EnumerationOptions
		{
			RecurseSubdirectories = recursive,
			AttributesToSkip = FileAttributes.ReparsePoint,
			IgnoreInaccessible = true,
		};
		return Directory.EnumerateFiles(root, "*.pdf", options)
			.Where(p => Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Deletes <paramref name="path"/> if it exists. Swallows IO errors and logs them —
	/// callers reach for this when failure is fine but a silent leak is not.
	/// </summary>
	public static void SafeDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to delete '{path}': {ex.Message}");
		}
	}

	/// <summary>
	/// Collapses any whitespace, trims, and truncates to <paramref name="maxLen"/>
	/// characters with an ellipsis suffix. Used to keep ghostscript stderr from
	/// blowing up error messages.
	/// </summary>
	public static string Truncate(string s, int maxLen)
	{
		if (string.IsNullOrEmpty(s))
			return string.Empty;
		s = s.Replace("\r", " ").Replace("\n", " ").Trim();
		return s.Length <= maxLen ? s : string.Concat(s.AsSpan(0, maxLen), "...");
	}

	/// <summary>
	/// Picks paths in <paramref name="archiveFolder"/> and <paramref name="outputFolder"/>
	/// that don't collide with anything already on disk. Both returned paths share the
	/// same disambiguating suffix so a "foo (3).pdf" in Output can be matched to the
	/// archived original by name.
	///
	/// Watch mode never loses an original: if the same filename arrives twice, the
	/// second pass lands at "name (2).pdf" rather than clobbering the first.
	/// </summary>
	public static (string archivePath, string outputPath) BuildUniquePathPair(
		string archiveFolder, string outputFolder, string fileName)
	{
		string baseName = Path.GetFileNameWithoutExtension(fileName);
		string ext = Path.GetExtension(fileName);

		string a = Path.Combine(archiveFolder, fileName);
		string o = Path.Combine(outputFolder, fileName);
		if (!File.Exists(a) && !File.Exists(o))
			return (a, o);

		for (int n = 2; n < 1000; n++)
		{
			a = Path.Combine(archiveFolder, $"{baseName} ({n}){ext}");
			o = Path.Combine(outputFolder, $"{baseName} ({n}){ext}");
			if (!File.Exists(a) && !File.Exists(o))
				return (a, o);
		}

		string guid = Guid.NewGuid().ToString("N");
		return (Path.Combine(archiveFolder, $"{baseName} ({guid}){ext}"),
				Path.Combine(outputFolder, $"{baseName} ({guid}){ext}"));
	}

	/// <summary>
	/// Polls until <paramref name="filePath"/> can be opened with exclusive read access
	/// (i.e. no other handle holds it), or returns false after <paramref name="maxAttempts"/>
	/// attempts spaced <paramref name="delayMs"/> milliseconds apart.
	///
	/// Returns true regardless of file length — zero-byte handling is the caller's job.
	/// Returns false immediately on UnauthorizedAccessException (permissions are not
	/// going to change while we sleep).
	/// </summary>
	public static bool TryWaitForExclusiveAccess(string filePath, int maxAttempts, int delayMs)
	{
		for (int i = 0; i < maxAttempts; i++)
		{
			try
			{
				using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
				return true;
			}
			catch (IOException)
			{
				Thread.Sleep(delayMs);
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}
		return false;
	}
}
