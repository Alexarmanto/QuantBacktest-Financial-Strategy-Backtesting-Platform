using Microsoft.Extensions.Configuration;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Tests.Infrastructure;

public class FakeServicesStatusTests
{
    private static FakeServicesStatus Create(params (string Part, string Value)[] keys)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(keys.Select(k => new KeyValuePair<string, string?>($"QuantBacktest:UseFakes:{k.Part}", k.Value)))
            .Build();
        return new FakeServicesStatus(configuration);
    }

    [Fact]
    public void AllKeysTrue_ListsTheFiveParts_InContractOrder() // P4-U13
    {
        var status = Create(("Engine", "true"), ("Data", "true"), ("Analytics", "true"), ("Reports", "true"), ("Strategies", "true"));

        status.IsAnyActive.Should().BeTrue();
        status.ActiveParts.Should().Equal("Data", "Reports", "Strategies", "Analytics", "Engine");
        status.BannerText.Should().Be("Demo data: fake services active (Data, Reports, Strategies, Analytics, Engine)");
    }

    [Fact]
    public void AllKeysFalse_NoBanner() // P4-U13
    {
        var status = Create(("Data", "false"), ("Reports", "false"), ("Strategies", "false"), ("Analytics", "false"), ("Engine", "false"));

        status.IsAnyActive.Should().BeFalse();
        status.ActiveParts.Should().BeEmpty();
    }

    [Fact]
    public void MixedKeys_ListOnlyTheTrueOnes() // P4-U13
    {
        var status = Create(("Data", "false"), ("Reports", "true"), ("Engine", "true"));

        status.ActiveParts.Should().Equal("Reports", "Engine");
        status.BannerText.Should().Be("Demo data: fake services active (Reports, Engine)");
        status.IsFake("Reports").Should().BeTrue();
        status.IsFake("Data").Should().BeFalse();
    }

    [Fact]
    public void MissingKeys_AreTreatedAsFalse() // P4-U13, CONTRACT section 13 (default false)
    {
        var status = Create();

        status.IsAnyActive.Should().BeFalse();
        status.IsFake("Strategies").Should().BeFalse();
    }
}
