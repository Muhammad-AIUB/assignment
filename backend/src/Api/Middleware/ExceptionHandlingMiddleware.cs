namespace Api.Middleware;

/// <summary>
/// Last line of defence: any exception a controller or service lets escape is logged in full and answered with the
/// standard error shape and a 4xx status (see <see cref="ExceptionMapper"/>), never a raw 500 or a stack trace.
/// Predictable failures are not meant to get here; controllers answer those themselves with their own 4xx.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value;

        // The caller hung up. Nobody is waiting for an answer and nothing went wrong on our side.
        if (context.RequestAborted.IsCancellationRequested && ExceptionMapper.Chain(ex).Any(e => e is OperationCanceledException))
        {
            logger.LogInformation("Request {Method} {Path} was cancelled by the client", method, path);
            if (!context.Response.HasStarted) context.Response.StatusCode = 499; // "client closed request" (nginx convention)
            return;
        }

        var mapped = ExceptionMapper.Map(ex);
        logger.Log(mapped.LogLevel, ex,
            "Unhandled {ExceptionType} on {Method} {Path}; answering {StatusCode} {ErrorCode}",
            ex.GetType().Name, method, path, mapped.StatusCode, mapped.Code);

        if (context.Response.HasStarted)
        {
            // The status line and headers are already on the wire, so an error body cannot be sent. Cut the connection so
            // the client sees a failed response instead of a truncated one that looks complete.
            logger.LogWarning("The response to {Method} {Path} had already started; aborting the connection", method, path);
            context.Abort();
            return;
        }

        // Drop whatever the failed handler had put on the response (content type, length, ...). The correlation id header is
        // re-applied by CorrelationIdMiddleware when the new response starts.
        context.Response.Headers.Clear();

        var message = mapped.Code == ExceptionMapper.UnexpectedErrorCode
            ? $"{mapped.Message} Reference: {CorrelationIdMiddleware.GetId(context)}"
            : mapped.Message;

        try
        {
            await ErrorResponseWriter.WriteAsync(context, mapped.StatusCode, mapped.Code, message);
        }
        catch (Exception writeFailure)
        {
            logger.LogWarning(writeFailure, "Could not write the error response for {Method} {Path}", method, path);
        }
    }
}
