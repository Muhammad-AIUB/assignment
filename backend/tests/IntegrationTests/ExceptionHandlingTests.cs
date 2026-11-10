using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Api.Dtos;
using Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The global exception handler, driven through the real pipeline (Development environment, so the framework's
/// own developer exception page would otherwise print a stack trace). The thing that throws is a test double
/// for <see cref="IClassService"/>; everything between it and the HTTP client is the real application.
/// </summary>
[Collection("api")]
public class ExceptionHandlingTests(ApiFactory factory)
{
    private const string Secret = "SECRET-INTERNAL-DETAIL select * from classes";

    // ---------- harness ----------

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private sealed class ThrowingClassService(Func<Task> behaviour) : IClassService
    {
        public Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<ClassSummaryResponse>> ListAsync(CancellationToken ct)
        {
            await behaviour();
            return [];
        }

        public Task<ClassSummaryResponse?> GetAsync(string classId, CancellationToken ct) =>
            Task.FromResult<ClassSummaryResponse?>(null); // "unknown class", so the controller answers 404
    }

    private WebApplicationFactory<Program> HostWhere(Func<Task> listBehaviour, CapturingSink sink) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<ILogEventSink>(sink);
            s.AddHttpContextAccessor();
            s.RemoveAll<IClassService>();
            s.AddScoped<IClassService>(_ => new ThrowingClassService(listBehaviour));
        }));

    private WebApplicationFactory<Program> HostThrowing(Func<Exception> make, CapturingSink sink) =>
        HostWhere(() => throw make(), sink);

    private static PostgresException Pg(string sqlState) => new(Secret, "ERROR", "ERROR", sqlState);

    private static string CorrelationId(HttpResponseMessage res) =>
        Assert.Single(res.Headers.GetValues("X-Correlation-Id"));

    private static object? Prop(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var v) && v is ScalarValue s ? s.Value : null;

    private static async Task<JsonElement> AssertStandardError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.StartsWith("application/json", res.Content.Headers.ContentType?.ToString());
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["error", "message"], body.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(code, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        return body;
    }

    // ---------- the response ----------

    [Fact]
    public async Task An_unexpected_exception_becomes_a_4xx_in_the_standard_shape_with_no_internal_detail()
    {
        await using var app = HostThrowing(() => new InvalidOperationException(Secret), new CapturingSink());

        var res = await app.CreateClient().GetAsync("/classes");

        var body = await AssertStandardError(res, HttpStatusCode.BadRequest, "UNEXPECTED_ERROR");
        var raw = body.GetRawText();
        Assert.DoesNotContain("SECRET", raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("System.", raw);
        Assert.DoesNotContain(" at ", raw); // no stack frames
    }

    [Fact]
    public async Task The_unexpected_error_message_quotes_the_correlation_id_so_support_can_find_the_log()
    {
        await using var app = HostThrowing(() => new InvalidOperationException(Secret), new CapturingSink());

        var res = await app.CreateClient().GetAsync("/classes");

        var id = CorrelationId(res);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(id, body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("deadlock", HttpStatusCode.Conflict, "CONFLICT")]
    [InlineData("serialization", HttpStatusCode.Conflict, "CONFLICT")]
    [InlineData("unique", HttpStatusCode.Conflict, "CONFLICT")]
    [InlineData("nul-byte", HttpStatusCode.BadRequest, "INVALID_INPUT")]
    [InlineData("too-large", HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE")]
    [InlineData("timeout", HttpStatusCode.RequestTimeout, "REQUEST_TIMEOUT")]
    [InlineData("db-down", HttpStatusCode.BadRequest, "UNEXPECTED_ERROR")]
    public async Task Known_failure_kinds_map_to_a_specific_4xx(string kind, HttpStatusCode status, string code)
    {
        Exception Make() => kind switch
        {
            "deadlock" => Pg("40P01"),
            "serialization" => Pg("40001"),
            "unique" => new Microsoft.EntityFrameworkCore.DbUpdateException("save failed", Pg("23505")),
            "nul-byte" => Pg("22021"),
            "too-large" => new BadHttpRequestException(Secret, 413),
            "timeout" => new BadHttpRequestException(Secret, 408),
            "db-down" => new NpgsqlException(Secret, new IOException(Secret)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await using var app = HostThrowing(Make, new CapturingSink());

        var res = await app.CreateClient().GetAsync("/classes");

        var body = await AssertStandardError(res, status, code);
        Assert.DoesNotContain("SECRET", body.GetRawText());
    }

    // ---------- logging and the correlation id ----------

    [Fact]
    public async Task The_real_exception_is_logged_through_serilog_with_the_response_correlation_id()
    {
        var sink = new CapturingSink();
        await using var app = HostThrowing(() => new InvalidOperationException(Secret), sink);

        var res = await app.CreateClient().GetAsync("/classes");

        var logged = Assert.Single(sink.Events, e => e.Exception is InvalidOperationException);
        Assert.Equal(LogEventLevel.Error, logged.Level);
        Assert.Contains(Secret, logged.Exception!.Message); // the full detail is in the log, not in the response
        Assert.Equal(CorrelationId(res), Prop(logged, "CorrelationId"));
        Assert.Equal("GET", Prop(logged, "Method"));
        Assert.Equal("/classes", Prop(logged, "Path"));
    }

    [Fact]
    public async Task A_caller_supplied_correlation_id_is_used_for_the_response_and_the_log()
    {
        var sink = new CapturingSink();
        await using var app = HostThrowing(() => new InvalidOperationException(Secret), sink);
        var req = new HttpRequestMessage(HttpMethod.Get, "/classes");
        req.Headers.Add("X-Correlation-Id", "client-trace-7f3a");

        var res = await app.CreateClient().SendAsync(req);

        Assert.Equal("client-trace-7f3a", CorrelationId(res));
        var logged = Assert.Single(sink.Events, e => e.Exception is InvalidOperationException);
        Assert.Equal("client-trace-7f3a", Prop(logged, "CorrelationId"));
    }

    [Theory]
    [InlineData("has spaces and !")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")] // 70 chars
    public async Task An_unsafe_caller_supplied_correlation_id_is_replaced(string supplied)
    {
        await using var app = HostThrowing(() => new InvalidOperationException(Secret), new CapturingSink());
        var req = new HttpRequestMessage(HttpMethod.Get, "/classes");
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", supplied);

        var res = await app.CreateClient().SendAsync(req);

        var id = CorrelationId(res);
        Assert.NotEqual(supplied, id);
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", id);
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id_and_each_request_gets_its_own()
    {
        var client = factory.CreateClient();

        var ok = await client.GetAsync("/classes");
        var notFound = await client.GetAsync("/classes/c_does_not_exist");
        var unknownRoute = await client.GetAsync("/no/such/route");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        var ids = new[] { CorrelationId(ok), CorrelationId(notFound), CorrelationId(unknownRoute) };
        Assert.Equal(3, ids.Distinct().Count());
    }

    // ---------- edge cases of the handler itself ----------

    [Fact]
    public async Task A_client_that_gives_up_is_logged_quietly_and_is_not_an_error()
    {
        var sink = new CapturingSink();
        // The double must wait on the *request's* abort token, so it reaches it through the accessor.
        await using var app2 = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<ILogEventSink>(sink);
            s.AddHttpContextAccessor();
            s.RemoveAll<IClassService>();
            s.AddScoped<IClassService>(sp => new ThrowingClassService(() =>
                Task.Delay(Timeout.Infinite, sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.RequestAborted)));
        }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app2.CreateClient().GetAsync("/classes", cts.Token));

        // Give the server side a moment to unwind, then check nothing was reported as an error.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !sink.Events.Any(e => e.MessageTemplate.Text.Contains("cancelled")))
            await Task.Delay(50);

        Assert.Contains(sink.Events, e => e.MessageTemplate.Text.Contains("cancelled") && e.Level == LogEventLevel.Information);
        Assert.DoesNotContain(sink.Events, e => e.Level >= LogEventLevel.Error);
    }

    [Fact]
    public async Task An_exception_after_the_response_started_is_logged_and_does_not_break_the_server()
    {
        var sink = new CapturingSink();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<ILogEventSink>(sink);
            s.AddHttpContextAccessor();
            s.RemoveAll<IClassService>();
            s.AddScoped<IClassService>(sp => new ThrowingClassService(async () =>
            {
                var response = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.Response;
                await response.WriteAsync("partial"); // starts the response: the status line is already gone
                throw new InvalidOperationException(Secret);
            }));
        }));
        var client = app.CreateClient();

        try { await client.GetAsync("/classes"); } catch (Exception) { /* a torn response may surface as a client error */ }

        var logged = Assert.Single(sink.Events, e => e.Exception is InvalidOperationException);
        Assert.Equal(LogEventLevel.Error, logged.Level);
        // The host is still healthy and still answers in the standard shape.
        await AssertStandardError(await client.GetAsync("/classes/c_does_not_exist"), HttpStatusCode.NotFound, "CLASS_NOT_FOUND");
    }
}
