using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

/// <summary>mc of the PDF URL (PART-4 section 5, decision D1): read and written by BacktestQuery only.</summary>
public class BacktestQueryReportTests
{
    private const string Canonical =
        "symbol=AAPL&tf=weekly&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0&p.fast=20&p.slow=50";

    private static QueryCollection Collection(string queryString) => new(QueryHelpers.ParseQuery(queryString));

    private static BacktestQuery Query(string queryString = Canonical)
    {
        BacktestQuery.TryParse(Collection(queryString), out var query, out _).Should().BeTrue();
        return query!;
    }

    [Theory] // D1: absent or empty (GET form) → no Monte Carlo section
    [InlineData(Canonical)]
    [InlineData(Canonical + "&mc=")]
    [InlineData(Canonical + "&mc=%20&sims=1000&seed=42")]
    public void ReportFlag_AbsentOrEmpty_IsFalse(string queryString)
    {
        BacktestQuery.TryParseReportFlag(Collection(queryString), out var withMonteCarlo, out var errors).Should().BeTrue();
        withMonteCarlo.Should().BeFalse();
        errors.Should().BeEmpty();
    }

    [Theory] // D1: "true" in any case adds the section (the canonical redirect then writes "true")
    [InlineData("mc=true")]
    [InlineData("mc=TRUE")]
    [InlineData("mc=True")]
    public void ReportFlag_True_AnyCase_IsTrue(string queryString)
    {
        BacktestQuery.TryParseReportFlag(Collection(queryString), out var withMonteCarlo, out var errors).Should().BeTrue();
        withMonteCarlo.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Theory] // D1: any other value is a field error, never an exception
    [InlineData("mc=false")]
    [InlineData("mc=1")]
    [InlineData("mc=yes")]
    public void ReportFlag_OtherValue_IsAFieldError(string queryString)
    {
        BacktestQuery.TryParseReportFlag(Collection(queryString), out var withMonteCarlo, out var errors).Should().BeFalse();
        withMonteCarlo.Should().BeFalse();
        errors.Should().Equal(new QueryFieldError(BacktestQuery.MonteCarloKey, BacktestQuery.MonteCarloFlagMessage));
    }

    [Fact] // D1, same rule as every parameter: given twice is an error
    public void ReportFlag_Repeated_IsAFieldError()
    {
        BacktestQuery.TryParseReportFlag(Collection("mc=true&mc=true"), out var withMonteCarlo, out var errors).Should().BeFalse();
        withMonteCarlo.Should().BeFalse();
        errors.Should().Equal(new QueryFieldError(BacktestQuery.MonteCarloKey, "Monte Carlo flag (mc) is given more than once."));
    }

    [Theory] // D4: the PDF URL with Monte Carlo is canonical only as …&mc=true&sims=&seed=
    [InlineData(Canonical + "&mc=true&sims=1000&seed=42", true)]
    [InlineData(Canonical + "&sims=1000&seed=42", false)]
    [InlineData(Canonical + "&mc=TRUE&sims=1000&seed=42", false)]
    [InlineData(Canonical + "&sims=1000&seed=42&mc=true", false)]
    [InlineData(Canonical + "&mc=true", false)]
    public void IsCanonical_ForReport_AcceptsOnlyTheMcUrl(string received, bool expected)
    {
        Query().IsCanonical(new QueryString("?" + received), new MonteCarloRequest(), forReport: true).Should().Be(expected);
    }

    [Fact] // D4: URL → model → URL gives the same PDF URL
    public void IsCanonical_ForReport_RoundTrip()
    {
        const string url = Canonical + "&mc=true&sims=2500&seed=-7";
        var collection = Collection(url);

        BacktestQuery.TryParseReportFlag(collection, out var withMonteCarlo, out _).Should().BeTrue();
        BacktestQuery.TryParseMonteCarlo(collection, out var request, out _).Should().BeTrue();
        withMonteCarlo.Should().BeTrue();
        var query = Query(url);

        query.ToQueryString(request!, forReport: true).Should().Be(url);
        query.IsCanonical(new QueryString("?" + url), request!, forReport: true).Should().BeTrue();
    }
}
