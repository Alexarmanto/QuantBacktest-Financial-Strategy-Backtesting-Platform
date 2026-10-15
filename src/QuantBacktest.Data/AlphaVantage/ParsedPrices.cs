using QuantBacktest.Core.Models;

namespace QuantBacktest.Data.AlphaVantage;

/// <summary>Bars read from one Alpha Vantage CSV document, sorted by date ascending, without duplicates.</summary>
internal sealed record ParsedPrices(IReadOnlyList<PriceBar> Bars, bool IsAdjusted);
