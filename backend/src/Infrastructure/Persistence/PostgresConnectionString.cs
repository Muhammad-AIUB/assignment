using Npgsql;

namespace Infrastructure.Persistence;

/// <summary>
/// Gives the PostgreSQL connection string a cap on pooled connections when it has none.
/// <para>
/// Npgsql's own default is 100 connections per process, which equals PostgreSQL's default <c>max_connections</c> of 100.
/// After one burst of simultaneous requests a single instance can therefore hold every connection the server has and keep
/// them idle (Npgsql prunes idle ones slowly). The next instance then has none left and its requests fail with SQLSTATE 53300
/// "too many clients already", which the student sees as 400 <c>UNEXPECTED_ERROR</c>. Seat safety is unaffected (it lives in the
/// database); this is capacity only. Found by the Session 22 dual-instance run: one instance held 98 idle connections.
/// </para>
/// <para>
/// <see cref="DefaultMaxPoolSize"/> is 30: PostgreSQL keeps 3 of its 100 connections for superusers, leaving 97, and 3 instances
/// of 30 take 90, so three instances fit with a little room for an operator's <c>psql</c>. A request holds one connection for
/// one short transaction and no code path needs a second connection while holding one, so 30 do not limit throughput: when all
/// 30 are busy a request waits briefly for one instead of failing (the seat claim serialises on the class row anyway).
/// An explicit <c>Maximum Pool Size</c> in the connection string always wins; raise it together with the server's
/// <c>max_connections</c>.
/// </para>
/// </summary>
public static class PostgresConnectionString
{
    public const int DefaultMaxPoolSize = 30;

    /// <summary>
    /// Parses <paramref name="connectionString"/> and, if it does not set <c>Maximum Pool Size</c> (or its alias
    /// <c>MaxPoolSize</c>), sets it to <see cref="DefaultMaxPoolSize"/>. Throws <see cref="ArgumentException"/> for a string that
    /// is not a valid connection string, so a typo stops the host at startup instead of failing at the first query.
    /// </summary>
    public static NpgsqlConnectionStringBuilder WithDefaultPoolLimit(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // ShouldSerialize is true only for a keyword the string actually contained; MaxPoolSize alone cannot say, because it
        // reads 100 both when the string says 100 and when it says nothing.
        if (!builder.ShouldSerialize("Maximum Pool Size"))
        {
            // Never below the minimum: "Minimum Pool Size=50" was valid with Npgsql's 100 and must not become invalid here.
            builder.MaxPoolSize = Math.Max(DefaultMaxPoolSize, builder.MinPoolSize);
        }

        return builder;
    }
}
