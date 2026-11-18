namespace Hopon.Api.Services;

public sealed class RetentionOptions
{
    public bool Enabled { get; set; }
    public int DaysAfterTrip { get; set; } = 90;
    public int LocationDays { get; set; } = 30;
    public bool DryRun { get; set; } = true;
    public bool RunOnStartup { get; set; }
}

public record RetentionSummary(DateTime RanAtUtc, bool DryRun, bool Enabled, int Locations = 0,
    int Boardings = 0, int Notifications = 0, int Audits = 0, int Otps = 0, int Users = 0, int Contacts = 0);
