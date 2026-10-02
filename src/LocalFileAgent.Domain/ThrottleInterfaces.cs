using System.Threading;
using System.Threading.Tasks;

namespace LocalFileAgent.Domain.Throttling;

public sealed record PowerStatusInfo(
    bool IsOnBattery,
    int? BatteryLifePercent,
    bool IsThrottled
);

public sealed record ResourceThrottleOptions(
    bool PauseOnBattery = true,
    int BatteryThresholdPercent = 20,
    int ThrottleDelayMs = 50,
    bool EnableCpuThrottling = false
);

public interface IPowerStatusProvider
{
    PowerStatusInfo GetPowerStatus();
}

public interface IResourceGovernor
{
    ResourceThrottleOptions Options { get; }
    PowerStatusInfo GetPowerStatus();
    bool ShouldThrottleHeavyWork();
    Task WaitIfThrottledAsync(CancellationToken cancellationToken = default);
}
