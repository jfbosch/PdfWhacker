using PdfWhacker;

namespace PdfWhacker.Tests;

public class PasswordStoreTests : IDisposable
{
	private readonly string _tempDir;

	public PasswordStoreTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "pdfwhacker-passwordstore-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
	}

	private string WritePasswordsFile(string contents)
	{
		string path = Path.Combine(_tempDir, "appsettings.json");
		File.WriteAllText(path, contents);
		return path;
	}

	[Fact]
	public void Returns_empty_when_file_is_missing()
	{
		string path = Path.Combine(_tempDir, "does-not-exist.json");
		var store = PasswordStore.LoadFromFile(path);
		Assert.Empty(store.Passwords);
	}

	[Fact]
	public void Reads_a_valid_passwords_array()
	{
		string path = WritePasswordsFile("""{ "Passwords": ["alpha", "beta", "gamma"] }""");
		var store = PasswordStore.LoadFromFile(path);
		Assert.Equal(new[] { "alpha", "beta", "gamma" }, store.Passwords);
	}

	[Fact]
	public void Treats_empty_passwords_array_as_no_passwords()
	{
		string path = WritePasswordsFile("""{ "Passwords": [] }""");
		var store = PasswordStore.LoadFromFile(path);
		Assert.Empty(store.Passwords);
	}

	[Fact]
	public void Treats_missing_passwords_key_as_no_passwords()
	{
		string path = WritePasswordsFile("""{ "OtherSetting": 1 }""");
		var store = PasswordStore.LoadFromFile(path);
		Assert.Empty(store.Passwords);
	}

	[Fact]
	public void Returns_empty_when_json_is_malformed()
	{
		string path = WritePasswordsFile("not json {");
		var store = PasswordStore.LoadFromFile(path);
		Assert.Empty(store.Passwords);
	}

	[Fact]
	public void Trims_whitespace_and_drops_empty_entries()
	{
		string path = WritePasswordsFile("""{ "Passwords": ["  spaced  ", "", "   ", "kept"] }""");
		var store = PasswordStore.LoadFromFile(path);
		Assert.Equal(new[] { "spaced", "kept" }, store.Passwords);
	}

	[Fact]
	public void Ignores_non_string_entries_in_passwords_array()
	{
		string path = WritePasswordsFile("""{ "Passwords": ["valid", 42, null, "also-valid"] }""");
		var store = PasswordStore.LoadFromFile(path);
		Assert.Equal(new[] { "valid", "also-valid" }, store.Passwords);
	}
}
