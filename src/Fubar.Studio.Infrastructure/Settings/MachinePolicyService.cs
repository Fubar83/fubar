using System.Text.Json;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Settings;

/// <inheritdoc cref="IMachinePolicyService"/>
/// <remarks>
/// Read ONCE at construction, deliberately. A policy that could change under a running session would
/// mean a capture allowed at the top of a collection run and refused at the bottom, which is harder to
/// explain than either answer on its own. Restarting the app is the documented way to pick up a change.
/// </remarks>
public sealed class MachinePolicyService : IMachinePolicyService
{
    public MachinePolicyService()
        : this(DefaultPath())
    {
    }

    public MachinePolicyService(string path)
    {
        if (!File.Exists(path))
        {
            Current = MachinePolicy.None;
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<MachinePolicy>(File.ReadAllText(path), FubarJson.Options)
                      ?? MachinePolicy.None;
            Source = path;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A malformed policy file must not stop the app starting - refusing to launch over an
            // administrator's typo is a worse failure than the one it would be reporting. It also must
            // not silently apply half of itself, so nothing is applied at all.
            Current = MachinePolicy.None;
            Source = $"{path} (could not be read - no policy applied)";
        }
    }

    public MachinePolicy Current { get; }

    public string? Source { get; }

    /// <summary>
    /// <c>%ProgramData%\Fubar\policy.json</c> on Windows, <c>/etc/fubar/policy.json</c> elsewhere -
    /// both places an administrator can write and an ordinary user cannot.
    /// </summary>
    public static string DefaultPath() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Fubar", "policy.json")
            : "/etc/fubar/policy.json";
}
