using System.Text.Json;

namespace PdfWhacker;

internal sealed class PasswordStore
{
	public const string DefaultFileName = "appsettings.json";

	public IReadOnlyList<string> Passwords { get; }

	private PasswordStore(IReadOnlyList<string> passwords)
	{
		Passwords = passwords;
	}

	/// <summary>
	/// Resolves the configuration file the user is most likely to be editing.
	/// Prefers %APPDATA%/PdfWhacker/appsettings.json so a user-local install can edit
	/// the file without admin rights and without losing their copy to a dotnet publish
	/// that rewrites the binary-directory copy. Falls back to the binary directory.
	/// </summary>
	public static PasswordStore LoadFromBaseDirectory()
	{
		string? appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		if (!string.IsNullOrEmpty(appData))
		{
			string userPath = Path.Combine(appData, "PdfWhacker", DefaultFileName);
			if (File.Exists(userPath))
				return LoadFromFile(userPath);
		}
		return LoadFromFile(Path.Combine(AppContext.BaseDirectory, DefaultFileName));
	}

	public static PasswordStore LoadFromFile(string filePath)
	{
		if (!File.Exists(filePath))
		{
			Console.WriteLine($"No password configuration found at '{filePath}'; continuing with no configured passwords.");
			return new PasswordStore(Array.Empty<string>());
		}

		string json;
		try
		{
			json = File.ReadAllText(filePath);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to read password configuration '{filePath}': {ex.Message}. Continuing with no configured passwords.");
			return new PasswordStore(Array.Empty<string>());
		}

		List<string> passwords = new();
		try
		{
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind == JsonValueKind.Object &&
				doc.RootElement.TryGetProperty("Passwords", out var passwordsElement) &&
				passwordsElement.ValueKind == JsonValueKind.Array)
			{
				foreach (var element in passwordsElement.EnumerateArray())
				{
					if (element.ValueKind != JsonValueKind.String)
						continue;
					string? value = element.GetString();
					if (string.IsNullOrWhiteSpace(value))
						continue;
					// Do NOT trim: leading/trailing whitespace can be intentional in a
					// password, and a configured "secret " with a stray space must NOT
					// be silently turned into "secret" — that would never match.
					passwords.Add(value);
				}
			}
		}
		catch (JsonException ex)
		{
			Console.WriteLine($"Password configuration '{filePath}' is not valid JSON: {ex.Message}. Continuing with no configured passwords.");
			return new PasswordStore(Array.Empty<string>());
		}

		if (passwords.Count == 0)
			Console.WriteLine($"Password configuration '{filePath}' contains no passwords; only owner-password-only PDFs will be decryptable.");

		return new PasswordStore(passwords);
	}
}
