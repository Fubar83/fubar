using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> to <c>%AppData%/Fubar/settings.json</c> (or the platform
/// equivalent, via <see cref="Environment.SpecialFolder.ApplicationData"/>) - global user
/// preferences that apply across every workspace, as opposed to a workspace's own <c>fubar.json</c>.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fubar", "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, FubarJson.Options, cancellationToken)
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupt/partial settings file - fall back to defaults rather than blocking startup.
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, FubarJson.Options, cancellationToken);
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(stream, FubarJson.Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
