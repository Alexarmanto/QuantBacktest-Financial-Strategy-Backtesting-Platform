namespace QuantBacktest.Data.Options;

/// <summary>
/// Configuration section "QuantBacktest" (docs/CONTRACT.md section 13, P1-D1).
/// Relative paths are resolved from the content root with <see cref="ResolvePath"/>.
/// </summary>
internal sealed class DataOptions
{
    public const string SectionName = "QuantBacktest";

    public string DatabasePath { get; set; } = "App_Data/quantbacktest.db";

    public string SeedDirectory { get; set; } = "../../data/seed";

    /// <summary>P1-D1: a relative path is resolved from <paramref name="contentRootPath"/>; an absolute path is kept as is.</summary>
    public static string ResolvePath(string contentRootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(contentRootPath, path));
    }
}
