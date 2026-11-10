using System.Text.RegularExpressions;
using Serilog.Context;

namespace Api.Middleware;

/// <summary>
/// Gives every request one correlation id: the caller's <c>X-Correlation-Id</c> if it is safe to echo, otherwise a fresh one.
/// The id is returned on every response (including errors) and attached to every Serilog event written while the request
/// runs, so a client that quotes it lets someone find exactly the log lines for that request.
/// Registered first, so the request-logging middleware inside it carries the property too.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogProperty = "CorrelationId";

    private const string ItemKey = "Api.CorrelationId";

    // Echoing a caller-controlled value back into a header and into the logs: keep it short and boring.
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeId();

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var id = SafeId().IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;

        // OnStarting, not a plain assignment: the error handler clears the headers of a half-built response, and this
        // still runs after that, right before the headers go out.
        context.Response.OnStarting(static state =>
        {
            var (ctx, value) = ((HttpContext, string))state;
            ctx.Response.Headers[HeaderName] = value;
            return Task.CompletedTask;
        }, (context, id));

        using (LogContext.PushProperty(LogProperty, id))
        {
            await next(context);
        }
    }

    /// <summary>The id of the current request (falls back to the ASP.NET request id if the middleware did not run).</summary>
    public static string GetId(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var id) && id is string s ? s : context.TraceIdentifier;
}
