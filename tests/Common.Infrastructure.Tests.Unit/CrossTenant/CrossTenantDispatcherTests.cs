namespace Stratum.Common.Infrastructure.Tests.Unit.CrossTenant;

using Stratum.Common.Infrastructure.CrossTenant;
using Xunit;

public class CrossTenantDispatcherTests
{
    [Fact]
    public void Options_have_correct_defaults()
    {
        var options = new CrossTenantDispatcherOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(5, options.MaxRetries);
    }

    [Fact]
    public void Options_section_name_is_correct()
    {
        Assert.Equal("CrossTenantDispatcher", CrossTenantDispatcherOptions.SectionName);
    }

    [Fact]
    public void Options_properties_are_settable()
    {
        var options = new CrossTenantDispatcherOptions
        {
            PollingInterval = TimeSpan.FromSeconds(10),
            BatchSize = 50,
            MaxRetries = 3,
        };

        Assert.Equal(TimeSpan.FromSeconds(10), options.PollingInterval);
        Assert.Equal(50, options.BatchSize);
        Assert.Equal(3, options.MaxRetries);
    }
}
