using Fubar.Studio.Core.Settings;

namespace Fubar.Studio.Infrastructure.Settings;

/// <summary>No policy in force - the null object for callers that construct a service directly.</summary>
public sealed class NoPolicy : IMachinePolicyService
{
    public static NoPolicy Instance { get; } = new();

    public MachinePolicy Current => MachinePolicy.None;

    public string? Source => null;
}
