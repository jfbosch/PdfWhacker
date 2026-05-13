using System.Text.Json;

namespace PdfWhacker;

public sealed class PasswordStore
{
	public const string DefaultFileName = "appsettings.json";

	public IReadOnlyList<string> Passwords { get; }

	private PasswordStore(IReadOnlyList<string> passwords)
	{
		Passwords = passwords;
	}

	public static PasswordStore LoadFromBaseDirectory() =>
		LoadFromFile(Path.Combine(AppContext.BaseDirectory, DefaultFileName));

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
					passwords.Add(value.Trim());
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
