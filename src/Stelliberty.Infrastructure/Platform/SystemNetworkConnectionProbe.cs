using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed partial class SystemNetworkConnectionProbe : INetworkConnectionProbe
{
    public NetworkConnectionInfo Detect()
    {
        try
        {
            var primary = SelectPrimaryInterface();
            if (primary is null)
            {
                return NetworkConnectionInfo.Disconnected;
            }

            var type = MapType(primary.NetworkInterfaceType);
            return new NetworkConnectionInfo(type, ResolveName(primary, type));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Network connection probe failed: {exception.Message}");
            return NetworkConnectionInfo.Disconnected;
        }
    }

    private static NetworkInterface? SelectPrimaryInterface()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static n => n.OperationalStatus == OperationalStatus.Up)
            .Where(static n => n.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel)
            .Where(HasGateway)
            .ToArray();

        return candidates.FirstOrDefault(static n => MapType(n.NetworkInterfaceType) == NetworkConnectionType.Wifi)
            ?? candidates.FirstOrDefault(static n => MapType(n.NetworkInterfaceType) == NetworkConnectionType.Wired)
            ?? candidates.FirstOrDefault();
    }

    private static bool HasGateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static NetworkConnectionType MapType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => NetworkConnectionType.Wifi,
        NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx => NetworkConnectionType.Wired,
        _ => NetworkConnectionType.Other
    };

    private static string ResolveName(NetworkInterface adapter, NetworkConnectionType type)
    {
        if (type == NetworkConnectionType.Wifi && OperatingSystem.IsWindows())
        {
            // netsh 依赖位置权限；注册表是降级来源。
            var ssid = TryReadWifiSsid() ?? TryReadWifiSsidFromRegistry();
            if (!string.IsNullOrWhiteSpace(ssid))
            {
                return ssid;
            }
        }

        return adapter.Name;
    }

    private static string? TryReadWifiSsid()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(1000))
            {
                try { process.Kill(); }
                catch
                {
                    // 超时竞态会把 SSID 探测降级到备用路径。
                }
                return null;
            }

            var match = SsidRegex().Match(process.StandardOutput.ReadToEnd());
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryReadWifiSsidFromRegistry()
    {
        try
        {
            const string profilesPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles";
            using var profiles = Registry.LocalMachine.OpenSubKey(profilesPath);
            if (profiles is null)
            {
                return null;
            }

            string? latestName = null;
            long latestStamp = -1;
            foreach (var guid in profiles.GetSubKeyNames())
            {
                using var profile = profiles.OpenSubKey(guid);
                // NameType 71 表示无线配置文件，不是有线连接。
                if (profile?.GetValue("NameType") is not int nameType || nameType != 71)
                {
                    continue;
                }

                if (profile.GetValue("ProfileName") is not string name || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var stamp = SystemTimeToStamp(profile.GetValue("DateLastConnected") as byte[]);
                if (stamp > latestStamp)
                {
                    latestStamp = stamp;
                    latestName = name;
                }
            }

            return latestName;
        }
        catch
        {
            return null;
        }
    }

    private static long SystemTimeToStamp(byte[]? systemTime)
    {
        if (systemTime is null || systemTime.Length < 16)
        {
            return 0;
        }

        // SYSTEMTIME 是 16 字节小端 WORD 字段。
        int Word(int offset) => systemTime[offset] | (systemTime[offset + 1] << 8);
        long year = Word(0), month = Word(2), day = Word(6), hour = Word(8), minute = Word(10), second = Word(12);
        return ((((year * 13 + month) * 32 + day) * 24 + hour) * 60 + minute) * 60 + second;
    }

    [GeneratedRegex(@"(?m)^\s*SSID\s*:\s*(.+?)\s*$")]
    private static partial Regex SsidRegex();
}
