using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Stelliberty.Desktop.Services;

internal static class DependencyDirectoryService
{
    private static string DepsDirectory => DesktopApplicationLayout.DepsDirectory;
    private static bool _configured;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Configure();
    }

    public static void Configure()
    {
        if (_configured || !Directory.Exists(DepsDirectory))
        {
            return;
        }

        _configured = true;
        AssemblyLoadContext.Default.Resolving += ResolveManagedAssembly;
    }

    private static Assembly? ResolveManagedAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var assemblyPath = Path.Combine(DepsDirectory, $"{assemblyName.Name}.dll");
        return File.Exists(assemblyPath) ? context.LoadFromAssemblyPath(assemblyPath) : null;
    }
}
