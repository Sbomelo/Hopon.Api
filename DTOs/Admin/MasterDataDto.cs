using System.ComponentModel.DataAnnotations;

namespace Hopon.Api.DTOs.Admin;

public class BusInput
{
    [Required, StringLength(30)] public string RegistrationNumber { get; set; } = string.Empty;
    [StringLength(100)] public string? Model { get; set; }
    [Required, Range(1, int.MaxValue)] public int? Capacity { get; set; }
}

public class AdminBusDto : BusInput { public int Id { get; set; } }

public class StopInput
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, Range(-90, 90)] public double? Latitude { get; set; }
    [Required, Range(-180, 180)] public double? Longitude { get; set; }
}

public class AdminStopDto : StopInput
{
    public int Id { get; set; }
    public string LocationUrl => FormattableString.Invariant($"https://www.openstreetmap.org/?mlat={Latitude}&mlon={Longitude}#map=15/{Latitude}/{Longitude}");
}

public class RouteInput
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
}

public class AdminRouteDto : RouteInput
{
    public int Id { get; set; }
    public bool UsedByTrips { get; set; }
    public List<AdminRouteStopDto> Stops { get; set; } = new();
}

public class AdminRouteStopDto
{
    public int Id { get; set; }
    public int StopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SequenceOrder { get; set; }
}

public class AddRouteStopInput { [Range(1, int.MaxValue)] public int StopId { get; set; } }
public class ChangeRouteStopInput { [Range(1, int.MaxValue)] public int RouteStopId { get; set; } }
public enum RouteStopAction { Add, Remove, MoveUp, MoveDown }
public record AdminSaveResult(bool Success, string? Error = null, int Id = 0);
