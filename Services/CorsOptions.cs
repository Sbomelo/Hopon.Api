namespace Hopon.Api.Services;

public class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    public bool HasValidOrigins() => AllowedOrigins is not null && AllowedOrigins.All(origin =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
}
