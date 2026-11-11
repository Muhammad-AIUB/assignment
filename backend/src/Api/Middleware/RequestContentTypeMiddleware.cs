using Microsoft.Net.Http.Headers;

namespace Api.Middleware;

/// <summary>
/// Refuses, before any model binding, a request body whose Content-Type is unparseable or declares a charset other than UTF-8
/// (see <see cref="RequestContentType"/>): 415 <c>UNSUPPORTED_MEDIA_TYPE</c> in the standard error shape, so it never reaches
/// the catch-all as <c>UNEXPECTED_ERROR</c>. Only requests that have a body are judged; a Content-Type describes a body, and a
/// <c>GET</c> or a body-less <c>POST .../confirm</c> has none to misread.
/// </summary>
public sealed class RequestContentTypeMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!HasBody(request) || RequestContentType.IsReadable(request.ContentType))
            return next(context);

        var (code, message) = HttpErrors.ForStatus(StatusCodes.Status415UnsupportedMediaType);
        return ErrorResponseWriter.WriteAsync(context, StatusCodes.Status415UnsupportedMediaType, code, message);
    }

    // A fixed length above zero, or a chunked body (which has no Content-Length).
    private static bool HasBody(HttpRequest request) =>
        request.ContentLength > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding);
}
