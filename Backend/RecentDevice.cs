// Stores one previously connected BLE address locally for automatic reconnect.
using System;
using System.Globalization;
using System.IO;

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

    /// <summary>Saves the two connection preferences locally.</summary>
    /// <param name="startup">Whether to connect on launch.</param>
    /// <param name="reconnect">Whether to recover an interrupted connection.</param>
    internal static void SaveOptions(bool startup, bool reconnect) =>
        Write(PreferencesPath, (startup ? "1" : "0") + (reconnect ? "1" : "0"));

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
