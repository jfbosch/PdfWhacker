using PdfWhacker;

namespace PdfWhacker.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(nameof(GhostscriptCollection))]
public class PageCountProbeTests
{
	private readonly IntegrationFixture _fx;

	public PageCountProbeTests(IntegrationFixture fx)
	{
		_fx = fx;
	}

	[Fact]
	public void Returns_positive_page_count_for_known_fixtures()
	{
		Assert.True(_fx.GetPageCount(_fx.BasePdf) >= 1);
		Assert.True(_fx.GetPageCount(_fx.LargeBasePdf) >= 1);
	}
}
