namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>The calls of several doubles in one list, in the order they were made (train/test, P4-X3 order of calls).</summary>
internal sealed class CallLog
{
    public List<string> Calls { get; } = [];

    public void Add(string call) => Calls.Add(call);
}
