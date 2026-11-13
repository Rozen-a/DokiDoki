// Shared helpers for WinRT Bluetooth operations and standard heart-rate packets.
using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace DokiDoki.Backend;

internal static class BleMeasurement
{
    /// <summary>Converts a WinRT operation to an awaitable .NET task.</summary>
    /// <typeparam name="T">The operation result type.</typeparam>
    /// <param name="operation">The started Windows Runtime operation.</param>
    /// <returns>A task carrying the operation result.</returns>
    internal static Task<T> WaitForAsync<T>(IAsyncOperation<T> operation) => operation.AsTask();
    /// <summary>Reads the mandatory BPM field from a BLE Heart Rate Measurement packet.</summary>
    /// <param name="bytes">Packet starting with the Heart Rate Measurement flags.</param>
    /// <param name="bpm">Decoded beats per minute when the packet contains a usable reading.</param>
    /// <returns>True when the mandatory field is present and the sensor has not reported losing contact.</returns>
    internal static bool TryDecode(byte[] bytes, out int bpm)
    {
        bpm = 0;
        if (bytes.Length < 2) return false;
        var flags = bytes[0];
        if ((flags & 6) == 4) return false;
        if ((flags & 1) == 0) bpm = bytes[1];
        else if (bytes.Length >= 3) bpm = bytes[1] | (bytes[2] << 8);
        return bpm > 0;
    }
}
