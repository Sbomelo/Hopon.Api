namespace Hopon.Api.Services;

public static class GeoMath
{
    public static double HaversineKilometers(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusKilometers = 6371;
        var latitudeDelta = ToRadians(latitude2 - latitude1);
        var longitudeDelta = ToRadians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + Math.Cos(ToRadians(latitude1)) * Math.Cos(ToRadians(latitude2))
            * Math.Pow(Math.Sin(longitudeDelta / 2), 2);

        return earthRadiusKilometers * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
