using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Updates;

/// <summary>Public feed at Storage <c>app-updates/latest.json</c>.</summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>If current app is older than this, toast that a full Setup reinstall may be required.</summary>
    [JsonPropertyName("minVersion")]
    public string? MinVersion { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    /// <summary>Optional zip size in bytes (fallback when Content-Length is missing).</summary>
    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("releasedAt")]
    public string? ReleasedAt { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public enum UpdateProgressPhase
{
    Downloading,
    Verifying,
    Extracting,
    Applying
}

public sealed class UpdateDownloadProgress
{
    public UpdateProgressPhase Phase { get; init; }
    public string Message { get; init; } = "";
    public long BytesReceived { get; init; }
    public long? TotalBytes { get; init; }

    /// <summary>0–1 when total known; otherwise null (indeterminate).</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp(BytesReceived / (double)TotalBytes.Value, 0, 1) : null;
}

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    RequiresFullSetup,
    Error
}

public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public string Message { get; init; } = "";
    public UpdateManifest? Manifest { get; init; }
    public Version? CurrentVersion { get; init; }
    public Version? RemoteVersion { get; init; }
}
