namespace Api.Middleware;

/// <summary>
/// Stable error codes and messages for the HTTP-level 4xx that have no domain meaning of their own
/// (unknown route, wrong method, wrong content type, a request the server could not read).
/// Domain errors (CLASS_NOT_FOUND, HOLD_EXPIRED, ...) are produced by the controllers.
/// </summary>
internal static class HttpErrors
{
    public static (string Code, string Message) ForStatus(int status) => status switch
    {
        400 => ("BAD_REQUEST", "The request is not valid."),
        404 => ("ROUTE_NOT_FOUND", "The requested resource does not exist."),
        405 => ("METHOD_NOT_ALLOWED", "This HTTP method is not supported for this resource."),
        406 => ("NOT_ACCEPTABLE", "The server cannot produce a response in a format the client accepts; use application/json."),
        408 => ("REQUEST_TIMEOUT", "The request took too long to arrive."),
        413 => ("PAYLOAD_TOO_LARGE", "The request body is too large."),
        415 => ("UNSUPPORTED_MEDIA_TYPE", "The request body must be sent as application/json."),
        _ => ("REQUEST_REJECTED", "The request could not be processed.")
    };
}
