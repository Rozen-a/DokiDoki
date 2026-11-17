// Stores one previously connected BLE address locally for automatic reconnect.
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DokiDoki.Backend;

/// <summary>Reads and replaces the single remembered address.</summary>
internal static class RecentDevice
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DokiDoki", "recent-device.txt");

    /// <summary>Reads the last saved address when available.</summary>
    /// <returns>The address, or null if no usable entry exists.</returns>
    internal static ulong? Load()
    {
        try
        {
            return ulong.TryParse(File.ReadAllText(FilePath), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out var address) ? address : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Replaces the previous entry only after a successful connection.</summary>
    /// <param name="address">The connected device address.</param>
    internal static void Save(ulong address) => Write(FilePath, address.ToString("X12", CultureInfo.InvariantCulture));

    private static readonly string PreferencesPath = Path.Combine(Path.GetDirectoryName(FilePath), "connection-options.txt");

    /// <summary>Reads connection preferences, defaulting to enabled.</summary>
    /// <returns>The startup and reconnect switches.</returns>
    internal static (bool Startup, bool Reconnect) LoadOptions()
    {
        try
        {
            var options = File.ReadAllText(PreferencesPath);
            return (options.Length < 1 || options[0] != '0', options.Length < 2 || options[1] != '0');
        }
        catch (IOException) { return (true, true); }
        catch (UnauthorizedAccessException) { return (true, true); }
    }

    private static readonly string SettingsPath = Path.Combine(Path.GetDirectoryName(FilePath), "settings.json");

    /// <summary>Reads the saved settings or falls back to the supplied defaults.</summary>
    /// <typeparam name="T">The settings snapshot type.</typeparam>
    /// <param name="fallback">Defaults, including any migrated connection preferences.</param>
    /// <returns>The saved or default snapshot.</returns>
    internal static T LoadSettings<T>(T fallback) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(SettingsPath)) ?? fallback; }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
        catch (JsonException) { return fallback; }
    }

    /// <summary>Saves the complete settings snapshot locally.</summary>
    /// <typeparam name="T">The settings snapshot type.</typeparam>
    /// <param name="settings">The latest settings.</param>
    internal static void SaveSettings<T>(T settings) => Write(SettingsPath, JsonSerializer.Serialize(settings));

    /// <summary>Atomically replaces one local settings file.</summary>
    /// <param name="path">The destination file.</param>
    /// <param name="value">The settings text.</param>
    private static void Write(string path, string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var pending = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(pending, value);
                File.Move(pending, path, overwrite: true);
            }
            finally { File.Delete(pending); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
