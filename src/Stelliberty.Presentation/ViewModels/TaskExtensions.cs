using System;
using System.Threading.Tasks;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Presentation.ViewModels;

internal static class TaskExtensions
{
    // 丢弃式异步调用的统一异常边界：异常不逃逸为未观察任务异常，并带操作名进日志。
    public static async void SafeFireAndForget(this Task task, string operation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 主动取消属正常流程，不记录。
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, $"Fire-and-forget failed: {operation}");
        }
    }
}
