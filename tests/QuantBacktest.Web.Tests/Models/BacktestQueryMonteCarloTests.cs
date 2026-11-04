using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

/// <summary>sims, seed and mc of P4-X4 and of the PDF URL (PART-4 section 5): read and written by BacktestQuery only.</summary>
public class BacktestQueryMonteCarloTests
{
    private const string Canonical =
        "symbol=AAPL&tf=weekly&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0&p.fast=20&p.slow=50";

    private static QueryCollection Collection(string queryString) => new(QueryHelpers.ParseQuery(queryString));

    private static BacktestQuery Query(string queryString = Canonical)
    {
        BacktestQuery.TryParse(Collection(queryString), out var query, out _).Should().BeTrue();
        return query!;
    }

    private static MonteCarloRequest ParseMonteCarlo(string queryString)
    {
        BacktestQuery.TryParseMonteCarlo(Collection(queryString), out var request, out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        return request!;
    }

    private static IReadOnlyList<QueryFieldError> MonteCarloErrors(string queryString)
    {
        BacktestQuery.TryParseMonteCarlo(Collection(queryString), out var request, out var errors).Should().BeFalse();
        request.Should().BeNull();
        return errors;
    }

    [Theory] // P4-X4: absent or empty (GET form) sims / seed take the defaults of Conventions
    [InlineData(Canonical)]
    [InlineData(Canonical + "&sims=&seed=")]
    [InlineData(Canonical + "&sims=%20")]
    public void MonteCarlo_AbsentOrEmpty_UsesTheConventionDefaults(string queryString)
    {
        ParseMonteCarlo(queryString).Should().Be(
            new MonteCarloRequest(Conventions.DefaultMonteCarloSimulations, Conventions.DefaultMonteCarloSeed));
    }

    [Fact] // P4-X4: values read as received; mc is ignored by this reader
    public void MonteCarlo_ParsesSimsAndSeed()
    {
        ParseMonteCarlo(Canonical + "&mc=true&sims=2500&seed=-7").Should().Be(new MonteCarloRequest(2500, -7));
    }

    [Theory] // MC1: Simulations in [100, MaxMonteCarloSimulations], bounds included
    [InlineData(BacktestQuery.MinMonteCarloSimulations)]
    [InlineData(Conventions.MaxMonteCarloSimulations)]
    public void MonteCarlo_SimulationBounds_AreAccepted(int simulations)
    {
        ParseMonteCarlo($"sims={simulations}").Simulations.Should().Be(simulations);
        BacktestQuery.MinMonteCarloSimulations.Should().Be(100);
    }

    [Theory] // MC1 + PART-4 section 7: an invalid sims is a field error, never an exception
    [InlineData("sims=99")]
    [InlineData("sims=10001")]
    [InlineData("sims=abc")]
    [InlineData("sims=1.5")]
    [InlineData("sims=1e3")]
    [InlineData("sims=1,000")]
    [InlineData("sims=-100")]
    public void MonteCarlo_InvalidSims_IsAFieldError(string queryString)
    {
        MonteCarloErrors(queryString).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError("sims", "Simulations must be a whole number between 100 and 10000."));
    }

    [Fact] // P4-U12 rule of the reader: a repeated parameter is an error
    public void MonteCarlo_RepeatedSims_IsAFieldError()
    {
        MonteCarloErrors("sims=100&sims=200").Should().ContainSingle()
            .Which.Field.Should().Be("sims");
    }

    [Theory] // The contract sets no range for the seed: any Int32 is accepted (decision H4)
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void MonteCarlo_AnyInt32Seed_IsAccepted(int seed)
    {
        ParseMonteCarlo($"seed={seed}").Seed.Should().Be(seed);
    }

    [Theory] // decision H4: whole Int32 only
    [InlineData("seed=x")]
    [InlineData("seed=1.5")]
    [InlineData("seed=2147483648")]
    [InlineData("seed=-2147483649")]
    [InlineData("seed=1&seed=2")]
    public void MonteCarlo_InvalidSeed_IsAFieldError(string queryString)
    {
        MonteCarloErrors(queryString).Should().ContainSingle().Which.Field.Should().Be("seed");
    }

    [Fact] // P4-X4: canonical Monte Carlo URL = canonical backtest URL, then sims, then seed; never mc
    public void MonteCarloQueryString_IsTheBacktestQueryThenSimsAndSeed()
    {
        Query().ToQueryString(new MonteCarloRequest(2500, 7)).Should().Be(Canonical + "&sims=2500&seed=7");
    }

    [Fact] // PART-4 section 5: the PDF URL adds mc=true before sims and seed (decision H1)
    public void ReportQueryString_AddsMcThenSimsAndSeed()
    {
        Query().ToQueryString(new MonteCarloRequest(), forReport: true).Should().Be(Canonical + "&mc=true&sims=1000&seed=42");
    }

    [Fact] // PART-4 section 7: URL → model → identical URL
    public void MonteCarlo_RoundTrip_GivesTheSameUrl()
    {
        var url = Canonical + "&sims=300&seed=-5";

        Query(url).ToQueryString(ParseMonteCarlo(url)).Should().Be(url);
    }

    [Theory] // P4-X4: only the exact canonical form is canonical; anything else redirects once
    [InlineData(Canonical + "&sims=1000&seed=42", true)]
    [InlineData(Canonical + "&sims=1000&seed=%34%32", true)]
    [InlineData(Canonical, false)]
    [InlineData(Canonical + "&sims=1000", false)]
    [InlineData(Canonical + "&seed=42&sims=1000", false)]
    [InlineData(Canonical + "&mc=true&sims=1000&seed=42", false)]
    public void MonteCarlo_IsCanonical(string received, bool canonical)
    {
        Query(received).IsCanonical(new QueryString("?" + received), new MonteCarloRequest()).Should().Be(canonical);
    }

    [Fact] // P4-X4 (form): the hidden fields are the canonical pairs, decoded, in the order of ToQueryString
    public void QueryPairs_EncodeToTheCanonicalQueryString()
    {
        var query = Query(Canonical + "&from=2020-01-03&sl=5");

        var encoded = string.Join("&", query.ToQueryPairs().Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        encoded.Should().Be(query.ToQueryString());
    }

    [Fact] // P4-X4 (form, F11): hidden pairs + sims + seed, submitted in DOM order, is already the canonical URL
    public void FormSubmission_IsTheCanonicalMonteCarloQuery()
    {
        var query = Query();
        var submitted = query.ToQueryPairs()
            .Append(new("sims", "2000"))
            .Append(new("seed", "9"))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");
        var queryString = string.Join("&", submitted);

        var parsed = Query(queryString);
        var monteCarlo = ParseMonteCarlo(queryString);

        parsed.IsCanonical(new QueryString("?" + queryString), monteCarlo).Should().BeTrue();
        parsed.ToQueryString(monteCarlo).Should().Be(Canonical + "&sims=2000&seed=9");
    }
}
