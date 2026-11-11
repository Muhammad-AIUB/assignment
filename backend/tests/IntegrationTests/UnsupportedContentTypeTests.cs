using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// A request whose Content-Type names a charset the API will not read (or a Content-Type it cannot parse) is answered
/// 415 UNSUPPORTED_MEDIA_TYPE in the standard shape, not the catch-all 400 UNEXPECTED_ERROR it used to be.
/// Found by the Session 22 test plan: <c>application/json; charset=utf-16</c> made MVC's transcoding stream throw
/// <see cref="DecoderFallbackException"/> on any body that was not valid UTF-16, and an empty <c>charset=</c> made the
/// framework's header parser throw <see cref="ArgumentOutOfRangeException"/>; both reached the global handler.
/// </summary>
[Collection("api")]
public class UnsupportedContentTypeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string Body = "{\"name\":\"__NAME__\",\"totalSeats\":5}";

    /// <summary>A body with an arbitrary Content-Type string, bypassing HttpClient's own header validation.</summary>
    private static ByteArrayContent Raw(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return content;
    }

    private static string NewName() => "CT-" + Guid.NewGuid().ToString("N")[..10];

    private static byte[] Utf8(string name) => Encoding.UTF8.GetBytes(Body.Replace("__NAME__", name));
    private static byte[] Utf16Le(string name) => Encoding.Unicode.GetBytes(Body.Replace("__NAME__", name));
    private static byte[] Utf16Be(string name) => Encoding.BigEndianUnicode.GetBytes(Body.Replace("__NAME__", name));

    private static async Task ExpectUnsupportedMediaType(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
        Assert.StartsWith("application/json", res.Content.Headers.ContentType?.ToString());
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["error", "message"], body.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("UNSUPPORTED_MEDIA_TYPE", body.GetProperty("error").GetString());
        Assert.Contains("UTF-8", body.GetProperty("message").GetString());
    }

    private async Task<bool> ClassExistsAsync(string name)
    {
        var list = await _client.GetFromJsonAsync<JsonElement>("/classes");
        return list.EnumerateArray().Any(c => c.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task Charset_utf16_on_a_body_that_is_not_valid_utf16_is_415_not_UNEXPECTED_ERROR()
    {
        // The reported case: a UTF-8 body labelled utf-16 (an odd number of bytes, so it cannot be UTF-16 at all).
        var name = NewName();
        var res = await _client.PostAsync("/classes", Raw(Utf8(name), "application/json; charset=utf-16"));

        await ExpectUnsupportedMediaType(res);
        Assert.False(await ClassExistsAsync(name), "a refused request must not create anything");
    }

    [Fact]
    public async Task Charset_utf16_on_a_body_that_is_valid_utf16_is_415_too_json_is_utf8_only()
    {
        // Before this change a well-formed UTF-16LE body was accepted (201). JSON between systems is UTF-8 (RFC 8259),
        // and the API already refused every other non-UTF-8 charset (utf-32, iso-8859-1, ...), so UTF-16 is refused as well.
        var name = NewName();
        var res = await _client.PostAsync("/classes", Raw(Utf16Le(name), "application/json; charset=utf-16"));

        await ExpectUnsupportedMediaType(res);
        Assert.False(await ClassExistsAsync(name));
    }

    [Theory]
    [InlineData("application/json; charset=UTF-16")]
    [InlineData("application/json; charset=utf-16le")]
    [InlineData("application/json; charset=utf-16be")]
    [InlineData("application/json; charset=\"utf-16\"")]
    [InlineData("application/json; charset=utf-32")]
    [InlineData("application/json; charset=iso-8859-1")]
    [InlineData("application/json; charset=bogus")]
    public async Task Any_charset_other_than_utf8_is_415_UNSUPPORTED_MEDIA_TYPE(string contentType)
    {
        var res = await _client.PostAsync("/classes", Raw(Utf16Be(NewName()), contentType));
        await ExpectUnsupportedMediaType(res);
    }

    [Theory]
    [InlineData("application/json; charset=")]
    [InlineData("application/json; charset=\"\"")]
    [InlineData("garbage")]
    public async Task A_content_type_that_cannot_be_parsed_is_415_UNSUPPORTED_MEDIA_TYPE(string contentType)
    {
        var name = NewName();
        var res = await _client.PostAsync("/classes", Raw(Utf8(name), contentType));

        await ExpectUnsupportedMediaType(res);
        Assert.False(await ClassExistsAsync(name));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/json;charset=UTF-8")]
    [InlineData("application/json; charset=\"utf-8\"")]
    public async Task The_usual_ways_of_sending_json_still_work(string contentType)
    {
        var name = NewName();
        var res = await _client.PostAsync("/classes", Raw(Utf8(name), contentType));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.True(await ClassExistsAsync(name));
    }

    [Fact]
    public async Task A_content_type_on_a_request_that_has_no_body_is_not_judged()
    {
        // Content-Type describes a body. GET /classes has none, so a charset the API would refuse on a body means nothing here.
        var req = new HttpRequestMessage(HttpMethod.Get, "/classes") { Content = Raw([], "application/json; charset=utf-16") };
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task The_refusal_is_readable_by_the_frontend_and_carries_the_correlation_id()
    {
        // Same guarantees as every other error: CORS headers so the page can read the message, and a correlation id.
        var req = new HttpRequestMessage(HttpMethod.Post, "/classes") { Content = Raw(Utf8(NewName()), "application/json; charset=utf-16") };
        req.Headers.Add("Origin", "http://localhost:5173");
        var res = await _client.SendAsync(req);

        await ExpectUnsupportedMediaType(res);
        Assert.True(res.Headers.Contains("X-Correlation-Id"));
        Assert.Equal("http://localhost:5173", Assert.Single(res.Headers.GetValues("Access-Control-Allow-Origin")));
    }
}
