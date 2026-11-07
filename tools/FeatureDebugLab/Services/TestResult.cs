namespace FeatureDebugLab.Services;

public enum TestStatus
{
    Pass,
    Fail,
    Skip,
    Warn
}

/// <summary>
/// One lab assertion. Failures should include Expected/Actual + FixHint so an agent (or human)
/// can open the right file and repair the product.
/// </summary>
public sealed record TestResult(
    string Suite,
    string Name,
    TestStatus Status,
    string Detail = "",
    string? Expected = null,
    string? Actual = null,
    string? FixHint = null,
    string? Category = null)
{
    public bool IsActionableBug =>
        Status is TestStatus.Fail or TestStatus.Warn &&
        !string.IsNullOrWhiteSpace(FixHint);

    public string StatusTag => Status switch
    {
        TestStatus.Pass => "PASS",
        TestStatus.Fail => "FAIL",
        TestStatus.Warn => "WARN",
        _ => "SKIP"
    };

    public string ToAgentLine()
    {
        var parts = new List<string> { $"[{StatusTag}] {Suite} / {Name}" };
        if (!string.IsNullOrWhiteSpace(Detail))
            parts.Add(Detail);
        if (!string.IsNullOrWhiteSpace(Expected))
            parts.Add("expected=" + Expected);
        if (!string.IsNullOrWhiteSpace(Actual))
            parts.Add("actual=" + Actual);
        if (!string.IsNullOrWhiteSpace(FixHint))
            parts.Add("fix → " + FixHint);
        return string.Join(" | ", parts);
    }
}
