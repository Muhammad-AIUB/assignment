using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Api.Middleware;

/// <summary>What an exception becomes on the wire, and how loudly it is logged.</summary>
public readonly record struct ErrorMapping(int StatusCode, string Code, string Message, LogLevel LogLevel);

/// <summary>
/// Decides the response for an exception that escaped a controller. The rules:
/// <list type="bullet">
/// <item>The status is always 4xx (project rule: a failure is never reported as a 5xx).</item>
/// <item>The message is fixed text. Nothing from the exception (SQL, paths, type names, stack frames) is ever included;
/// the full exception goes to the log instead.</item>
/// <item>Wrapper exceptions (<c>DbUpdateException</c>, <c>AggregateException</c>, ...) are looked through to the
/// exception that actually says what went wrong.</item>
/// </list>
/// </summary>
public static class ExceptionMapper
{
    public const string UnexpectedErrorCode = "UNEXPECTED_ERROR";

    public static ErrorMapping Map(Exception exception)
    {
        foreach (var ex in Chain(exception))
        {
            switch (ex)
            {
                case BadHttpRequestException bad:
                {
                    // Kestrel / model binding already decided the status; keep it, but only if it is a 4xx.
                    var status = bad.StatusCode is >= 400 and <= 499 ? bad.StatusCode : StatusCodes.Status400BadRequest;
                    var (code, message) = HttpErrors.ForStatus(status);
                    return new ErrorMapping(status, code, message, LogLevel.Warning);
                }

                // Two requests collided in the database (deadlock, serialization failure, lock timeout, or an integrity
                // constraint such as the unique index on active holds). The caller can simply retry.
                case PostgresException pg when IsConflict(pg.SqlState):
                    return new ErrorMapping(StatusCodes.Status409Conflict, "CONFLICT",
                        "The request conflicted with another request and could not be completed. Please retry.",
                        LogLevel.Warning);

                // SQLSTATE class 22 = data exception: the database refused a value it was given
                // (a NUL character, a string or number outside the column's range, ...). The caller's input is the cause.
                case PostgresException { SqlState: { } s } when s.StartsWith("22", StringComparison.Ordinal):
                    return new ErrorMapping(StatusCodes.Status400BadRequest, "INVALID_INPUT",
                        "A value in the request is not valid.", LogLevel.Warning);
            }
        }

        return new ErrorMapping(StatusCodes.Status400BadRequest, UnexpectedErrorCode,
            "The request could not be processed. Please try again later.", LogLevel.Error);
    }

    /// <summary>The exception and everything nested inside it, outermost first.</summary>
    internal static IEnumerable<Exception> Chain(Exception root)
    {
        var pending = new Stack<Exception>();
        pending.Push(root);
        for (var visited = 0; pending.Count > 0 && visited < 32; visited++)
        {
            var ex = pending.Pop();
            yield return ex;

            if (ex is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions.Reverse()) pending.Push(inner);
            else if (ex.InnerException is not null)
                pending.Push(ex.InnerException);
        }
    }

    private static bool IsConflict(string? sqlState) =>
        sqlState is "40001" or "40P01" or "55P03" // serialization_failure, deadlock_detected, lock_not_available
        || (sqlState is not null && sqlState.StartsWith("23", StringComparison.Ordinal)); // integrity constraint (unique, FK, check)
}
