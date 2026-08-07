using System.Diagnostics;
using Stelliberty.Tray;
using Xunit;

namespace Stelliberty.Tray.Tests;

public sealed class DesktopUiLauncherTests
{
    [Fact(DisplayName = "Desktop launcher retains the UI process until tray shutdown")]
    public async Task DesktopLauncherRetainsUiProcessUntilTrayShutdown()
    {
        var process = new FakeDesktopUiProcess(101, exitsWhileWaiting: true);
        var starter = new FakeDesktopUiProcessStarter(process);
        await using var launcher = CreateLauncher(starter);

        await launcher.LaunchAsync("session-token", CancellationToken.None);

        Assert.False(process.IsDisposed);
        var startInfo = Assert.Single(starter.StartInfos);
        Assert.Equal("ui-test", startInfo.FileName);
        Assert.Equal(["--ui", "--tray-session", "session-token"], startInfo.ArgumentList);
    }

    [Fact(DisplayName = "Desktop launcher allows graceful UI exit before releasing ownership")]
    public async Task DesktopLauncherAllowsGracefulUiExitBeforeReleasingOwnership()
    {
        var process = new FakeDesktopUiProcess(102, exitsWhileWaiting: true);
        var starter = new FakeDesktopUiProcessStarter(process);
        var launcher = CreateLauncher(starter);
        await launcher.LaunchAsync("session-token", CancellationToken.None);

        await launcher.DisposeAsync();

        Assert.Equal(1, process.WaitCount);
        Assert.Equal(0, process.KillCount);
        Assert.True(process.IsDisposed);
        Assert.True(starter.IsDisposed);
    }

    [Fact(DisplayName = "Desktop launcher terminates UI that outlives the tray shutdown timeout")]
    public async Task DesktopLauncherTerminatesUiAfterShutdownTimeout()
    {
        var process = new FakeDesktopUiProcess(103, exitsWhileWaiting: false);
        var starter = new FakeDesktopUiProcessStarter(process);
        var launcher = CreateLauncher(starter);
        await launcher.LaunchAsync("session-token", CancellationToken.None);

        await launcher.DisposeAsync();

        Assert.Equal(1, process.WaitCount);
        Assert.Equal(1, process.KillCount);
        Assert.True(process.IsDisposed);
    }

    [Fact(DisplayName = "Desktop launcher releases an exited UI process without terminating it")]
    public async Task DesktopLauncherReleasesExitedUiWithoutTerminatingIt()
    {
        var process = new FakeDesktopUiProcess(104, exitsWhileWaiting: true)
        {
            HasExited = true,
        };
        var starter = new FakeDesktopUiProcessStarter(process);
        var launcher = CreateLauncher(starter);
        await launcher.LaunchAsync("session-token", CancellationToken.None);

        await launcher.DisposeAsync();

        Assert.Equal(0, process.WaitCount);
        Assert.Equal(0, process.KillCount);
        Assert.True(process.IsDisposed);
    }

    [Fact(DisplayName = "Desktop launcher cleans the previous UI process before a replacement launch")]
    public async Task DesktopLauncherCleansPreviousUiBeforeReplacementLaunch()
    {
        var first = new FakeDesktopUiProcess(105, exitsWhileWaiting: true);
        var second = new FakeDesktopUiProcess(106, exitsWhileWaiting: true);
        var starter = new FakeDesktopUiProcessStarter(first, second);
        await using var launcher = CreateLauncher(starter);
        await launcher.LaunchAsync("first-token", CancellationToken.None);

        await launcher.LaunchAsync("second-token", CancellationToken.None);

        Assert.True(first.IsDisposed);
        Assert.Equal(0, first.KillCount);
        Assert.False(second.IsDisposed);
        Assert.Equal(2, starter.StartInfos.Count);
    }

    [Fact(DisplayName = "Windows job terminates its assigned UI process when tray ownership ends")]
    public async Task WindowsJobTerminatesAssignedUiWhenTrayOwnershipEnds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var starter = new SystemDesktopUiProcessStarter();
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"))
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("30");
        startInfo.ArgumentList.Add("127.0.0.1");
        using var process = starter.Start(startInfo);
        Assert.False(process.HasExited);

        starter.Dispose();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.HasExited);
    }

    private static DesktopUiLauncher CreateLauncher(FakeDesktopUiProcessStarter starter) =>
        new(starter, () => "ui-test", TimeSpan.FromMilliseconds(10));

    private sealed class FakeDesktopUiProcessStarter(params FakeDesktopUiProcess[] processes) : IDesktopUiProcessStarter
    {
        private readonly Queue<FakeDesktopUiProcess> _processes = new(processes);

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public bool IsDisposed { get; private set; }

        public IDesktopUiProcess Start(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return _processes.Dequeue();
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeDesktopUiProcess(int id, bool exitsWhileWaiting) : IDesktopUiProcess
    {
        public int Id { get; } = id;

        public bool HasExited { get; set; }

        public int WaitCount { get; private set; }

        public int KillCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitCount++;
            if (exitsWhileWaiting)
            {
                HasExited = true;
                return;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Kill()
        {
            KillCount++;
            HasExited = true;
        }

        public void Dispose() => IsDisposed = true;
    }
}
