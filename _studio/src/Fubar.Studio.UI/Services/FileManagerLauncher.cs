using System.Diagnostics;

namespace Fubar.Studio.UI.Services;

/// <summary>Opens the host OS's file manager, selecting the given path where the platform supports it.</summary>
public static class FileManagerLauncher
{
    public static void Reveal(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"") { UseShellExecute = true });
        }
        else
        {
            // xdg-open has no "select this file" concept - open the containing directory instead.
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{directory}\"") { UseShellExecute = true });
        }
    }
}
