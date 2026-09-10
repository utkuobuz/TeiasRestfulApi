namespace TEİASRestfulApi;

public enum YtbsIssueLevel
{
    Fail,
    Warn
}

public enum YtbsChannel
{
    Anlik,
    Saatlik,
    Sistem
}

public enum YtbsIssueKind
{
    PackageRejected,
    OverLimitSkip,
    NoSample,
    LoginFailed
}

public sealed class YtbsIssue
{
    public YtbsIssueLevel Level { get; init; }
    public YtbsChannel Channel { get; init; }
    public YtbsIssueKind Kind { get; init; }
    public DateTime Slot { get; init; }
    public string? LicenseNo { get; init; }
    public int? PlantId { get; init; }
    public string? PlantName { get; init; }
    public string Reason { get; init; } = "";
    public double? Value { get; init; }
    public decimal? Limit { get; init; }
    public IReadOnlyList<PlantRef> RelatedPlants { get; init; } = [];
}

public readonly record struct PlantRef(int Id, string Name);
