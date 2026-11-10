namespace Infrastructure.Options;

public class HoldExpirySweepOptions
{
    public const string EnabledConfigKey = "HOLD_EXPIRY_SWEEP_ENABLED";
    public const string IntervalConfigKey = "HOLD_EXPIRY_SWEEP_INTERVAL_SECONDS";
    public const int DefaultIntervalSeconds = 5;

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
