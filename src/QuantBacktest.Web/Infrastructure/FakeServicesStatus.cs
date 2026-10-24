namespace QuantBacktest.Web.Infrastructure;

/// <summary>
/// Reads the QuantBacktest:UseFakes:* keys once. Drives the service registration in Program.cs
/// and the yellow "Demo data" banner of the layout (P4-U13).
/// </summary>
internal sealed class FakeServicesStatus
{
    /// <summary>Part names in the order of docs/CONTRACT.md section 2; also the configuration key suffixes.</summary>
    public static readonly IReadOnlyList<string> Parts = ["Data", "Reports", "Strategies", "Analytics", "Engine"];

    public FakeServicesStatus(IConfiguration configuration)
    {
        ActiveParts = Parts
            .Where(part => configuration.GetValue<bool>($"QuantBacktest:UseFakes:{part}"))
            .ToArray();
    }

    /// <summary>Parts currently served by fakes, in contract order.</summary>
    public IReadOnlyList<string> ActiveParts { get; }

    public bool IsAnyActive => ActiveParts.Count > 0;

    public string BannerText => $"Demo data: fake services active ({string.Join(", ", ActiveParts)})";

    public bool IsFake(string part) => ActiveParts.Contains(part);
}
