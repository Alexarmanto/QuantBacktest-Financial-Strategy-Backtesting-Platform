namespace QuantBacktest.Web.Models;

/// <summary>Generic error page (P4-U12): only an identifier to find the details in the logs, never a stack trace.</summary>
internal sealed record ErrorViewModel(string RequestId);
