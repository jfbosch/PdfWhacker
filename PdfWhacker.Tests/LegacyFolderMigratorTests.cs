using PdfWhacker;

namespace PdfWhacker.Tests;

public class LegacyFolderMigratorTests : IDisposable
{
	private readonly string _tempDir;

	public LegacyFolderMigratorTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "pdfwhacker-migrator-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
	}

	private string OldPath(string name) => Path.Combine(_tempDir, "old", name);
	private string NewPath(string name) => Path.Combine(_tempDir, "new", name);

	private void WriteFile(string path, string content)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}

	[Fact]
	public void Returns_silently_when_old_folder_does_not_exist()
	{
		// New folder also doesn't exist — Migrate must not throw and must not create it.
		string oldFolder = Path.Combine(_tempDir, "absent-old");
		string newFolder = Path.Combine(_tempDir, "absent-new");

		LegacyFolderMigrator.Migrate(oldFolder, newFolder);

		Assert.False(Directory.Exists(oldFolder));
		Assert.False(Directory.Exists(newFolder));
	}

	[Fact]
	public void Moves_files_into_new_folder_and_removes_drained_old_folder()
	{
		WriteFile(OldPath("a.pdf"), "alpha");
		WriteFile(OldPath("b.pdf"), "bravo");

		LegacyFolderMigrator.Migrate(Path.Combine(_tempDir, "old"), Path.Combine(_tempDir, "new"));

		Assert.False(File.Exists(OldPath("a.pdf")));
		Assert.False(File.Exists(OldPath("b.pdf")));
		Assert.False(Directory.Exists(Path.Combine(_tempDir, "old")), "Drained old folder should be removed.");
		Assert.Equal("alpha", File.ReadAllText(NewPath("a.pdf")));
		Assert.Equal("bravo", File.ReadAllText(NewPath("b.pdf")));
	}

	[Fact]
	public void Refuses_to_overwrite_existing_files_at_destination()
	{
		WriteFile(OldPath("conflict.pdf"), "from-old");
		WriteFile(NewPath("conflict.pdf"), "from-new");

		LegacyFolderMigrator.Migrate(Path.Combine(_tempDir, "old"), Path.Combine(_tempDir, "new"));

		// Conflicting destination preserved, source left in place.
		Assert.Equal("from-new", File.ReadAllText(NewPath("conflict.pdf")));
		Assert.True(File.Exists(OldPath("conflict.pdf")), "Source should remain when destination already has the name.");
		Assert.Equal("from-old", File.ReadAllText(OldPath("conflict.pdf")));
		// Old folder still exists because it's not empty.
		Assert.True(Directory.Exists(Path.Combine(_tempDir, "old")));
	}

	[Fact]
	public void Mixed_conflict_and_non_conflict_moves_what_it_can()
	{
		WriteFile(OldPath("clean.pdf"), "moves");
		WriteFile(OldPath("dirty.pdf"), "old-version");
		WriteFile(NewPath("dirty.pdf"), "new-version");

		LegacyFolderMigrator.Migrate(Path.Combine(_tempDir, "old"), Path.Combine(_tempDir, "new"));

		Assert.False(File.Exists(OldPath("clean.pdf")));
		Assert.Equal("moves", File.ReadAllText(NewPath("clean.pdf")));
		Assert.True(File.Exists(OldPath("dirty.pdf")), "Conflicting source should remain.");
		Assert.Equal("new-version", File.ReadAllText(NewPath("dirty.pdf")));
		// Old folder remains because dirty.pdf still lives there.
		Assert.True(Directory.Exists(Path.Combine(_tempDir, "old")));
	}

	[Fact]
	public void Empty_old_folder_is_removed_after_migration()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, "old"));

		LegacyFolderMigrator.Migrate(Path.Combine(_tempDir, "old"), Path.Combine(_tempDir, "new"));

		Assert.False(Directory.Exists(Path.Combine(_tempDir, "old")), "Empty old folder should be removed.");
	}
}
