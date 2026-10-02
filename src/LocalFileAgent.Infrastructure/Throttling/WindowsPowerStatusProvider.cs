using System;
using System.Runtime.InteropServices;
using LocalFileAgent.Domain.Throttling;

namespace LocalFileAgent.Infrastructure.Throttling;

public sealed class WindowsPowerStatusProvider : IPowerStatusProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = Offline (Battery), 1 = Online (AC), 255 = Unknown
        public byte BatteryFlag;  // 1 = High, 2 = Low, 4 = Critical, 8 = Charging, 128 = No battery, 255 = Unknown
        public byte BatteryLifePercent; // 0..100, 255 = Unknown
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public PowerStatusInfo GetPowerStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PowerStatusInfo(IsOnBattery: false, BatteryLifePercent: 100, IsThrottled: false);
        }

        try
        {
            if (GetSystemPowerStatus(out var status))
            {
                var isOnBattery = status.ACLineStatus == 0;
                int? percent = status.BatteryLifePercent <= 100 ? (int)status.BatteryLifePercent : null;
                return new PowerStatusInfo(
                    IsOnBattery: isOnBattery,
                    BatteryLifePercent: percent,
                    IsThrottled: isOnBattery && (percent == null || percent <= 20)
                );
            }
        }
        catch
        {
            // Non-fatal fallback
        }

        return new PowerStatusInfo(IsOnBattery: false, BatteryLifePercent: 100, IsThrottled: false);
    }
}
