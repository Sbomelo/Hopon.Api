namespace Hopon.Api.Services;

public enum LocationOutcome { Recorded, Throttled, Rejected }
public record LocationRecordResult(LocationOutcome Outcome, string? Error = null);
