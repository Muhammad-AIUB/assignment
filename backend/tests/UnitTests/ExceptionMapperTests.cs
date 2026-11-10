using Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace UnitTests;

/// <summary>
/// The mapper decides what a leaked exception becomes: always a 4xx and a stable error code, and a message
/// that never repeats anything from the exception itself (so no SQL, paths or stack frames reach the client).
/// </summary>
public class ExceptionMapperTests
{
    private const string Secret = "SECRET-INTERNAL-DETAIL: select * from holds";

    private static PostgresException Pg(string sqlState) =>
        new(Secret, "ERROR", "ERROR", sqlState);

    [Fact]
    public void An_unknown_exception_becomes_a_4xx_with_a_generic_message()
    {
        var m = ExceptionMapper.Map(new InvalidOperationException(Secret));

        Assert.Equal(400, m.StatusCode);
        Assert.Equal("UNEXPECTED_ERROR", m.Code);
        Assert.Equal(LogLevel.Error, m.LogLevel);
        Assert.DoesNotContain("SECRET", m.Message);
    }

    [Theory]
    [InlineData("40P01")] // deadlock_detected
    [InlineData("40001")] // serialization_failure
    [InlineData("55P03")] // lock_not_available
    [InlineData("23505")] // unique_violation
    [InlineData("23503")] // foreign_key_violation
    [InlineData("23514")] // check_violation
    public void A_database_conflict_is_409_CONFLICT(string sqlState)
    {
        var m = ExceptionMapper.Map(Pg(sqlState));

        Assert.Equal(409, m.StatusCode);
        Assert.Equal("CONFLICT", m.Code);
        Assert.Equal(LogLevel.Warning, m.LogLevel);
        Assert.DoesNotContain("SECRET", m.Message);
    }

    [Fact]
    public void A_database_conflict_is_found_behind_wrapper_exceptions()
    {
        var wrapped = new DbUpdateException("save failed", new InvalidOperationException("x", Pg("40P01")));

        var m = ExceptionMapper.Map(wrapped);

        Assert.Equal(409, m.StatusCode);
        Assert.Equal("CONFLICT", m.Code);
    }

    [Theory]
    [InlineData("22021")] // invalid byte sequence for encoding (e.g. a NUL character)
    [InlineData("22001")] // string data right truncation
    [InlineData("22003")] // numeric value out of range
    [InlineData("22P02")] // invalid text representation
    public void A_database_data_exception_is_400_INVALID_INPUT(string sqlState)
    {
        var m = ExceptionMapper.Map(Pg(sqlState));

        Assert.Equal(400, m.StatusCode);
        Assert.Equal("INVALID_INPUT", m.Code);
        Assert.DoesNotContain("SECRET", m.Message);
    }

    [Fact]
    public void Another_database_error_is_still_a_4xx_and_leaks_nothing()
    {
        var m = ExceptionMapper.Map(Pg("42P01")); // undefined_table

        Assert.InRange(m.StatusCode, 400, 499);
        Assert.Equal("UNEXPECTED_ERROR", m.Code);
        Assert.Equal(LogLevel.Error, m.LogLevel);
        Assert.DoesNotContain("SECRET", m.Message);
    }

    [Theory]
    [InlineData(400, "BAD_REQUEST")]
    [InlineData(408, "REQUEST_TIMEOUT")]
    [InlineData(413, "PAYLOAD_TOO_LARGE")]
    public void A_bad_http_request_keeps_its_own_4xx_status(int status, string code)
    {
        var m = ExceptionMapper.Map(new BadHttpRequestException(Secret, status));

        Assert.Equal(status, m.StatusCode);
        Assert.Equal(code, m.Code);
        Assert.Equal(LogLevel.Warning, m.LogLevel);
        Assert.DoesNotContain("SECRET", m.Message);
    }

    [Fact]
    public void An_aggregate_of_one_exception_maps_like_that_exception()
    {
        var m = ExceptionMapper.Map(new AggregateException(Pg("40P01")));

        Assert.Equal(409, m.StatusCode);
    }

    [Fact]
    public void Every_mapping_is_a_4xx_with_a_non_empty_code_and_message()
    {
        Exception[] samples =
        [
            new Exception(Secret), new NullReferenceException(), new TimeoutException(Secret),
            new IOException(Secret), new NpgsqlException(Secret), Pg("08006"), Pg("57P01"),
            new BadHttpRequestException(Secret, 500) // a non-4xx status must never be passed through
        ];

        foreach (var ex in samples)
        {
            var m = ExceptionMapper.Map(ex);
            Assert.InRange(m.StatusCode, 400, 499);
            Assert.False(string.IsNullOrWhiteSpace(m.Code));
            Assert.False(string.IsNullOrWhiteSpace(m.Message));
            Assert.DoesNotContain("SECRET", m.Message);
        }
    }
}
