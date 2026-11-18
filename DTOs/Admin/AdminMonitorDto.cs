namespace Hopon.Api.DTOs.Admin;

public class AdminMonitorDto
{
    public int TripId { get; set; }
    public string Route { get; set; } = string.Empty;
    public string Bus { get; set; } = string.Empty;
    public string? Driver { get; set; }
    public string Status { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AgeMinutes { get; set; }
    public string? LocationUrl { get; set; }
    public string? NextStop { get; set; }
    public string? FinalEtaSast { get; set; }
}
