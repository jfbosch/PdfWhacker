namespace PdfWhacker;

/// <summary>
/// Handles the four near-identical "Ghostscript failed somehow, fall back to the
/// archived original" branches that previously lived in <see cref="PdfCompressor"/>
/// and <see cref="PdfDecryptor"/>. A single home so a future outcome value can't
/// be silently forgotten in one pipeline but handled in another.
/// </summary>
internal static class WatchOutcomeReporter
{
	public enum FallbackVerb
	{
		CopyToOutput,
		PassThrough,
	}

	/// <summary>
	/// If <paramref name="outcome"/> represents anything other than success, logs
	/// the appropriate failure message, copies the archived original over to the
	/// output folder, and deletes the input. Returns true when the outcome was
	/// handled; false when the caller should continue down the success path.
	///
	/// Passing <see cref="GhostscriptOutcome.EncryptedNoMatch"/> is treated as a
	/// terminal failure here. Callers that loop over passwords (e.g.
	/// <see cref="PdfDecryptor"/>) must intercept that outcome themselves to
	/// continue iterating.
	/// </summary>
	public static bool TryApplyFallback(
		GhostscriptOutcome outcome,
		GhostscriptResult result,
		string archivePath,
		string outputPath,
		string inputPath,
		FallbackVerb verb,
		string? scrubSecret = null)
	{
		string verbLabel = verb == FallbackVerb.CopyToOutput
			? "copying original to output"
			: "passing original through";

		switch (outcome)
		{
			case GhostscriptOutcome.Ok:
				return false;

			case GhostscriptOutcome.EncryptedNoMatch:
				Console.WriteLine($"PDF is password-protected; {verbLabel}.");
				break;

			case GhostscriptOutcome.TimedOut:
				Console.WriteLine($"Ghostscript timed out; {verbLabel}.");
				break;

			case GhostscriptOutcome.Failed:
				Console.WriteLine($"Ghostscript exited with code {result.ExitCode}; {verbLabel}.");
				if (!string.IsNullOrWhiteSpace(result.StandardError))
					Console.WriteLine($"Stderr: {Scrub(result.StandardError, scrubSecret).Trim()}");
				break;

			case GhostscriptOutcome.MissingOutput:
			case GhostscriptOutcome.EmptyOutput:
			case GhostscriptOutcome.InvalidStructure:
				Console.WriteLine($"Ghostscript output missing or failed structural validation; {verbLabel}.");
				break;

			default:
				throw new InvalidOperationException($"Unhandled GhostscriptOutcome: {outcome}");
		}

		File.Copy(archivePath, outputPath, overwrite: true);
		PdfFs.SafeDelete(inputPath);
		return true;
	}

	// Defensive: today Ghostscript doesn't echo -sPDFPassword= to stderr, but a
	// future gs version might. Cheap insurance against the entire password landing
	// in a console log or a piped redirect.
	private static string Scrub(string text, string? secret)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret))
			return text;
		return text.Replace(secret, "***");
	}
}
