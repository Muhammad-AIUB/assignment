using Microsoft.Net.Http.Headers;

namespace Api.Middleware;

/// <summary>
/// The rule for the <c>Content-Type</c> of a request that carries a body: the header must be one the server can parse, and if
/// it names a charset, that charset must be UTF-8. JSON exchanged between systems is UTF-8 (RFC 8259, section 8.1), and the
/// API already refused every other non-UTF-8 charset (utf-32, iso-8859-1, ...) with 415; UTF-16 was the exception, because MVC
/// ships a UTF-16 decoder for JSON. That decoder throws <see cref="System.Text.DecoderFallbackException"/> on a body that is
/// not valid UTF-16, which nothing caught, so it reached the global handler as 400 <c>UNEXPECTED_ERROR</c>. A header such as
/// <c>charset=</c> (nothing after the equals sign) did the same through <see cref="ArgumentOutOfRangeException"/> in the
/// framework's own header parser. Both are predictable bad input and are answered 415 <c>UNSUPPORTED_MEDIA_TYPE</c> instead.
/// The media type itself (<c>application/json</c> or not) is not judged here: MVC already answers 415 for that.
/// </summary>
public static class RequestContentType
{
    private const string Utf8 = "utf-8";

    /// <summary>True when the header is absent, or parses and names no charset or UTF-8.</summary>
    public static bool IsReadable(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return true; // nothing to judge; a body with no type is MVC's 415

        // This parser (unlike the one inside MVC's input formatter) reports a bad header as "false" instead of throwing.
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType)) return false;

        // The parameter keeps its quotes as written, and RFC 7231 allows charset="utf-8", so unquote before comparing.
        var charset = HeaderUtilities.RemoveQuotes(mediaType.Charset);
        return !charset.HasValue || charset.Equals(Utf8, StringComparison.OrdinalIgnoreCase);
    }
}
