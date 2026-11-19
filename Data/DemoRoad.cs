using System.Text.Json;
using Hopon.Api.Services;

namespace Hopon.Api.Data;

public readonly record struct RoadPoint(double Longitude, double Latitude);

public sealed class DemoRoad
{
    public const string RouteName = "Durban - Johannesburg";
    // Road-access points returned by OSRM for the operator's five boarding pins.
    public static readonly (string Name, RoadPoint Point, int Arrival, int? Departure)[] Stops =
    [
        ("Durban", new(31.024345, -29.846779), 0, 0),
        ("Pietermaritzburg", new(30.382027, -29.604659), 90, 100),
        ("Estcourt", new(29.888933, -29.049837), 195, 205),
        ("Harrismith", new(29.123982, -28.275420), 330, 360),
        ("Johannesburg Park Station", new(28.041673, -26.197113), 570, null)
    ];
    public static readonly RoadPoint Villiers = new(28.618667, -27.031420);
    public static DemoRoad N3 { get; } = Load();

    public double[][] Coordinates { get; }
    private readonly double[] _kilometres;
    public double Length => _kilometres[^1];

    private static DemoRoad Load()
    {
        using var stream = typeof(DemoRoad).Assembly.GetManifestResourceStream("Hopon.N3Route")!;
        using var json = JsonDocument.Parse(stream);
        return new DemoRoad(json.RootElement.GetProperty("coordinates").Deserialize<double[][]>()!);
    }

    private DemoRoad(double[][] coordinates)
    {
        Coordinates = coordinates;
        _kilometres = new double[coordinates.Length];
        for (var i = 1; i < coordinates.Length; i++)
        {
            var a = coordinates[i - 1];
            var b = coordinates[i];
            _kilometres[i] = _kilometres[i - 1] + GeoMath.HaversineKilometers(a[1], a[0], b[1], b[0]);
        }
    }

    // Project onto a segment, so repeated small moves accumulate between vertices.
    public double DistanceAlong(RoadPoint point)
    {
        var scale = Math.Cos(point.Latitude * Math.PI / 180);
        var closest = double.PositiveInfinity;
        double distance = 0;
        for (var i = 1; i < Coordinates.Length; i++)
        {
            var a = Coordinates[i - 1];
            var b = Coordinates[i];
            var dx = (b[0] - a[0]) * scale;
            var dy = b[1] - a[1];
            var x = (point.Longitude - a[0]) * scale;
            var y = point.Latitude - a[1];
            var squaredLength = dx * dx + dy * dy;
            if (squaredLength == 0) continue;
            var t = Math.Clamp((x * dx + y * dy) / squaredLength, 0, 1);
            var error = Math.Pow(x - t * dx, 2) + Math.Pow(y - t * dy, 2);
            if (error >= closest) continue;
            closest = error;
            distance = _kilometres[i - 1] + t * (_kilometres[i] - _kilometres[i - 1]);
        }
        return distance;
    }

    public RoadPoint At(double kilometres)
    {
        kilometres = Math.Clamp(kilometres, 0, Length);
        var index = Array.BinarySearch(_kilometres, kilometres);
        if (index >= 0) return new(Coordinates[index][0], Coordinates[index][1]);
        index = ~index;
        var fraction = (kilometres - _kilometres[index - 1]) / (_kilometres[index] - _kilometres[index - 1]);
        var a = Coordinates[index - 1];
        var b = Coordinates[index];
        return new(a[0] + fraction * (b[0] - a[0]), a[1] + fraction * (b[1] - a[1]));
    }

    public static bool Matches(string name, IEnumerable<string> orderedStops) =>
        name == RouteName && orderedStops.SequenceEqual(Stops.Select(s => s.Name));
}
