using Api.Dtos;

namespace Api.Middleware;

/// <summary>Writes the one error shape the API uses: <c>{ "error": "SOME_CODE", "message": "..." }</c>.</summary>
internal static class ErrorResponseWriter
{
    public static Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(code, message));
    }
}
