namespace PdfWhacker;

/// <summary>
/// Single categorization of a Ghostscript run that previously lived as a chain of
/// if-statements duplicated across PdfCompressor, PdfDecryptor, and their recursive
/// twins.
/// </summary>
public enum GhostscriptOutcome
{
	Ok,
	EncryptedNoMatch,
	TimedOut,
	Failed,
	MissingOutput,
	EmptyOutput,
	InvalidStructure,
}

public static class PdfPipeline
{
	/// <summary>
	/// Looks at the raw <paramref name="result"/> plus the actual output file at
	/// <paramref name="outputPath"/> and reports the canonical outcome.
	///
	/// Ordering is important and matches the historical behaviour:
	///   PasswordProtected → EncryptedNoMatch (don't probe output, file may not exist)
	///   TimedOut          → TimedOut
	///   ExitCode != 0     → Failed
	///   No output file    → MissingOutput
	///   Empty output      → EmptyOutput
	///   Invalid PDF       → InvalidStructure
	///   otherwise         → Ok
	/// </summary>
	public static GhostscriptOutcome Classify(GhostscriptResult result, string outputPath)
	{
		if (result.PasswordProtected)
			return GhostscriptOutcome.EncryptedNoMatch;
		if (result.TimedOut)
			return GhostscriptOutcome.TimedOut;
		if (!result.Succeeded)
			return GhostscriptOutcome.Failed;
		if (!File.Exists(outputPath))
			return GhostscriptOutcome.MissingOutput;
		if (new FileInfo(outputPath).Length == 0)
			return GhostscriptOutcome.EmptyOutput;
		if (!GhostscriptRunner.IsValidPdfStructure(outputPath))
			return GhostscriptOutcome.InvalidStructure;
		return GhostscriptOutcome.Ok;
	}
}
