using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>Writes every sweep to the log, then behaves as <see cref="RecordingSweepService"/> (scripted failure or result, or the inner service).</summary>
internal sealed class LoggingSweepService(CallLog log, RecordingSweepService inner) : IParameterSweepService
{
    public RecordingSweepService Inner => inner;

    public List<SweepRequest> Requests => inner.Requests;

    public Task<SweepResult> RunAsync(SweepRequest request, CancellationToken cancellationToken = default)
    {
        log.Add($"Sweep {request.BaseRequest.From:yyyy-MM-dd}..{request.BaseRequest.To:yyyy-MM-dd}");
        return inner.RunAsync(request, cancellationToken);
    }
}
