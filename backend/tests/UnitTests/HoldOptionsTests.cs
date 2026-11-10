using Infrastructure;
using Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UnitTests;

/// <summary>HOLD_DURATION_SECONDS: how long a student has to pay before the seat is released.</summary>
public class HoldOptionsTests
{
    private static HoldOptions Resolve(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused;Username=u;Password=p"
        };
        foreach (var (key, value) in settings) values[key] = value;

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var provider = new ServiceCollection().AddLogging().AddInfrastructure(config).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HoldOptions>>().Value;
    }

    [Fact]
    public void Defaults_to_ten_minutes_when_nothing_is_configured()
    {
        var options = Resolve();

        Assert.Equal(600, options.DurationSeconds);
        Assert.Equal(TimeSpan.FromMinutes(10), options.Duration);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("30", 30)]
    [InlineData("3600", 3600)]
    public void Reads_the_duration_in_seconds_from_configuration(string value, int seconds)
    {
        var options = Resolve(("HOLD_DURATION_SECONDS", value));

        Assert.Equal(seconds, options.DurationSeconds);
        Assert.Equal(TimeSpan.FromSeconds(seconds), options.Duration);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-600")]
    public void Rejects_a_duration_that_is_not_positive(string value)
    {
        // A zero or negative window would create holds that are already expired, so the app refuses to start with it.
        var ex = Assert.Throws<OptionsValidationException>(() => Resolve(("HOLD_DURATION_SECONDS", value)));

        Assert.Contains("HOLD_DURATION_SECONDS", ex.Message);
    }

    [Theory]
    [InlineData("ten minutes")]
    [InlineData("1.5")]
    public void Rejects_a_duration_that_is_not_a_whole_number(string value)
    {
        Assert.Throws<InvalidOperationException>(() => Resolve(("HOLD_DURATION_SECONDS", value)));
    }
}
