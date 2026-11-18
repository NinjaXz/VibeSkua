namespace Skua.Core.Models.Servers;

/// <summary>
/// Describes a server's measured latency relative to the user's configured warning threshold.
/// </summary>
public enum ServerLatencyStatus
{
    Unknown,
    Good,
    NearThreshold,
    High,
    Unavailable
}

/// <summary>
/// Shared classification rules for AQW server latency measurements.
/// </summary>
public static class ServerLatencyClassifier
{
    public const long FailedPing = 9999;
    public const int DefaultThresholdMs = 200;
    public const int DefaultNearThresholdPercent = 75;

    /// <summary>
    /// Classifies a measured latency using the configured warning threshold and near-threshold percentage.
    /// </summary>
    public static ServerLatencyStatus Classify(long pingMs, int thresholdMs = DefaultThresholdMs, int nearThresholdPercent = DefaultNearThresholdPercent)
    {
        if (pingMs < 0)
            return ServerLatencyStatus.Unknown;

        if (pingMs >= FailedPing)
            return ServerLatencyStatus.Unavailable;

        int safeThreshold = Math.Max(1, thresholdMs);
        int safeNearPercent = Math.Clamp(nearThresholdPercent, 1, 99);
        long nearThreshold = (long)Math.Ceiling(safeThreshold * (safeNearPercent / 100d));

        if (pingMs >= safeThreshold)
            return ServerLatencyStatus.High;

        return pingMs >= nearThreshold
            ? ServerLatencyStatus.NearThreshold
            : ServerLatencyStatus.Good;
    }
}
