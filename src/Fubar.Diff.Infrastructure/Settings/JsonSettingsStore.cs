using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Settings;

namespace Fubar.Diff.Infrastructure.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> over a JSON file in the user's application-data directory.
///
/// Every failure path returns rather than throws. A corrupt or unreadable settings file means the user
/// loses their preferences, which is a nuisance; it must not mean the app refuses to start, and it
/// must not interrupt a comparison they are in the middle of.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;

    /// <summary>Uses the default location under the user's application-data directory.</summary>
    public JsonSettingsStore()
        : this(DefaultPath())
    {
    }

    /// <summary>Uses an explicit path. Exists so tests do not touch the real user profile.</summary>
    public JsonSettingsStore(string path) => _path = path;

    /// <summary>Where the settings live, for diagnostics.</summary>
    public string Path => _path;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Written to be hand-editable: an enum as "Auto" is far more useful in a config file than 0,
        // and a stale name from a future version is handled by the catch below.
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "fubar-diff",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return AppSettings.Default;
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), SerializerOptions)
                   ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Corrupt or unreadable: start clean rather than failing to start at all. The file is left
            // alone so the user can inspect it; the next save overwrites it.
            return AppSettings.Default;
        }
    }

    public async Task<bool> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);

            // Write-then-replace, so an interrupted save cannot leave a half-written file that then
            // fails to parse on next start.
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
