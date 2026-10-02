using FluentAssertions;
using LocalFileAgent.Infrastructure.Throttling;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class PowerStatusProviderTests
{
    [Fact]
    public void WindowsPowerStatusProvider_GetPowerStatus_ReturnsConsistentResult()
    {
        var provider = new WindowsPowerStatusProvider();
        var status = provider.GetPowerStatus();

        status.Should().NotBeNull();
        if (status.BatteryLifePercent.HasValue)
        {
            status.BatteryLifePercent.Value.Should().BeInRange(0, 100);
        }
    }
}
