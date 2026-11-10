using Infrastructure;
using Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class HoldExpirySweepOptionsTests
{
    private static HoldExpirySweepOptions Resolve(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused;Username=u;Password=p"
        };
        foreach (var (key, value) in settings) values[key] = value;

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var provider = new ServiceCollection().AddLogging().AddInfrastructure(config).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HoldExpirySweepOptions>>().Value;
    }

    [Fact]
    public void Defaults_to_enabled_with_a_five_second_interval()
    {
        var options = Resolve();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Interval);
    }

    [Fact]
    public void Reads_the_flag_and_interval_from_configuration()
    {
        var options = Resolve(
            ("HOLD_EXPIRY_SWEEP_ENABLED", "false"),
            ("HOLD_EXPIRY_SWEEP_INTERVAL_SECONDS", "8"));

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(8), options.Interval);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void Rejects_an_interval_that_is_not_positive(string seconds)
    {
        Assert.Throws<OptionsValidationException>(() =>
            Resolve(("HOLD_EXPIRY_SWEEP_INTERVAL_SECONDS", seconds)));
    }
}
