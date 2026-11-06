using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class BacktestQueryValidationTests
{
    private const string Settings = "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0";

    private const string Grid = "&x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025&metric=sharpe";

    /// <summary>fake-threshold, P4-X2 default grid, default training share.</summary>
    internal const string Canonical = Settings + Grid + "&train=70";

    private static QueryCollection Query(string url) => new(QueryHelpers.ParseQuery(url));

    private static ValidationQuery Parse(string url)
    {
        BacktestQuery.TryParseValidation(Query(url), out var query, out var errors).Should().BeTrue(string.Join(" ", errors));
        return query!;
    }

    private static ValidationQuery Resolve(string url)
    {
        Parse(url).Resolve(BacktestQueryOptimizationTests.Threshold, out var effective).Should().BeEmpty();
        return effective!;
    }

    private static IReadOnlyList<QueryFieldError> Errors(string url)
    {
        BacktestQuery.TryParseValidation(Query(url), out var query, out var errors).Should().BeFalse();
        query.Should().BeNull();
        return errors;
    }

    [Fact] // PART-4 section 7 (BacktestQuery round trip), P4-X3: URL → model → URL gives the same canonical string
    public void Validation_RoundTrip_CanonicalUrlUnchanged()
    {
        var effective = Resolve(Canonical);

        effective.ToQueryString().Should().Be(Canonical);
        effective.IsCanonical(new QueryString("?" + Canonical)).Should().BeTrue();
    }

    [Fact] // T1, P4-X3: an absent train takes Conventions.DefaultTrainFraction (0.7), written 70 after metric
    public void Train_Absent_DefaultsToConventions()
    {
        BacktestQuery.DefaultTrainPercent.Should().Be((decimal)Conventions.DefaultTrainFraction * 100m).And.Be(70m);
        Parse(Settings).TrainPercent.Should().Be(70m);
        Resolve(Settings).ToQueryString().Should().Be(Canonical);
    }

    [Theory] // T1: trainFraction in [0.5, 0.9], bounds included; a decimal share is accepted (decision 11)
    [InlineData("50", 50)]
    [InlineData("90", 90)]
    [InlineData("72.5", 72.5)]
    [InlineData(" 70 ", 70)]
    public void Train_AcceptsBoundsAndDecimals(string text, double expected)
    {
        Parse(Settings + "&train=" + text).TrainPercent.Should().Be((decimal)expected);
    }

    [Theory] // T1: outside [50, 90] → a field error on train, never an exception
    [InlineData("49.9")]
    [InlineData("90.1")]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("-70")]
    public void Train_OutOfRange_IsFieldError(string text)
    {
        Errors(Settings + "&train=" + text).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(BacktestQuery.TrainKey, BacktestQuery.TrainRangeMessage));
        BacktestQuery.TrainRangeMessage.Should().Be("Training share must be between 50% and 90%.");
    }

    [Theory] // PART-4 section 7: an invalid URL value is a validation error, not an exception
    [InlineData("&train=abc", "Training share must be a number, with '.' as the decimal separator.")]
    [InlineData("&train=7e1", "Training share must be a number, with '.' as the decimal separator.")]
    [InlineData("&train=70&train=80", "Training share is given more than once.")]
    public void Train_InvalidOrRepeated_IsFieldError(string train, string message)
    {
        Errors(Settings + train).Should().ContainSingle().Which.Should().Be(new QueryFieldError(BacktestQuery.TrainKey, message));
    }

    [Theory] // P4-X3: the heatmap fields are read by the same code as /Optimize, so they give exactly the same errors
    [InlineData("symbol=a$&strategy=fake-threshold&capital=-1&x=lookback&xmin=abc&metric=best")]
    [InlineData("strategy=fake-threshold&from=2020-02-01&to=2020-01-01&fee=9&p.=1")]
    public void SharedFields_SameErrorsAsOptimization(string url)
    {
        BacktestQuery.TryParseOptimization(Query(url), out _, out var optimization).Should().BeFalse();

        Errors(url).Should().Equal(optimization);
        Errors(url + "&train=95").Should().Equal(optimization.Append(new QueryFieldError(BacktestQuery.TrainKey, BacktestQuery.TrainRangeMessage)));
    }

    [Fact] // P4-X3: canonical order = the order of /Optimize, then train (shortest form: 70.0 → 70)
    public void Canonical_TrainAfterMetric_ShortestForm()
    {
        Resolve(Settings + Grid + "&train=70.0").ToQueryString().Should().Be(Canonical);
        Resolve("train=65.50&" + Settings + Grid).ToQueryString().Should().Be(Settings + Grid + "&train=65.5");
    }

    [Theory] // P4-X3: only the exact canonical string is canonical (so the action redirects once); encoding does not matter
    [InlineData(Settings + Grid + "&train=70.0", false)]
    [InlineData(Settings + Grid, false)]
    [InlineData("train=70&" + Settings + Grid, false)]
    [InlineData(Settings + Grid + "&train=70&unknown=1", false)]
    [InlineData("symbol=AAPL&tf=daily&strategy=fake%2Dthreshold&capital=10000&fee=0%2E1&slip=0&rf=0" + Grid + "&train=70", true)]
    public void IsCanonical_OnlyTheExactString(string received, bool canonical)
    {
        Resolve(received).IsCanonical(new QueryString("?" + received)).Should().Be(canonical);
    }

    [Fact] // P4-X2 rules still apply on the train/test page (Resolve of the heatmap)
    public void Resolve_HeatmapRules_Apply()
    {
        Parse(Settings + "&x=lookback&y=lookback").Resolve(BacktestQueryOptimizationTests.Threshold, out var effective)
            .Should().Contain(new QueryFieldError(BacktestQuery.YKey, OptimizationQuery.SameParameterMessage));
        effective.Should().BeNull();
    }
}
