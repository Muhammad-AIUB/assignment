using Api.Middleware;

namespace UnitTests;

/// <summary>
/// The rule for the Content-Type of a request that carries a body: if the header names a charset it must be UTF-8 (the only
/// encoding JSON may use between systems, RFC 8259 section 8.1), and the header must be one the server can parse at all.
/// The media type itself (application/json or not) is not this rule's business; MVC already answers 415 for that.
/// </summary>
public class RequestContentTypeTests
{
    [Theory]
    [InlineData(null)]                                           // no header: nothing to judge here (MVC answers for a body with no type)
    [InlineData("")]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/json;charset=utf-8")]
    [InlineData("application/json; charset=UTF-8")]
    [InlineData("Application/JSON; Charset=Utf-8")]
    [InlineData("application/json; charset=\"utf-8\"")]          // quoted, as RFC 7231 allows
    [InlineData("application/json; charset=utf-8; profile=x")]
    [InlineData("text/plain")]                                   // a wrong media type is MVC's 415, not decided here
    public void A_charset_of_utf8_or_none_is_readable(string? contentType) =>
        Assert.True(RequestContentType.IsReadable(contentType), $"expected readable: {contentType}");

    [Theory]
    [InlineData("application/json; charset=utf-16")]             // the reported case
    [InlineData("application/json; charset=UTF-16")]
    [InlineData("application/json; charset=utf-16le")]
    [InlineData("application/json; charset=utf-16be")]
    [InlineData("application/json; charset=\"utf-16\"")]
    [InlineData("application/json; charset=utf-32")]
    [InlineData("application/json; charset=iso-8859-1")]
    [InlineData("application/json; charset=us-ascii")]
    [InlineData("application/json; charset=utf8")]               // not a registered name: "utf-8" is
    [InlineData("application/json; charset=bogus")]
    [InlineData("text/plain; charset=utf-16")]
    public void Any_other_charset_is_not_readable(string contentType) =>
        Assert.False(RequestContentType.IsReadable(contentType), $"expected not readable: {contentType}");

    [Theory]
    [InlineData("application/json; charset=")]                   // the framework's own parser throws on this one
    [InlineData("application/json; charset=\"\"")]
    [InlineData("garbage")]
    [InlineData("application/")]
    [InlineData(";")]
    public void A_header_that_cannot_be_parsed_is_not_readable(string contentType) =>
        Assert.False(RequestContentType.IsReadable(contentType), $"expected not readable: {contentType}");
}
