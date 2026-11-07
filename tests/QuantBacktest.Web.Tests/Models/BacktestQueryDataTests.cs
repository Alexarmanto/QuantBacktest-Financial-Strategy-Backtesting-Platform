using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

/// <summary>The URL of the data pages (P4-U10, PART-4 section 5, P4-U16): symbol required, tf daily by default.</summary>
public class BacktestQueryDataTests
{
    private static QueryCollection Query(string queryString) => new(QueryHelpers.ParseQuery(queryString));

    private static FormCollection Form(params (string Key, string Value)[] fields) =>
        new(fields.ToDictionary(f => f.Key, f => new StringValues(f.Value)));

    [Theory] // PART-4 section 5: symbol normalized in upper case; tf daily or weekly in any case, absent = daily
    [InlineData("symbol=aapl&tf=daily", "AAPL", Timeframe.Daily)]
    [InlineData("symbol=%20msft%20&tf=WEEKLY", "MSFT", Timeframe.Weekly)]
    [InlineData("symbol=brk.b", "BRK.B", Timeframe.Daily)]
    [InlineData("symbol=SPY&tf=", "SPY", Timeframe.Daily)]
    public void TryParseDataSeries_Valid_NormalizesSymbolAndTimeframe(string queryString, string symbol, Timeframe timeframe)
    {
        BacktestQuery.TryParseDataSeries(Query(queryString), out var result, out var errors).Should().BeTrue();

        errors.Should().BeEmpty();
        result.Should().Be(new DataSeriesQuery(symbol, timeframe));
    }

    [Theory] // P4-U10: symbol is required (tf alone is not enough)
    [InlineData("")]
    [InlineData("tf=weekly")]
    [InlineData("symbol=&tf=")]
    public void TryParseDataSeries_MissingSymbol_FieldError(string queryString)
    {
        BacktestQuery.TryParseDataSeries(Query(queryString), out var result, out var errors).Should().BeFalse();

        result.Should().BeNull();
        errors.Should().Equal(new QueryFieldError("symbol", BacktestQuery.SymbolRequiredMessage));
    }

    [Theory] // Conventions.SymbolPattern, tf daily|weekly, repeated parameter: field errors, never an exception
    [InlineData("symbol=BAD%20SYMBOL!", "symbol", BacktestQuery.SymbolFormatMessage)]
    [InlineData("symbol=ABCDEFGHIJK", "symbol", BacktestQuery.SymbolFormatMessage)]
    [InlineData("symbol=AAPL&tf=monthly", "tf", BacktestQuery.TimeframeFormatMessage)]
    [InlineData("symbol=AAPL&symbol=MSFT", "symbol", "Symbol is given more than once.")]
    public void TryParseDataSeries_InvalidValue_FieldErrorWithoutException(string queryString, string field, string message)
    {
        BacktestQuery.TryParseDataSeries(Query(queryString), out var result, out var errors).Should().BeFalse();

        result.Should().BeNull();
        errors.Should().Equal(new QueryFieldError(field, message));
    }

    [Theory] // P4-U16: URL → model → URL gives the same canonical URL
    [InlineData("symbol=AAPL&tf=daily")]
    [InlineData("symbol=BRK.B&tf=weekly")]
    public void ToDataQueryString_RoundTrip_UrlToModelToUrl(string canonical)
    {
        BacktestQuery.TryParseDataSeries(Query(canonical), out var series, out _).Should().BeTrue();

        series!.ToQueryString().Should().Be(canonical);
        BacktestQuery.IsCanonicalData(new QueryString("?" + canonical), series).Should().BeTrue();
    }

    [Theory] // PART-4 section 5: lower-case symbol, tf absent (default daily) or in another case, unknown key, other order
    [InlineData("symbol=aapl&tf=daily")]
    [InlineData("symbol=AAPL")]
    [InlineData("symbol=AAPL&tf=Daily")]
    [InlineData("symbol=AAPL&tf=daily&x=1")]
    [InlineData("tf=daily&symbol=AAPL")]
    public void IsCanonicalData_NonCanonical_False(string queryString)
    {
        BacktestQuery.IsCanonicalData(new QueryString("?" + queryString), new DataSeriesQuery("AAPL", Timeframe.Daily))
            .Should().BeFalse();
    }

    [Fact] // a percent-encoded spelling of the canonical URL is canonical: no redirect loop
    public void IsCanonicalData_PercentEncodedSameValues_True()
    {
        BacktestQuery.IsCanonicalData(new QueryString("?symbol=BRK%2EB&tf=weekly"), new DataSeriesQuery("BRK.B", Timeframe.Weekly))
            .Should().BeTrue();
    }

    [Fact] // POST /Data/Refresh: the form is read with the rules of the URL, tf absent = daily
    public void TryParseRefresh_Valid_SameRulesAsTheUrl()
    {
        BacktestQuery.TryParseRefresh(Form(("symbol", " ibm ")), out var result, out var errors).Should().BeTrue();

        errors.Should().BeEmpty();
        result.Should().Be(new DataSeriesQuery("IBM", Timeframe.Daily));
    }

    [Fact] // POST /Data/Refresh: an invalid field is a field error, as in the URL
    public void TryParseRefresh_Invalid_FieldErrors()
    {
        BacktestQuery.TryParseRefresh(Form(("symbol", "bad symbol"), ("tf", "monthly")), out var result, out var errors).Should().BeFalse();

        result.Should().BeNull();
        errors.Should().Equal(
            new QueryFieldError("symbol", BacktestQuery.SymbolFormatMessage),
            new QueryFieldError("tf", BacktestQuery.TimeframeFormatMessage));
    }

    [Theory] // P4-U10: the form of /Data shows the symbol as typed; tf absent, unknown or invalid = daily; other keys ignored
    [InlineData("", "", Timeframe.Daily)]
    [InlineData("symbol=ibm&tf=weekly&strategy=x", "ibm", Timeframe.Weekly)]
    [InlineData("symbol=BAD%20SYMBOL!&tf=monthly", "BAD SYMBOL!", Timeframe.Daily)]
    public void ReadRefreshPrefill_KeepsTheInput(string queryString, string symbol, Timeframe timeframe)
    {
        BacktestQuery.ReadRefreshPrefill(Query(queryString)).Should().Be((symbol, timeframe));
    }

    [Theory] // the redirect of an invalid refresh sends the input back as typed (trimmed), blank fields left out
    [InlineData("bad symbol", "monthly", "symbol=bad%20symbol&tf=monthly")]
    [InlineData("  ", "weekly", "tf=weekly")]
    [InlineData("", "", "")]
    public void ToRefreshPrefillQueryString_SendsTheInputBack(string symbol, string tf, string expected)
    {
        BacktestQuery.ToRefreshPrefillQueryString(Form(("symbol", symbol), ("tf", tf))).Should().Be(expected);
    }
}
