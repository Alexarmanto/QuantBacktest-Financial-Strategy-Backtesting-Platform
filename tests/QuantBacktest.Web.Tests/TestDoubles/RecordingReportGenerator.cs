using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// Records every call, then answers with the fake (or throws the given exception). The web only serves these bytes:
/// the tests compare the response with what the generator returned.
/// </summary>
internal sealed class RecordingReportGenerator(Exception? toThrow = null) : IReportGenerator
{
    private readonly FakeReportGenerator _inner = new();

    public List<(BacktestResult Result, MonteCarloResult? MonteCarlo, byte[] Pdf)> PdfCalls { get; } = [];

    public List<(BacktestResult Result, string Csv)> CsvCalls { get; } = [];

    public byte[] GeneratePdf(BacktestResult result, MonteCarloResult? monteCarlo = null)
    {
        if (toThrow is not null)
        {
            PdfCalls.Add((result, monteCarlo, []));
            throw toThrow;
        }

        var pdf = _inner.GeneratePdf(result, monteCarlo);
        PdfCalls.Add((result, monteCarlo, pdf));
        return pdf;
    }

    public string GenerateTradesCsv(BacktestResult result)
    {
        if (toThrow is not null)
        {
            CsvCalls.Add((result, string.Empty));
            throw toThrow;
        }

        var csv = _inner.GenerateTradesCsv(result);
        CsvCalls.Add((result, csv));
        return csv;
    }
}
