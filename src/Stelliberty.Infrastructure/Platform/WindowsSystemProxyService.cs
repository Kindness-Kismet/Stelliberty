using System.ComponentModel;
using System.Runtime.InteropServices;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class WindowsSystemProxyService(string appDataDirectory) : ISystemProxyService
{
    // WinINet 选项常量必须匹配 InternetSetOptionW 结构布局。
    private const uint InternetOptionPerConnectionOption = 75;
    private const uint InternetOptionSettingsChanged = 39;
    private const uint InternetOptionRefresh = 37;
    private const uint InternetPerConnFlags = 1;
    private const uint InternetPerConnProxyServer = 2;
    private const uint InternetPerConnProxyBypass = 3;
    private const uint InternetPerConnAutoconfigUrl = 4;
    private const uint ProxyTypeDirect = 0x00000001;
    private const uint ProxyTypeProxy = 0x00000002;
    private const uint ProxyTypeAutoProxyUrl = 0x00000004;

    public SystemProxyOperationResult Enable(SystemProxyApplicationRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SystemProxyOperationResult(false, "Windows system proxy is not supported in this environment");
        }

        try
        {
            if (request.IsPacModeEnabled)
            {
                return EnablePac(request);
            }

            var proxyServer = $"{request.Host}:{request.Port}";
            var bypassRules = string.Join(';', request.BypassRules);
            using var proxyServerText = NativeText.From(proxyServer);
            using var bypassText = NativeText.From(bypassRules);
            var options = new[]
            {
                // Flags 可叠加；Direct 保留系统直连备选。
                InternetPerConnOption.Flags(ProxyTypeDirect | ProxyTypeProxy),
                InternetPerConnOption.String(InternetPerConnProxyServer, proxyServerText.Pointer),
                InternetPerConnOption.String(InternetPerConnProxyBypass, bypassText.Pointer)
            };

            SetOptions(options);
            AppLogger.Info($"Windows system proxy enabled: {proxyServer}");
            return new SystemProxyOperationResult(true, $"System proxy enabled: {proxyServer}");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Windows system proxy enable failed: {exception}");
            return new SystemProxyOperationResult(false, exception.Message);
        }
    }

    public SystemProxyOperationResult Disable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SystemProxyOperationResult(false, "Windows system proxy is not supported in this environment");
        }

        try
        {
            using var empty = NativeText.From(string.Empty);
            var options = new[]
            {
                InternetPerConnOption.Flags(ProxyTypeDirect),
                InternetPerConnOption.String(InternetPerConnProxyServer, empty.Pointer),
                InternetPerConnOption.String(InternetPerConnProxyBypass, empty.Pointer),
                InternetPerConnOption.String(InternetPerConnAutoconfigUrl, empty.Pointer)
            };

            SetOptions(options);
            AppLogger.Info("Windows system proxy disabled");
            return new SystemProxyOperationResult(true, "System proxy disabled");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Windows system proxy disable failed: {exception}");
            return new SystemProxyOperationResult(false, exception.Message);
        }
    }

    private SystemProxyOperationResult EnablePac(SystemProxyApplicationRequest request)
    {
        var pacPath = Path.Combine(appDataDirectory, "pac.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(pacPath)!);
        File.WriteAllText(pacPath, request.PacScript ?? string.Empty);
        var pacUrl = $"file:///{pacPath.Replace('\\', '/')}";
        using var pacUrlText = NativeText.From(pacUrl);
        var options = new[]
        {
            // PAC 加 Direct 可在脚本不可用时保留系统备选。
            InternetPerConnOption.Flags(ProxyTypeAutoProxyUrl | ProxyTypeDirect),
            InternetPerConnOption.String(InternetPerConnAutoconfigUrl, pacUrlText.Pointer)
        };

        SetOptions(options);
        AppLogger.Info($"Windows PAC system proxy enabled: {pacUrl}");
        return new SystemProxyOperationResult(true, $"System proxy PAC enabled: {pacUrl}");
    }

    private static void SetOptions(InternetPerConnOption[] options)
    {
        var optionSize = Marshal.SizeOf<InternetPerConnOption>();
        var optionsPointer = IntPtr.Zero;
        try
        {
            optionsPointer = Marshal.AllocHGlobal(optionSize * options.Length);
            for (var index = 0; index < options.Length; index++)
            {
                Marshal.StructureToPtr(options[index], IntPtr.Add(optionsPointer, index * optionSize), false);
            }

            var optionList = new InternetPerConnOptionList
            {
                Size = Marshal.SizeOf<InternetPerConnOptionList>(),

                Connection = IntPtr.Zero,
                OptionCount = options.Length,
                OptionError = 0,
                Options = optionsPointer
            };

            if (!InternetSetOption(IntPtr.Zero, InternetOptionPerConnectionOption, ref optionList, Marshal.SizeOf<InternetPerConnOptionList>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }
        finally
        {
            if (optionsPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(optionsPointer);
            }
        }
    }

    // 托管字符串按 UTF-16 传入 WinINet。
    // 入口固定绑定 W 版本，避免平台默认字符集漂移。
    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "InternetSetOptionW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr internet, uint option, ref InternetPerConnOptionList buffer, int bufferLength);

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "InternetSetOptionW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr internet, uint option, IntPtr buffer, int bufferLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct InternetPerConnOptionList
    {
        public int Size;
        public IntPtr Connection;
        public int OptionCount;
        public int OptionError;
        public IntPtr Options;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InternetPerConnOption
    {
        public uint Option;
        public InternetPerConnOptionValue Value;

        public static InternetPerConnOption Flags(uint flags)
        {
            return new InternetPerConnOption
            {
                Option = InternetPerConnFlags,
                Value = InternetPerConnOptionValue.FromInteger(flags)
            };
        }

        public static InternetPerConnOption String(uint option, IntPtr value)
        {
            return new InternetPerConnOption
            {
                Option = option,
                Value = InternetPerConnOptionValue.FromPointer(value)
            };
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InternetPerConnOptionValue
    {
        [FieldOffset(0)]
        public uint Integer;

        [FieldOffset(0)]
        public IntPtr Pointer;

        public static InternetPerConnOptionValue FromInteger(uint value)
        {
            return new InternetPerConnOptionValue { Integer = value };
        }

        public static InternetPerConnOptionValue FromPointer(IntPtr value)
        {
            return new InternetPerConnOptionValue { Pointer = value };
        }
    }

    private sealed class NativeText : IDisposable
    {
        private NativeText(IntPtr pointer)
        {
            Pointer = pointer;
        }

        public IntPtr Pointer { get; }

        public static NativeText From(string value)
        {
            return new NativeText(Marshal.StringToHGlobalUni(value));
        }

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
            }
        }
    }
}
