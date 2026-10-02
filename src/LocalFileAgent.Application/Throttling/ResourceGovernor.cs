using System;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Throttling;

namespace LocalFileAgent.Application.Throttling;

public sealed class ResourceGovernor : IResourceGovernor
{
    private readonly IPowerStatusProvider _powerProvider;
    public ResourceThrottleOptions Options { get; }

    public ResourceGovernor(IPowerStatusProvider? powerProvider = null, ResourceThrottleOptions? options = null)
    {
        _powerProvider = powerProvider ?? new FallbackPowerStatusProvider();
        Options = options ?? new ResourceThrottleOptions();
    }

    public PowerStatusInfo GetPowerStatus()
    {
        var rawStatus = _powerProvider.GetPowerStatus();
        var shouldThrottle = ShouldThrottle(rawStatus);
        return new PowerStatusInfo(
            IsOnBattery: rawStatus.IsOnBattery,
            BatteryLifePercent: rawStatus.BatteryLifePercent,
            IsThrottled: shouldThrottle
        );
    }

    public bool ShouldThrottleHeavyWork()
    {
        var rawStatus = _powerProvider.GetPowerStatus();
        return ShouldThrottle(rawStatus);
    }

    private bool ShouldThrottle(PowerStatusInfo status)
    {
        if (!status.IsOnBattery)
        {
            return false;
        }

        if (Options.PauseOnBattery)
        {
            return true;
        }

        if (status.BatteryLifePercent.HasValue && status.BatteryLifePercent.Value <= Options.BatteryThresholdPercent)
        {
            return true;
        }

        return false;
    }

    public async Task WaitIfThrottledAsync(CancellationToken cancellationToken = default)
    {
        if (ShouldThrottleHeavyWork())
        {
            await Task.Delay(Options.ThrottleDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class FallbackPowerStatusProvider : IPowerStatusProvider
    {
        public PowerStatusInfo GetPowerStatus() =>
            new(IsOnBattery: false, BatteryLifePercent: 100, IsThrottled: false);
    }
}
