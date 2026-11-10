using Microsoft.Extensions.Configuration;

namespace Api.Cors;

/// <summary>
/// Which browser origins (scheme + host + port of the page) may call the API, read from <c>CORS_ALLOWED_ORIGINS</c>.
/// <list type="bullet">
/// <item>Not set: the local frontend dev servers (<see cref="DefaultOrigins"/>).</item>
/// <item>Set but blank: no origin at all, i.e. cross-origin access is off (for example when the API and the frontend share one origin).</item>
/// <item>Otherwise a comma-separated list. It is an exact allow-list: no wildcard, and no path, query or credentials.</item>
/// </list>
/// A bad value stops the application at startup with a message naming the setting. A mistyped origin would otherwise
/// show up only as the browser silently refusing every response, which is very hard to trace back to this setting.
/// </summary>
public sealed class ApiCorsOptions(IReadOnlyList<string> allowedOrigins)
{
    public const string ConfigKey = "CORS_ALLOWED_ORIGINS";

    /// <summary>5173 is <c>npm run dev</c> (Vite), 4173 is <c>npm run preview</c> (the production build served locally).</summary>
    public static readonly IReadOnlyList<string> DefaultOrigins = ["http://localhost:5173", "http://localhost:4173"];

    /// <summary>Each origin in the form a browser sends it in the <c>Origin</c> header, which is what gets compared.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; } = allowedOrigins;

    public static ApiCorsOptions FromConfiguration(IConfiguration configuration) =>
        new(Parse(configuration[ConfigKey]));

    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (raw is null) return DefaultOrigins;

        var origins = new List<string>();
        foreach (var item in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var origin = Normalise(item);
            if (!origins.Contains(origin)) origins.Add(origin);
        }
        return origins;
    }

    private static string Normalise(string item)
    {
        var plainOrigin =
            Uri.TryCreate(item, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.UserInfo.Length == 0
            && uri.AbsolutePath == "/" // a trailing slash is tolerated; any real path is not
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0;

        if (!plainOrigin)
        {
            throw new InvalidOperationException(
                $"{ConfigKey} contains '{item}', which is not an allowed origin. Use a comma-separated list of origins " +
                "made of a scheme, a host and optionally a port, such as http://localhost:5173 " +
                "(no wildcard, no path, no credentials; http or https only).");
        }

        // The authority part lower-cases scheme and host and drops the scheme's default port, which is exactly how a
        // browser writes the Origin header, so the stored value can be compared with it as a plain string.
        return uri!.GetLeftPart(UriPartial.Authority);
    }
}
