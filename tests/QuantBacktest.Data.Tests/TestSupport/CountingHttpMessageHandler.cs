using System.Net;
using System.Text;

namespace QuantBacktest.Data.Tests.TestSupport;

/// <summary>Fake HTTP endpoint: never touches the network, counts calls and records the requested URIs.</summary>
internal sealed class CountingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private int _callCount;

    public int CallCount => _callCount;

    public List<Uri> RequestedUris { get; } = [];

    public static CountingHttpMessageHandler Returning(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/csv"),
        }));

    public static CountingHttpMessageHandler Throwing(Exception exception)
        => new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        lock (RequestedUris)
        {
            RequestedUris.Add(request.RequestUri!);
        }

        return respond(request, cancellationToken);
    }
}
