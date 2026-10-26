using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class BacktestQueryTests
{
    private const string FullCanonical =
        "symbol=AAPL&tf=weekly&strategy=sma-crossover&from=2020-01-02&to=2024-12-31&capital=25000.5&fee=0.25"
        + "&slip=0.05&sl=5&tp=12.5&ts=3&rf=2&p.fast=20&p.slow=50";

    private const string MinimalCanonical = "symbol=AAPL&tf=daily&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0";

    private static IQueryCollection Query(string queryString) => new QueryCollection(QueryHelpers.ParseQuery(queryString));

    private static BacktestQuery Parse(string queryString)
    {
        BacktestQuery.TryParse(Query(queryString), out var result, out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        return result!;
    }

    private static IReadOnlyList<QueryFieldError> Errors(string queryString)
    {
        BacktestQuery.TryParse(Query(queryString), out var result, out var errors).Should().BeFalse();
        result.Should().BeNull();
        return errors;
    }

    // ---- Canonical URL ----

    [Theory] // P4-U16, PART-4 section 7: URL → model → identical URL
    [InlineData(FullCanonical)]
    [InlineData(MinimalCanonical)]
    public void CanonicalUrl_RoundTrips(string canonical)
    {
        Parse(canonical).ToQueryString().Should().Be(canonical);
    }

    [Fact] // P4-U16, PART-4 section 5: fixed order, shortest invariant numbers, tf in lower case, p.* sorted
    public void NonCanonicalUrl_IsNormalized()
    {
        var query = Parse("p.slow=50&rf=&tf=WEEKLY&p.fast=20.0&capital=10000.00&strategy=sma-crossover&symbol=%20aapl%20&fee=0.10&slip=0");

        query.ToQueryString().Should().Be(
            "symbol=AAPL&tf=weekly&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0&p.fast=20&p.slow=50");
    }

    [Theory] // P4-U16: Parse → ToQueryString → Parse → ToQueryString is stable
    [InlineData("p.slow=50&rf=&tf=WEEKLY&p.fast=20.0&capital=10000.00&strategy=sma-crossover&symbol=%20aapl%20&fee=0.10")]
    [InlineData("symbol=msft&strategy=x&from=2021-03-04&sl=05.50&tp=&ts=0.5&rf=1.250&p.b=1&p.a=-0&p.a2=0.00001")]
    [InlineData(FullCanonical)]
    public void Canonicalization_IsIdempotent(string queryString)
    {
        var once = Parse(queryString).ToQueryString();

        Parse(once).ToQueryString().Should().Be(once);
    }

    [Fact] // PART-4 section 5: a small value never becomes an exponent such as 1E-05
    public void SmallValues_StayInPlainDecimalNotation()
    {
        var query = Parse("symbol=AAPL&strategy=x&p.threshold=0.00001&slip=0.0001");

        var url = query.ToQueryString();
        url.Should().Contain("p.threshold=0.00001").And.Contain("slip=0.0001").And.NotContainAny("E", "e-");
        Parse(url).ToQueryString().Should().Be(url);
        query.ToBacktestRequest().Parameters["threshold"].Should().Be(0.00001);
    }

    [Fact] // PART-4 section 5: unknown parameters (mc, sims, seed...) are ignored and not written back
    public void UnknownParameters_AreIgnored()
    {
        Parse("symbol=AAPL&strategy=sma-crossover&mc=true&sims=1000&seed=42").ToQueryString().Should().Be(MinimalCanonical);
    }

    [Fact] // PART-4 section 5: from, to, sl, tp, ts are written only when set
    public void OptionalFields_AreWrittenOnlyWhenSet()
    {
        var url = Parse("symbol=AAPL&strategy=x&to=2024-06-30&ts=2").ToQueryString();

        url.Should().Be("symbol=AAPL&tf=daily&strategy=x&to=2024-06-30&capital=10000&fee=0.1&slip=0&ts=2&rf=0");
    }

    [Fact] // PART-4 section 5: values are escaped
    public void Values_AreUrlEscaped()
    {
        Parse("symbol=BRK.B&strategy=custom-a%26b").ToQueryString().Should().StartWith("symbol=BRK.B&tf=daily&strategy=custom-a%26b&");
    }

    // ---- Defaults and tolerant reading ----

    [Fact] // PART-4 section 5 and section 7: defaults applied, taken from Conventions
    public void MissingFields_GetTheDefaults()
    {
        var query = Parse("symbol=AAPL&strategy=sma-crossover");

        query.Timeframe.Should().Be(Timeframe.Daily);
        query.From.Should().BeNull();
        query.To.Should().BeNull();
        query.Capital.Should().Be(10000m);
        query.FeePercent.Should().Be(0.1m);
        query.SlippagePercent.Should().Be(0m);
        query.StopLossPercent.Should().BeNull();
        query.TakeProfitPercent.Should().BeNull();
        query.TrailingStopPercent.Should().BeNull();
        query.RiskFreePercent.Should().Be(0m);
        query.Parameters.Should().BeEmpty();
    }

    [Fact] // PART-4 section 5: a GET form sends every field, so an empty value means absent
    public void EmptyValues_CountAsAbsent()
    {
        var query = Parse("symbol=AAPL&tf=&strategy=sma-crossover&from=&to=&capital=&fee=&slip=&sl=&tp=&ts=&rf=%20&p.fast=");

        query.ToQueryString().Should().Be(MinimalCanonical);
        query.Parameters.Should().BeEmpty();
    }

    [Theory] // PART-4 section 5: symbol normalised (trim + upper case); tf case-insensitive
    [InlineData("%20aapl%20", "daily", "AAPL", Timeframe.Daily)]
    [InlineData("brk.b", "Weekly", "BRK.B", Timeframe.Weekly)]
    [InlineData("SPY", "DAILY", "SPY", Timeframe.Daily)]
    public void Symbol_AndTimeframe_AreNormalized(string symbol, string tf, string expectedSymbol, Timeframe expectedTimeframe)
    {
        var query = Parse($"symbol={symbol}&tf={tf}&strategy=x");

        query.Symbol.Should().Be(expectedSymbol);
        query.Timeframe.Should().Be(expectedTimeframe);
    }

    [Fact] // PART-4 section 5: p.<key> → strategy parameter <key>; absent p.* stay absent (CONTRACT section 3: default)
    public void StrategyParameters_AreReadFromThePrefix()
    {
        var query = Parse("symbol=AAPL&strategy=sma-crossover&p.fast=20&p.stdDevs=2.5");

        query.Parameters.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["fast"] = 20m, ["stdDevs"] = 2.5m });
        query.ToBacktestRequest().Parameters.Should().BeEquivalentTo(new Dictionary<string, double> { ["fast"] = 20, ["stdDevs"] = 2.5 });
    }

    // ---- Conversion to the contract ----

    [Fact] // PART-4 section 5 and section 7: percent → fraction (value / 100), CONTRACT section 3
    public void ToBacktestRequest_ConvertsPercentToFractions()
    {
        var request = Parse(FullCanonical.Replace("fee=0.25", "fee=0.1", StringComparison.Ordinal)).ToBacktestRequest();

        request.Symbol.Should().Be("AAPL");
        request.Timeframe.Should().Be(Timeframe.Weekly);
        request.StrategyId.Should().Be("sma-crossover");
        request.From.Should().Be(new DateOnly(2020, 1, 2));
        request.To.Should().Be(new DateOnly(2024, 12, 31));
        request.Parameters.Should().BeEquivalentTo(new Dictionary<string, double> { ["fast"] = 20, ["slow"] = 50 });
        request.Execution.Should().Be(new ExecutionSettings(
            InitialCapital: 25000.5m,
            FeeRate: 0.001m,
            SlippageRate: 0.0005m,
            StopLossFraction: 0.05m,
            TakeProfitFraction: 0.125m,
            TrailingStopFraction: 0.03m,
            RiskFreeRate: 0.02));
    }

    [Fact] // PART-4 section 5: defaults convert to the defaults of the contract
    public void ToBacktestRequest_WithDefaults_EqualsTheContractDefaults()
    {
        var request = Parse("symbol=AAPL&strategy=x").ToBacktestRequest();

        request.Execution.Should().Be(ExecutionSettings.Default);
        request.Parameters.Should().BeEmpty();
    }

    // ---- Input validation (errors, never exceptions) ----

    [Theory] // P4-U1 (server side), PART-4 section 7: invalid URL parameters → validation errors
    [InlineData("strategy=x", "symbol", "Symbol is required.")]
    [InlineData("symbol=AA%20PL&strategy=x", "symbol", "Symbol must be 1 to 10 characters: letters, digits, '.' or '-'.")]
    [InlineData("symbol=ABCDEFGHIJK&strategy=x", "symbol", "Symbol must be 1 to 10 characters: letters, digits, '.' or '-'.")]
    [InlineData("symbol=AAPL&strategy=x&tf=monthly", "tf", "Timeframe must be 'daily' or 'weekly'.")]
    [InlineData("symbol=AAPL&strategy=x&tf=0", "tf", "Timeframe must be 'daily' or 'weekly'.")]
    [InlineData("symbol=AAPL&strategy=x&tf=1", "tf", "Timeframe must be 'daily' or 'weekly'.")]
    [InlineData("symbol=AAPL", "strategy", "Strategy is required.")]
    [InlineData("symbol=AAPL&strategy=x&from=2024-13-01", "from", "Start date must be a date in the yyyy-MM-dd format.")]
    [InlineData("symbol=AAPL&strategy=x&to=31/12/2024", "to", "End date must be a date in the yyyy-MM-dd format.")]
    [InlineData("symbol=AAPL&strategy=x&from=2024-06-02&to=2024-06-01", "from", "The start date must be on or before the end date.")]
    [InlineData("symbol=AAPL&strategy=x&capital=0", "capital", "Initial capital must be greater than 0.")]
    [InlineData("symbol=AAPL&strategy=x&capital=-100", "capital", "Initial capital must be greater than 0.")]
    [InlineData("symbol=AAPL&strategy=x&capital=10,000", "capital", "Initial capital must be a number, with '.' as the decimal separator.")]
    [InlineData("symbol=AAPL&strategy=x&capital=1e4", "capital", "Initial capital must be a number, with '.' as the decimal separator.")]
    [InlineData("symbol=AAPL&strategy=x&fee=0,1", "fee", "Fee must be a number, with '.' as the decimal separator.")]
    [InlineData("symbol=AAPL&strategy=x&rf=NaN", "rf", "Risk-free rate must be a number, with '.' as the decimal separator.")]
    [InlineData("symbol=AAPL&strategy=x&fee=0.1&fee=0.2", "fee", "Fee is given more than once.")]
    [InlineData("symbol=AAPL&strategy=x&p.fast=abc", "p.fast", "Parameter 'fast' must be a number, with '.' as the decimal separator.")]
    [InlineData("symbol=AAPL&strategy=x&p.=5", "p.", "A parameter name is missing after 'p.'.")]
    public void InvalidInput_GivesAFieldError(string queryString, string field, string message)
    {
        Errors(queryString).Should().ContainSingle().Which.Should().Be(new QueryFieldError(field, message));
    }

    [Theory] // CONTRACT section 4 (validation ranges, Conventions shown in percent), PART-4 section 7
    [InlineData("fee=-0.01", "fee", "Fee must be between 0% and 5%.")]
    [InlineData("fee=5.01", "fee", "Fee must be between 0% and 5%.")]
    [InlineData("slip=-1", "slip", "Slippage must be between 0% and 2%.")]
    [InlineData("slip=2.001", "slip", "Slippage must be between 0% and 2%.")]
    [InlineData("sl=0", "sl", "Stop-loss must be greater than 0% and at most 50% (leave empty to disable).")]
    [InlineData("sl=50.5", "sl", "Stop-loss must be greater than 0% and at most 50% (leave empty to disable).")]
    [InlineData("tp=-3", "tp", "Take-profit must be greater than 0% and at most 500% (leave empty to disable).")]
    [InlineData("tp=500.1", "tp", "Take-profit must be greater than 0% and at most 500% (leave empty to disable).")]
    [InlineData("ts=0", "ts", "Trailing stop must be greater than 0% and at most 50% (leave empty to disable).")]
    [InlineData("ts=51", "ts", "Trailing stop must be greater than 0% and at most 50% (leave empty to disable).")]
    [InlineData("rf=-0.5", "rf", "Risk-free rate must be between 0% and 20%.")]
    [InlineData("rf=20.01", "rf", "Risk-free rate must be between 0% and 20%.")]
    public void OutOfRangeSettings_GiveAFieldError(string setting, string field, string message)
    {
        Errors("symbol=AAPL&strategy=x&" + setting).Should().ContainSingle().Which.Should().Be(new QueryFieldError(field, message));
    }

    [Theory] // CONTRACT section 4: range bounds are accepted ([0, Max] or ]0, Max])
    [InlineData("fee=0&slip=0&rf=0")]
    [InlineData("fee=5&slip=2&sl=50&tp=500&ts=50&rf=20")]
    [InlineData("sl=0.01&tp=0.01&ts=0.01")]
    [InlineData("from=2024-06-01&to=2024-06-01")]
    public void RangeBounds_AreAccepted(string settings)
    {
        Parse("symbol=AAPL&strategy=x&" + settings);
    }

    [Fact] // P4-U1: every error is reported at once, in field order
    public void SeveralErrors_AreAllReported()
    {
        var errors = Errors("symbol=@@&tf=hourly&capital=0&fee=9&sl=abc&p.fast=x");

        errors.Select(e => e.Field).Should().Equal("symbol", "tf", "strategy", "capital", "fee", "sl", "p.fast");
    }

    [Theory] // PART-4 section 7: hostile input never throws
    [InlineData("symbol=%00&from=0000-00-00&to=9999-99-99")]
    [InlineData("symbol=AAPL&strategy=x&capital=99999999999999999999999999999999999")]
    [InlineData("symbol=AAPL&strategy=x&p.fast=1e400&rf=Infinity&fee=-")]
    [InlineData("&&=&p.&p..=1&%=")]
    public void HostileInput_GivesErrors_NotExceptions(string queryString)
    {
        var parse = () => BacktestQuery.TryParse(Query(queryString), out _, out _);

        parse.Should().NotThrow().Which.Should().BeFalse();
    }

    [Fact] // P4-U14, CLAUDE.md: invariant culture, whatever the culture of the machine
    public void Parsing_AndFormatting_DoNotDependOnTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            Parse("symbol=AAPL&strategy=x&capital=1234.5&fee=0.25&from=2024-01-31").ToQueryString()
                .Should().Be("symbol=AAPL&tf=daily&strategy=x&from=2024-01-31&capital=1234.5&fee=0.25&slip=0&rf=0");
            Errors("symbol=AAPL&strategy=x&fee=0,25").Should().ContainSingle().Which.Field.Should().Be("fee");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- Strategy parameters against the catalog definitions ----

    private static readonly IReadOnlyList<ParameterDefinition> Definitions =
    [
        new ParameterDefinition("fast", "Fast period", ParameterType.Integer, 20, 2, 200, 1),
        new ParameterDefinition("threshold", "Threshold", ParameterType.Decimal, 0.02, 0, 0.5, 0.005),
    ];

    [Theory] // CONTRACT section 4: known key, value in [Min, Max], whole number if Integer
    [InlineData("p.fast=20&p.threshold=0.02")]
    [InlineData("p.fast=2&p.threshold=0")]
    [InlineData("p.fast=200&p.threshold=0.5")]
    [InlineData("p.fast=20.0")]
    [InlineData("")]
    public void ValidParameters_GiveNoError(string parameters)
    {
        Parse("symbol=AAPL&strategy=x&" + parameters).ValidateParameters(Definitions).Should().BeEmpty();
    }

    [Theory] // CONTRACT section 4, PART-4 section 7 (parameter out of range)
    [InlineData("p.fast=1", "p.fast", "Fast period must be between 2 and 200.")]
    [InlineData("p.fast=201", "p.fast", "Fast period must be between 2 and 200.")]
    [InlineData("p.fast=20.5", "p.fast", "Fast period must be a whole number.")]
    [InlineData("p.threshold=0.6", "p.threshold", "Threshold must be between 0 and 0.5.")]
    [InlineData("p.slow=50", "p.slow", "Unknown parameter 'slow' for this strategy.")]
    [InlineData("p.Fast=20", "p.Fast", "Unknown parameter 'Fast' for this strategy.")]
    public void InvalidParameters_GiveAFieldError(string parameter, string field, string message)
    {
        Parse("symbol=AAPL&strategy=x&" + parameter).ValidateParameters(Definitions)
            .Should().ContainSingle().Which.Should().Be(new QueryFieldError(field, message));
    }

    [Fact] // CONTRACT section 4: a strategy without parameters rejects any p.*
    public void StrategyWithoutParameters_RejectsAnyParameter()
    {
        Parse("symbol=AAPL&strategy=buy-and-hold&p.fast=20").ValidateParameters([])
            .Should().ContainSingle().Which.Field.Should().Be("p.fast");
    }

    // ---- Effective parameters and canonical check (PART-4 section 5) ----

    [Fact] // correction 5: missing p.* get the catalog default, received ones are kept
    public void WithDefaultParameters_MakesEveryParameterExplicit()
    {
        var query = Parse("symbol=AAPL&strategy=x&p.threshold=0.1").WithDefaultParameters(Definitions);

        query.Parameters.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["fast"] = 20m, ["threshold"] = 0.1m });
        query.ToQueryString().Should().EndWith("&p.fast=20&p.threshold=0.1");
    }

    [Theory] // PART-4 section 5: decoded comparison, so needless encoding is still canonical (no redirect loop)
    [InlineData(MinimalCanonical)]
    [InlineData("symbol=%41APL&tf=daily&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0")]
    [InlineData("symbol=AAPL&tf=daily&strategy=sma%2Dcrossover&capital=10000&fee=0%2E1&slip=0&rf=0")]
    [InlineData("?symbol=AAPL&tf=daily&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0")]
    public void IsCanonical_ComparesDecodedPairs(string received)
    {
        var query = Parse(MinimalCanonical);

        query.IsCanonical(new QueryString(received.StartsWith('?') ? received : "?" + received)).Should().BeTrue();
    }

    [Theory] // PART-4 section 5: any other name, value, order or extra pair is not canonical
    [InlineData("symbol=aapl&tf=daily&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0")]
    [InlineData("tf=daily&symbol=AAPL&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0")]
    [InlineData("symbol=AAPL&tf=daily&strategy=sma-crossover&capital=10000.00&fee=0.1&slip=0&rf=0")]
    [InlineData("symbol=AAPL&tf=daily&strategy=sma-crossover&from=&capital=10000&fee=0.1&slip=0&rf=0")]
    [InlineData("symbol=AAPL&tf=daily&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0&mc=true")]
    [InlineData("SYMBOL=AAPL&tf=daily&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0")]
    [InlineData("symbol=AAPL&tf=daily&strategy=sma-crossover")]
    public void IsCanonical_RejectsAnyDifference(string received)
    {
        Parse(received).IsCanonical(new QueryString("?" + received)).Should().BeFalse();
    }

    [Fact] // PART-4 section 5: the canonical string of a query is canonical for that query (fixed point, no loop)
    public void CanonicalString_IsCanonical()
    {
        var query = Parse(FullCanonical);

        query.IsCanonical(new QueryString("?" + query.ToQueryString())).Should().BeTrue();
    }

    // ---- Number formatting (correction 3) ----

    [Theory] // PART-4 section 5: plain notation, and Format(Parse(Format(x))) == Format(x)
    [InlineData("0.00001", "0.00001")]
    [InlineData("10000000", "10000000")]
    [InlineData("0", "0")]
    [InlineData("20", "20")]
    [InlineData("-3", "-3")]
    [InlineData("12.50", "12.5")]
    public void FormatNumber_IsPlain_AndIdempotent(string value, string expected)
    {
        var formatted = BacktestQuery.FormatNumber(decimal.Parse(value, CultureInfo.InvariantCulture))!;

        formatted.Should().Be(expected).And.NotContainAny("E", "e");
        var reparsed = Parse("symbol=AAPL&strategy=x&p.v=" + formatted).Parameters["v"];
        BacktestQuery.FormatNumber(reparsed).Should().Be(formatted);
    }

    [Theory] // PART-4 section 5: catalog values (double) never print as 1E-05 or 1E+07
    [InlineData(0.00001, "0.00001")]
    [InlineData(1e7, "10000000")]
    [InlineData(0.005, "0.005")]
    [InlineData(200.0, "200")]
    [InlineData(0.0, "0")]
    public void FormatNumber_OfACatalogValue_IsPlain_AndIdempotent(double value, string expected)
    {
        var formatted = BacktestQuery.FormatNumber(value);

        formatted.Should().Be(expected);
        BacktestQuery.FormatNumber(Parse("symbol=AAPL&strategy=x&p.v=" + formatted).Parameters["v"]).Should().Be(formatted);
    }

    // ---- Duplicates and normalisation order (correction 4, decision C) ----

    [Theory] // correction 4: a key given twice is an error, whatever its case (the query collection ignores case)
    [InlineData("symbol=AAPL&strategy=x&p.fast=10&P.fast=20", "p.fast")]
    [InlineData("symbol=AAPL&strategy=x&p.fast=10&p.fast=10", "p.fast")]
    [InlineData("symbol=AAPL&SYMBOL=MSFT&strategy=x", "symbol")]
    [InlineData("symbol=AAPL&strategy=x&sl=&sl=5", "sl")]
    public void DuplicateKeys_AreAnError(string queryString, string field)
    {
        Errors(queryString).Should().ContainSingle().Which.Field.Should().BeEquivalentTo(field);
    }

    [Fact] // decision C: the symbol is upper-cased before the pattern check, so lower case is valid
    public void Symbol_IsUpperCased_BeforeThePatternCheck()
    {
        Parse("symbol=brk.b&strategy=x").Symbol.Should().Be("BRK.B");
        Errors("symbol=%5EGSPC&strategy=x").Should().ContainSingle().Which.Field.Should().Be("symbol");
    }
}
