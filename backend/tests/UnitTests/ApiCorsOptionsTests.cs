using Api.Cors;
using Microsoft.Extensions.Configuration;

namespace UnitTests;

/// <summary>
/// CORS_ALLOWED_ORIGINS: which browser origins (scheme + host + port of the page) may call the API. It is an exact
/// allow-list, checked at startup, because a mistyped origin would otherwise show up only as the browser silently refusing
/// every response.
/// </summary>
public class ApiCorsOptionsTests
{
    [Fact]
    public void Unset_means_the_local_development_servers()
    {
        // 5173 = `npm run dev` (Vite), 4173 = `npm run preview` (the production build served locally).
        Assert.Equal(["http://localhost:5173", "http://localhost:4173"], ApiCorsOptions.Parse(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_but_blank_means_no_origin_at_all(string value)
    {
        // The way to switch cross-origin access off (for example when the API and the frontend share one origin).
        // It must not silently fall back to the development defaults.
        Assert.Empty(ApiCorsOptions.Parse(value));
    }

    [Fact]
    public void Reads_a_comma_separated_list_and_trims_it()
    {
        var origins = ApiCorsOptions.Parse(" https://school.example ,http://localhost:3000 ");

        Assert.Equal(["https://school.example", "http://localhost:3000"], origins);
    }

    [Fact]
    public void Ignores_empty_items()
    {
        Assert.Equal(["https://a.example", "https://b.example"], ApiCorsOptions.Parse("https://a.example,, ,https://b.example,"));
    }

    [Theory]
    [InlineData("http://localhost:5173/", "http://localhost:5173")]        // trailing slash is a common slip, harmless
    [InlineData("HTTP://LOCALHOST:5173", "http://localhost:5173")]          // browsers send the origin in lower case
    [InlineData("http://example.com:80", "http://example.com")]             // ... and without the scheme's default port
    [InlineData("https://example.com:443", "https://example.com")]
    [InlineData("http://[::1]:5173", "http://[::1]:5173")]
    [InlineData("http://127.0.0.1:5173", "http://127.0.0.1:5173")]
    public void Writes_each_origin_the_way_a_browser_sends_it(string configured, string expected)
    {
        // Matching is an exact string comparison against the browser's Origin header, so the stored form must equal it.
        Assert.Equal([expected], ApiCorsOptions.Parse(configured));
    }

    [Fact]
    public void Collapses_duplicates_after_normalising()
    {
        Assert.Equal(["http://localhost:5173"], ApiCorsOptions.Parse("http://localhost:5173,HTTP://localhost:5173/,http://localhost:5173"));
    }

    [Theory]
    [InlineData("*")]                          // no wildcard: every origin is named on purpose
    [InlineData("null")]                       // the "null" origin is what sandboxed iframes and file:// pages send
    [InlineData("localhost:5173")]             // no scheme (parses as scheme "localhost")
    [InlineData("ftp://example.com")]
    [InlineData("http://example.com/app")]     // an origin has no path
    [InlineData("http://example.com?x=1")]
    [InlineData("http://example.com#top")]
    [InlineData("http://user@example.com")]
    [InlineData("not a url")]
    [InlineData("http://")]
    [InlineData("https://ok.example,*")]       // one bad entry spoils the list
    public void Refuses_a_value_that_is_not_a_plain_http_or_https_origin(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ApiCorsOptions.Parse(value));

        // The message has to be enough to fix the setting: which one, and what was wrong with it.
        Assert.Contains("CORS_ALLOWED_ORIGINS", ex.Message);
        Assert.Contains(value.Split(',').Last().Trim(), ex.Message);
    }

    [Fact]
    public void Reads_the_setting_from_configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CORS_ALLOWED_ORIGINS"] = "https://school.example" })
            .Build();

        Assert.Equal(["https://school.example"], ApiCorsOptions.FromConfiguration(config).AllowedOrigins);
    }

    [Fact]
    public void Uses_the_defaults_when_configuration_has_no_such_setting()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        Assert.Equal(["http://localhost:5173", "http://localhost:4173"], ApiCorsOptions.FromConfiguration(config).AllowedOrigins);
    }
}
