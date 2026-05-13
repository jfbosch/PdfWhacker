namespace PdfWhacker;

/// <summary>
/// One-time migration of the pre-v2 folder layout. Earlier versions kept originals
/// and outputs in flat per-operation folders (CompressionOriginal, CompressionOutput,
/// MergeOriginal, MergeOutput); the current layout consolidates them under
/// Original/&lt;Op&gt; and a shared Output. On startup we move legacy files into the new
/// home, refusing to overwrite anything that already exists at the destination.
/// </summary>
public static class LegacyFolderMigrator
{
	/// <summary>
	/// Moves files from <paramref name="oldFolder"/> into <paramref name="newFolder"/>.
	/// Returns silently if the old folder doesn't exist. Files whose names already
	/// exist at the destination are left where they are (never silently overwritten).
	/// The old folder is removed once it's been fully drained.
	/// </summary>
	public static void Migrate(string oldFolder, string newFolder)
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
}
