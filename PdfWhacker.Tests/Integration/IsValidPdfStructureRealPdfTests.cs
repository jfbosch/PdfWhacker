using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(nameof(GhostscriptCollection))]
public class IsValidPdfStructureRealPdfTests
{
	private readonly IntegrationFixture _fx;

	public IsValidPdfStructureRealPdfTests(IntegrationFixture fx)
	{
		_fx = fx;
	}

	[Fact]
	public void Real_pdfs_from_the_fixture_set_pass_structural_validation()
	{
		// IsValidPdfStructure's unit tests use minimal synthetic PDFs. This test
		// pairs the synthetic coverage with real-world PDFs (linearized output,
		// xref streams, object streams) produced by an actual Ghostscript run, so
		// a regression in the tail-scan heuristic against realistic structure
		// would surface here.
		Assert.True(GhostscriptRunner.IsValidPdfStructure(_fx.BasePdf));
		Assert.True(GhostscriptRunner.IsValidPdfStructure(_fx.LargeBasePdf));
		Assert.True(GhostscriptRunner.IsValidPdfStructure(_fx.UserPwdEncryptedPdf));
		Assert.True(GhostscriptRunner.IsValidPdfStructure(_fx.OwnerOnlyEncryptedPdf));
		Assert.True(GhostscriptRunner.IsValidPdfStructure(_fx.OtherUserPwdEncryptedPdf));
	}
}
