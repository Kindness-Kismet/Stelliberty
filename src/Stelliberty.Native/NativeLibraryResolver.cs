using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stelliberty.Native.Generated;

namespace Stelliberty.Native;

internal static class NativeLibraryResolver
{
    // 解析器必须在首次 P/Invoke 前注册。
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(Interop).Assembly, Resolve);
    }
#pragma warning restore CA2255

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Interop.NativeLib)
        {
            return nint.Zero;
        }

        // 单文件发布没有程序集目录，改用运行时默认路径。
        var directory = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(directory))
        {
            return nint.Zero;
        }

        var rootPath = Path.Combine(directory, HubFileName);
        if (NativeLibrary.TryLoad(rootPath, out var handle))
        {
            return handle;
        }

        var depsPath = Path.Combine(directory, "data", "deps", HubFileName);
        return NativeLibrary.TryLoad(depsPath, out handle) ? handle : nint.Zero;
    }

    private static string HubFileName =>
        OperatingSystem.IsWindows() ? $"{Interop.NativeLib}.dll"
        : OperatingSystem.IsMacOS() ? $"lib{Interop.NativeLib}.dylib"
        : $"lib{Interop.NativeLib}.so";
}
