namespace PdfWhacker;

/// <summary>
/// Handles the failure outcomes shared between <see cref="RecursivePdfCompressor"/>
/// and <see cref="RecursivePdfDecryptor"/>. The two pipelines historically carried
/// byte-for-byte identical switch blocks for non-Ok outcomes; this consolidates the
/// logging plus error-detail recording so a future outcome value can't be patched
/// in one place and forgotten in the other.
///
/// <see cref="GhostscriptOutcome.EncryptedNoMatch"/> is intentionally NOT terminal
/// here — the compressor treats it as "skip", the decryptor as "try next password".
/// Callers must intercept that outcome themselves before delegating.
/// </summary>
internal static class RecursiveOutcomeReporter
{
	/// <summary>
	/// Logs and records the terminal-failure outcomes (TimedOut, Failed,
	/// MissingOutput, EmptyOutput, InvalidStructure). Returns true when the
	/// outcome was handled and the caller should bail out of this file. Returns
	/// false for Ok (caller continues to success path) or EncryptedNoMatch
	/// (caller-specific handling required).
	/// </summary>
	public static bool TryRecordFailure(
		GhostscriptOutcome outcome,
		GhostscriptResult result,
		string originalPath,
		Action<string, string> recordError)
	{
		switch (outcome)
		{
			case GhostscriptOutcome.Ok:
			case GhostscriptOutcome.EncryptedNoMatch:
				return false;

			case GhostscriptOutcome.TimedOut:
				Console.WriteLine("Ghostscript timed out.");
				recordError(originalPath, "ghostscript timed out");
				return true;

			case GhostscriptOutcome.Failed:
				Console.WriteLine($"Ghostscript exited with code {result.ExitCode}.");
				if (!string.IsNullOrWhiteSpace(result.StandardError))
					Console.WriteLine($"Stderr: {result.StandardError.Trim()}");
				recordError(originalPath,
					$"ghostscript exit code {result.ExitCode}: {PdfFs.Truncate(result.StandardError, 200)}");
				return true;

			case GhostscriptOutcome.MissingOutput:
				Console.WriteLine("Ghostscript did not produce an output file.");
				recordError(originalPath, "no output file produced");
				return true;

			case GhostscriptOutcome.EmptyOutput:
				Console.WriteLine("Ghostscript produced an empty output file.");
				recordError(originalPath, "output file is zero bytes");
				return true;

			case GhostscriptOutcome.InvalidStructure:
				Console.WriteLine("Output file failed PDF structural validation.");
				recordError(originalPath, "output file failed PDF structure check");
				return true;

			default:
				throw new InvalidOperationException($"Unhandled GhostscriptOutcome: {outcome}");
		}
	}
}
