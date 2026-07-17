using System;

namespace Stelliberty.Presentation.Dialogs;

public static class DialogTiming
{
    public static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(150);

    // 多保留一帧，确保退出动画完成后再清理临时状态。
    public static readonly TimeSpan StateResetDelay = ExitDuration + TimeSpan.FromMilliseconds(20);
}
