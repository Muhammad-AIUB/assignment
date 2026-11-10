using System.Net;
using System.Text.Json;
using Api.Dtos;
using Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// CORS, driven through the real pipeline. The frontend runs on its own origin (the Vite dev server) and calls this API
/// from the browser, which only lets the page read a response if the API says so with Access-Control-* headers. The tests
/// play the browser's part: the preflight it sends before a "non-simple" request, then the request itself.
/// </summary>
[Collection("api")]
public class CorsTests(ApiFactory factory)
{
    // Written out here on purpose, not read from the production constants, so the tests state what the frontend needs.
    private const string FrontendOrigin = "http://localhost:5173";
    private const string StrangerOrigin = "https://evil.example";

    // ---------- harness ----------

    private static HttpRequestMessage Preflight(string method, string path, string requestHeaders, string origin = FrontendOrigin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        if (requestHeaders.Length > 0) request.Headers.Add("Access-Control-Request-Headers", requestHeaders);
        return request;
    }

    private static HttpRequestMessage FromOrigin(HttpMethod method, string path, string? origin = FrontendOrigin)
    {
        var request = new HttpRequestMessage(method, path);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return request;
    }

    private static string[] Values(HttpResponseMessage res, string name) =>
        res.Headers.TryGetValues(name, out var v)
            ? v.SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToArray()
            : [];

    private static string[] LowerValues(HttpResponseMessage res, string name) =>
        Values(res, name).Select(v => v.ToLowerInvariant()).ToArray();

    private static async Task<JsonElement> StandardError(HttpResponseMessage res, string code)
    {
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(code, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        return body;
    }

    private sealed class ThrowingClassService : IClassService
    {
        public Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClassSummaryResponse>> ListAsync(CancellationToken ct) =>
            throw new InvalidOperationException("SECRET-INTERNAL-DETAIL");

        public Task<ClassSummaryResponse?> GetAsync(string classId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    // ---------- the preflight ----------

    // The frontend's axios instance adds X-Student-Id to every request once the student has entered an id (not only to the
    // four endpoints that require it), so every endpoint, GETs included, is preceded by a preflight. Hence every row.
    [Theory]
    [InlineData("POST", "/classes", "content-type")]
    [InlineData("POST", "/classes", "content-type,x-student-id")]
    [InlineData("GET", "/classes", "x-student-id")]
    [InlineData("GET", "/classes/c_1", "x-student-id")]
    [InlineData("POST", "/classes/c_1/holds", "x-student-id")]
    [InlineData("POST", "/classes/c_1/holds", "content-type,x-student-id")]
    [InlineData("GET", "/holds/h_1", "x-student-id")]
    [InlineData("POST", "/holds/h_1/confirm", "x-student-id")]
    [InlineData("DELETE", "/holds/h_1", "x-student-id")]
    [InlineData("GET", "/me/holds", "x-student-id")]
    public async Task A_preflight_from_the_frontend_is_approved_for_each_call_the_frontend_makes(
        string method, string path, string requestHeaders)
    {
        var res = await factory.CreateClient().SendAsync(Preflight(method, path, requestHeaders));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal([FrontendOrigin], Values(res, "Access-Control-Allow-Origin"));
        Assert.Contains(method, Values(res, "Access-Control-Allow-Methods"));
        foreach (var header in requestHeaders.Split(','))
            Assert.Contains(header, LowerValues(res, "Access-Control-Allow-Headers"));
        Assert.Empty(await res.Content.ReadAsStringAsync()); // answered by the CORS layer, no controller, no database
    }

    [Fact]
    public async Task A_preflight_is_cached_by_the_browser_so_polling_does_not_repeat_it_every_time()
    {
        var res = await factory.CreateClient().SendAsync(Preflight("GET", "/me/holds", "x-student-id"));

        var maxAge = Assert.Single(Values(res, "Access-Control-Max-Age"));
        Assert.InRange(int.Parse(maxAge), 60, 600); // browsers cap this anyway (Chromium at 7200 s, older ones at 600 s)
    }

    [Fact]
    public async Task A_preflight_from_an_origin_that_is_not_allowed_is_not_approved()
    {
        var res = await factory.CreateClient().SendAsync(Preflight("GET", "/me/holds", "x-student-id", StrangerOrigin));

        Assert.Empty(Values(res, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_preflight_for_a_method_the_api_does_not_offer_is_not_approved()
    {
        var res = await factory.CreateClient().SendAsync(Preflight("PUT", "/classes", "content-type"));

        Assert.DoesNotContain("PUT", Values(res, "Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task A_preflight_asking_for_a_header_the_api_does_not_use_is_not_approved()
    {
        var res = await factory.CreateClient().SendAsync(Preflight("GET", "/me/holds", "x-student-id,x-evil"));

        Assert.DoesNotContain("x-evil", LowerValues(res, "Access-Control-Allow-Headers"));
    }

    // ---------- the request itself ----------

    [Fact]
    public async Task A_response_to_the_frontend_is_readable_by_it_and_exposes_the_correlation_id()
    {
        var res = await factory.CreateClient().SendAsync(FromOrigin(HttpMethod.Get, "/classes"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal([FrontendOrigin], Values(res, "Access-Control-Allow-Origin"));
        Assert.Contains("Origin", Values(res, "Vary")); // the answer depends on who asks, so caches must key on it
        // Without this a page cannot read X-Correlation-Id, which the UNEXPECTED_ERROR message tells people to quote.
        Assert.Contains("X-Correlation-Id", Values(res, "Access-Control-Expose-Headers"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_response_to_an_origin_that_is_not_allowed_is_not_readable_by_it()
    {
        var res = await factory.CreateClient().SendAsync(FromOrigin(HttpMethod.Get, "/classes", StrangerOrigin));

        Assert.Empty(Values(res, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_request_with_no_origin_gets_no_cors_headers()
    {
        // curl, server-to-server calls and same-origin pages send no Origin header; nothing about them changes.
        var res = await factory.CreateClient().SendAsync(FromOrigin(HttpMethod.Get, "/classes", origin: null));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Empty(Values(res, "Access-Control-Allow-Origin"));
    }

    // ---------- errors must be readable too ----------
    // The frontend shows the `message` of every 4xx. If an error response lacked the allow-origin header, the browser would
    // hide the body and the page would only see a failed network request.

    [Fact]
    public async Task A_business_rule_4xx_is_readable_by_the_frontend()
    {
        var res = await factory.CreateClient().SendAsync(FromOrigin(HttpMethod.Post, "/classes/c_nope/holds"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal([FrontendOrigin], Values(res, "Access-Control-Allow-Origin"));
        await StandardError(res, "INVALID_STUDENT_ID");
    }

    [Theory]
    [InlineData("GET", "/no/such/route", HttpStatusCode.NotFound, "ROUTE_NOT_FOUND")]
    [InlineData("PATCH", "/classes", HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED")]
    public async Task A_framework_generated_4xx_is_readable_by_the_frontend(
        string method, string path, HttpStatusCode status, string code)
    {
        var res = await factory.CreateClient().SendAsync(FromOrigin(new HttpMethod(method), path));

        Assert.Equal(status, res.StatusCode);
        Assert.Equal([FrontendOrigin], Values(res, "Access-Control-Allow-Origin"));
        await StandardError(res, code);
    }

    [Fact]
    public async Task An_unexpected_error_is_readable_by_the_frontend_and_still_carries_the_correlation_id()
    {
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IClassService>();
            s.AddScoped<IClassService>(_ => new ThrowingClassService());
        }));

        var res = await app.CreateClient().SendAsync(FromOrigin(HttpMethod.Get, "/classes"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await StandardError(res, "UNEXPECTED_ERROR");
        Assert.Equal([FrontendOrigin], Values(res, "Access-Control-Allow-Origin"));
        Assert.Contains("Origin", Values(res, "Vary"));
        Assert.Single(Values(res, "X-Correlation-Id")); // the handler's clean-up must not cost us this header either
    }

    // ---------- configuration: CORS_ALLOWED_ORIGINS ----------

    private WebApplicationFactory<Program> HostWithCorsSetting(string value) =>
        factory.WithWebHostBuilder(b => b.UseSetting("CORS_ALLOWED_ORIGINS", value));

    private static string AllMessages(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException!) messages.Add(e.Message);
        return string.Join(" | ", messages);
    }

    [Fact]
    public async Task The_setting_replaces_the_default_origins()
    {
        await using var app = HostWithCorsSetting(" https://school.example ,http://localhost:3000");
        var client = app.CreateClient();

        var school = await client.SendAsync(Preflight("GET", "/classes", "x-student-id", "https://school.example"));
        var port3000 = await client.SendAsync(Preflight("GET", "/classes", "x-student-id", "http://localhost:3000"));
        var vite = await client.SendAsync(Preflight("GET", "/classes", "x-student-id", FrontendOrigin));

        Assert.Equal(["https://school.example"], Values(school, "Access-Control-Allow-Origin"));
        Assert.Equal(["http://localhost:3000"], Values(port3000, "Access-Control-Allow-Origin"));
        // Once the setting exists the built-in development origins no longer apply. (The default-config tests above show
        // that FrontendOrigin IS approved when the setting is absent, so this is not passing for lack of any CORS at all.)
        Assert.Empty(Values(vite, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_blank_setting_turns_cross_origin_access_off_but_leaves_the_api_working()
    {
        await using var app = HostWithCorsSetting("");
        var client = app.CreateClient();

        var preflight = await client.SendAsync(Preflight("GET", "/classes", "x-student-id"));
        var fromFrontend = await client.SendAsync(FromOrigin(HttpMethod.Get, "/classes"));
        var noOrigin = await client.SendAsync(FromOrigin(HttpMethod.Get, "/classes", origin: null));

        Assert.Empty(Values(preflight, "Access-Control-Allow-Origin"));
        Assert.Empty(Values(fromFrontend, "Access-Control-Allow-Origin")); // served, but a browser would refuse to hand it over
        Assert.Equal(HttpStatusCode.OK, noOrigin.StatusCode);
    }

    [Fact]
    public void A_bad_setting_stops_the_application_from_starting_and_names_the_setting()
    {
        using var app = HostWithCorsSetting("*");

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        var text = AllMessages(ex);
        Assert.Contains("CORS_ALLOWED_ORIGINS", text);
        Assert.Contains("'*'", text);
    }
}
