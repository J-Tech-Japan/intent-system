using System.Reflection;

namespace IntentSystem.Cli.Commands;

internal static class DirectRunHelperHostResolver
{
    public static string ResolveExecutablePath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            throw new InvalidOperationException("Could not resolve the IntentSystem CLI assembly path for the direct run helper.");
        }

        var appHostPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath)
                ?? throw new InvalidOperationException("IntentSystem CLI assembly path did not contain a directory."),
            Path.GetFileNameWithoutExtension(assemblyPath) + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

        if (File.Exists(appHostPath))
        {
            return appHostPath;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && Path.IsPathRooted(processPath)
            && File.Exists(processPath)
            && string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(dotnetHostPath)
            && Path.IsPathRooted(dotnetHostPath)
            && File.Exists(dotnetHostPath)
            && string.Equals(Path.GetFileNameWithoutExtension(dotnetHostPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return dotnetHostPath;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
                if (Path.IsPathRooted(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("Could not resolve a runnable host for the IntentSystem CLI direct run helper.");
    }
}
