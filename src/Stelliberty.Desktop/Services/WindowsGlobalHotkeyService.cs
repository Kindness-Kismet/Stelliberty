using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyId = 1;
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const long ActivationCooldownMilliseconds = 500;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Action _activated;
    private readonly WndProc _wndProc; // 保持委托引用，防 GC 回收后回调悬空。
    private readonly string _className;
    private readonly IntPtr _instance;
    private IntPtr _windowHandle;
    private ushort _classAtom;
    private ParsedHotkey? _registeredHotkey;
    private long _lastActivationAt;

    public WindowsGlobalHotkeyService(Action activated)
    {
        _activated = activated;
        _wndProc = HandleMessage;
        _className = $"StellibertyGlobalHotkey_{Environment.ProcessId}";
        _instance = GetModuleHandle(null);
        Initialize();
    }

    public GlobalHotkeyApplyResult Apply(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            UnregisterCurrentHotkey();
            return GlobalHotkeyApplyResult.Success();
        }

        if (_windowHandle == IntPtr.Zero || !TryParse(gesture, out var hotkey))
        {
            return GlobalHotkeyApplyResult.Failure(
                _windowHandle == IntPtr.Zero ? GlobalHotkeyApplyError.Failed : GlobalHotkeyApplyError.Invalid);
        }

        if (_registeredHotkey == hotkey)
        {
            return GlobalHotkeyApplyResult.Success();
        }

        var previous = _registeredHotkey;
        UnregisterCurrentHotkey();
        if (RegisterHotKey(_windowHandle, HotkeyId, hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey))
        {
            _registeredHotkey = hotkey;
            AppLogger.Info($"Global window hotkey registered: {gesture}");
            return GlobalHotkeyApplyResult.Success();
        }

        var error = Marshal.GetLastWin32Error();
        RestorePreviousHotkey(previous);
        AppLogger.Warning($"Global window hotkey registration failed: error={error}");
        return GlobalHotkeyApplyResult.Failure(
            error == ErrorHotkeyAlreadyRegistered ? GlobalHotkeyApplyError.Conflict : GlobalHotkeyApplyError.Failed);
    }

    public void Dispose()
    {
        UnregisterCurrentHotkey();
        if (_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_classAtom != 0)
        {
            UnregisterClass(_className, _instance);
            _classAtom = 0;
        }
    }

    private void Initialize()
    {
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };

        _classAtom = RegisterClassEx(ref windowClass);
        if (_classAtom == 0)
        {
            AppLogger.Warning($"Global hotkey class registration failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        _windowHandle = CreateWindowEx(
            0,
            _className,
            "Stelliberty Global Hotkey",
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            _instance,
            IntPtr.Zero);
        if (_windowHandle == IntPtr.Zero)
        {
            AppLogger.Warning($"Global hotkey window creation failed: {Marshal.GetLastWin32Error()}");
            UnregisterClass(_className, _instance);
            _classAtom = 0;
        }
    }

    private IntPtr HandleMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey && wParam == new IntPtr(HotkeyId))
        {
            var now = Environment.TickCount64;
            if (now - _lastActivationAt < ActivationCooldownMilliseconds)
            {
                return IntPtr.Zero;
            }

            _lastActivationAt = now;
            _activated();
            return IntPtr.Zero;
        }

        return DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private void UnregisterCurrentHotkey()
    {
        if (_windowHandle != IntPtr.Zero && _registeredHotkey is not null)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _registeredHotkey = null;
        }
    }

    private void RestorePreviousHotkey(ParsedHotkey? previous)
    {
        if (previous is null || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (RegisterHotKey(_windowHandle, HotkeyId, previous.Value.Modifiers | ModNoRepeat, previous.Value.VirtualKey))
        {
            _registeredHotkey = previous;
        }
    }

    private static bool TryParse(string gesture, out ParsedHotkey hotkey)
    {
        var tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = 0u;
        var virtualKey = 0u;
        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": modifiers |= ModWin; break;
                default:
                    if (virtualKey != 0 || !TryVirtualKey(token, out virtualKey))
                    {
                        hotkey = default;
                        return false;
                    }
                    break;
            }
        }

        hotkey = new ParsedHotkey(modifiers, virtualKey);
        return modifiers != 0 && virtualKey != 0;
    }

    private static bool TryVirtualKey(string token, out uint virtualKey)
    {
        if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0]))
        {
            virtualKey = char.ToUpperInvariant(token[0]);
            return true;
        }

        if (token.Length is 2 or 3
            && token[0] is 'F' or 'f'
            && int.TryParse(token[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = token.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "tab" => 0x09,
            "enter" => 0x0D,
            "escape" => 0x1B,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "end" => 0x23,
            "home" => 0x24,
            "insert" => 0x2D,
            "delete" => 0x2E,
            _ => 0,
        };
        return virtualKey != 0;
    }

    private readonly record struct ParsedHotkey(uint Modifiers, uint VirtualKey);

    private delegate IntPtr WndProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
