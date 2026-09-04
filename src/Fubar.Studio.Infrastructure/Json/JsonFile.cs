using System.Text.Json;

namespace Fubar.Studio.Infrastructure.Json;

/// <summary>
/// Writes a Fubar document without ever leaving a truncated one behind.
///
/// <para>Every save used to be <c>File.Create</c> followed by <c>SerializeAsync</c>, which destroys the
/// old file before it has written the new one. A crash, a full disk, or two windows saving the same
/// request leaves a zero-length or half-written <c>request.json</c> where a valid one used to be - and
/// these are the user's committed source files, not scratch state.</para>
///
/// <para>Serialise to a temporary file <b>in the same directory</b>, flush it, then move it over the
/// target. Same directory matters: a move across volumes is a copy and a delete, which is exactly the
/// non-atomic thing this exists to avoid.</para>
/// </summary>
public static class JsonFile
{
    public static async Task WriteAtomicAsync<T>(
        string path, T value, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{path}.{Environment.ProcessId:x}.tmp";

        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken);
                // Explicit: the move must not overtake the bytes.
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // Leave no litter behind on the way out. Best effort - the original file is untouched
            // either way, which is the guarantee that matters.
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    /// <summary>Synchronous counterpart, for the two call sites that have no async context.</summary>
    public static void WriteAtomic<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{path}.{Environment.ProcessId:x}.tmp";

        try
        {
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush();
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }
}
