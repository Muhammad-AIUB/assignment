using Api.Middleware;
using Api.Swagger;

namespace Api.Cors;

/// <summary>
/// The CORS policy. The frontend is a page served from its own origin and calls this API from the browser, which only lets
/// the page read a response if the API grants it with Access-Control-* headers.
/// </summary>
public static class CorsSetup
{
    /// <summary>
    /// One default policy for every endpoint. Only what the frontend actually uses is allowed, nothing broader:
    /// <list type="bullet">
    /// <item>origins: the allow-list, exactly (see <see cref="ApiCorsOptions"/>);</item>
    /// <item>methods: the three the contract uses (GET, POST, DELETE);</item>
    /// <item>request headers: <c>Content-Type</c> (JSON body of POST /classes) and <c>X-Student-Id</c>;</item>
    /// <item>exposed response header: <c>X-Correlation-Id</c>, which the UNEXPECTED_ERROR message tells people to quote
    /// and which a page cannot read otherwise;</item>
    /// <item>preflight cache: 10 minutes, so polling does not pay for a preflight round-trip on every refresh. Browsers
    /// apply caps of their own, so this is an upper bound, not a promise.</item>
    /// </list>
    /// No credentials: identity is the <c>X-Student-Id</c> header, not a cookie, so cookies are never involved.
    /// </summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services, IReadOnlyList<string> allowedOrigins)
    {
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins.ToArray())
            .WithMethods("GET", "POST", "DELETE")
            .WithHeaders("Content-Type", SwaggerSetup.StudentIdHeader)
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
        return services;
    }
}
