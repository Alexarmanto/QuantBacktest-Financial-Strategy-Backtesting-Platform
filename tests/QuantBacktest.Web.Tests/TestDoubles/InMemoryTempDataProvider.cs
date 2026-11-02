using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// TempData kept in memory between two requests, like the cookie provider of the app: the controller of the
/// redirect target can read what the previous one saved, without a test server.
/// </summary>
internal sealed class InMemoryTempDataProvider : ITempDataProvider
{
    private IDictionary<string, object> _values = new Dictionary<string, object>();

    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(_values);

    public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
        _values = new Dictionary<string, object>(values);
}
