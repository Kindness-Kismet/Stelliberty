using System;
using System.Threading.Tasks;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Presentation.Dialogs;

namespace Stelliberty.Presentation.ViewModels;

internal sealed class DialogCloseResetScheduler
{
    private int _revision;

    public void Cancel()
    {
        _revision++;
    }

    // async void 异常会逃逸到 UI 调度器；委托抛错必须就地备选处理并记录。
    public async void Run(Func<bool> shouldReset, Action reset)
    {
        var revision = ++_revision;
        try
        {
            await Task.Delay(DialogTiming.StateResetDelay);
            if (revision == _revision && shouldReset())
            {
                reset();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Dialog close reset failed");
        }
    }
}
