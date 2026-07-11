using System.Net;
using System.Net.NetworkInformation;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class NetworkInterfaceSystemProxyHostDetector : ISystemProxyHostDetector
{
    public SystemProxyHostDetectionResult Detect()
    {
        var hostName = Dns.GetHostName();
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(IsUsableAddress)
            .Select(address => address.ToString())
            .ToArray();

        AppLogger.Info($"System proxy host detection completed: addresses={addresses.Length}");
        return new SystemProxyHostDetectionResult(hostName, addresses);
    }

    private static bool IsUsableAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // IPv4 本地链路地址不能用做代理主机。
            var bytes = address.GetAddressBytes();
            return bytes is not [169, 254, _, _];
        }

        // 保留 IPv6 候选，但排除 fe80:: 本地链路地址。
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            && !address.IsIPv6LinkLocal;
    }
}
