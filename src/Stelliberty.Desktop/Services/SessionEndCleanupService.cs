using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Desktop.Services;

// 系统关机/注销时同步清理系统代理，兜底用户未主动退出（如托盘常驻）的场景。
// 关机会强杀进程，异步回调来不及，故清理必须在会话结束消息/信号里同步完成。
public sealed class SessionEndCleanupService(Action cleanup, Action<bool>? shutdownStateChanged = null) : IDisposable
{
    private readonly List<IDisposable> _signalRegistrations = [];
    private WindowsSessionWatcher? _windowsWatcher;
    private bool _isDisposed;

    public void Start()
    {
        if (OperatingSystem.IsWindows())
        {
            _windowsWatcher = WindowsSessionWatcher.Create(RunCleanup, SetShutdownDetected);
            return;
        }

        // macOS/Linux 注销或关机前发送 SIGTERM，进程仍可同步清理。
        RegisterPosixSignal(PosixSignal.SIGTERM);
        RegisterPosixSignal(PosixSignal.SIGINT);
        RegisterPosixSignal(PosixSignal.SIGQUIT);
    }

    private void RegisterPosixSignal(PosixSignal signal)
    {
        try
        {
            _signalRegistrations.Add(PosixSignalRegistration.Create(signal, _ => RunCleanup()));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Session-end signal registration failed ({signal}): {exception.Message}");
        }
    }

    private void RunCleanup()
    {
        SetShutdownDetected(true);
        AppLogger.Info("Session-end cleanup started");
        try
        {
            cleanup();
            AppLogger.Info("Session-end cleanup completed");
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Session-end cleanup failed: {exception.Message}");
        }
    }

    private void SetShutdownDetected(bool isDetected)
    {
        shutdownStateChanged?.Invoke(isDetected);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (OperatingSystem.IsWindows())
        {
            _windowsWatcher?.Dispose();
        }

        foreach (var registration in _signalRegistrations)
        {
            registration.Dispose();
        }

        _signalRegistrations.Clear();
    }

    // 隐藏顶层窗口接收会话广播；托盘/静默启动时主窗口无句柄，故不能挂主窗口。
    // message-only 窗口收不到 WM_ENDSESSION，必须用普通顶层窗口。
    [SupportedOSPlatform("windows")]
    private sealed class WindowsSessionWatcher : IDisposable
    {
        private const uint WmQueryEndSession = 0x0011;
        private const uint WmEndSession = 0x0016;

        private readonly Action _cleanup;
        private readonly Action<bool> _shutdownStateChanged;
        private readonly WndProc _wndProc; // 保持委托引用，防 GC 回收后回调悬空
        private readonly string _className;
        private readonly IntPtr _instance;
        private IntPtr _hwnd;
        private ushort _classAtom;

        private WindowsSessionWatcher(Action cleanup, Action<bool> shutdownStateChanged)
        {
            _cleanup = cleanup;
            _shutdownStateChanged = shutdownStateChanged;
            _wndProc = HandleMessage;
            _className = $"StellibertySessionWatcher_{Environment.ProcessId}";
            _instance = GetModuleHandle(null);
        }

        public static WindowsSessionWatcher? Create(Action cleanup, Action<bool> shutdownStateChanged)
        {
            var watcher = new WindowsSessionWatcher(cleanup, shutdownStateChanged);
            return watcher.Initialize() ? watcher : null;
        }

        private bool Initialize()
        {
            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = _instance,
                lpszClassName = _className
            };

            _classAtom = RegisterClassEx(ref windowClass);
            if (_classAtom == 0)
            {
                AppLogger.Warning($"Session watcher class registration failed: {Marshal.GetLastWin32Error()}");
                return false;
            }

            _hwnd = CreateWindowEx(0, _className, "Stelliberty Session Watcher", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                AppLogger.Warning($"Session watcher window creation failed: {Marshal.GetLastWin32Error()}");
                UnregisterClass(_className, _instance);
                _classAtom = 0;
                return false;
            }

            return true;
        }

        private IntPtr HandleMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmEndSession)
            {
                if (wParam != IntPtr.Zero)
                {
                    AppLogger.Info("WM_ENDSESSION confirmed");
                    _cleanup();
                }
                else
                {
                    _shutdownStateChanged(false);
                    AppLogger.Info("WM_ENDSESSION canceled");
                }
                return IntPtr.Zero;
            }

            if (msg == WmQueryEndSession)
            {
                _shutdownStateChanged(true);
                AppLogger.Info("WM_QUERYENDSESSION accepted");
                return 1; // 允许会话结束
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            if (_classAtom != 0)
            {
                UnregisterClass(_className, _instance);
                _classAtom = 0;
            }
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "DestroyWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string? moduleName);
    }
}
