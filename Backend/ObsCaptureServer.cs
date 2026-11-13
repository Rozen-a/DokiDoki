// Serves the OBS overlay and its latest state over the local loopback interface.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DokiDoki.Backend;

/// <summary>Owns a local HTTP endpoint for a single live capture session.</summary>
internal sealed class ObsCaptureServer : IDisposable
{
    internal const string Address = "http://localhost:18765/";
    private readonly HttpListener listener = new();
    private byte[] state = "{}"u8.ToArray();

    /// <summary>Starts serving immutable page and image assets.</summary>
    /// <param name="page">Embedded overlay HTML.</param>
    /// <param name="heart">Embedded pixel-heart PNG.</param>
    internal ObsCaptureServer(byte[] page, byte[] heart)
    {
        listener.Prefixes.Add(Address);
        try { listener.Start(); }
        catch { listener.Close(); throw; }
        _ = ServeAsync(page, heart);
    }

    /// <summary>Publishes a complete JSON snapshot for the next browser poll.</summary>
    /// <param name="json">Serialized display options and readings.</param>
    internal void Update(string json) =>
        System.Threading.Volatile.Write(ref state, Encoding.UTF8.GetBytes(json));

    /// <summary>Answers browser requests until the capture session is closed.</summary>
    /// <param name="page">Overlay HTML bytes.</param>
    /// <param name="heart">Pixel-heart image bytes.</param>
    /// <returns>The server loop task.</returns>
    private async Task ServeAsync(byte[] page, byte[] heart)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }

            try
            {
                using var response = context.Response;
                response.Headers["Cache-Control"] = "no-store";
                var (body, type) = context.Request.Url?.AbsolutePath switch
                {
                    "/" => (page, "text/html; charset=utf-8"),
                    "/heart.png" => (heart, "image/png"),
                    "/state" => (System.Threading.Volatile.Read(ref state), "application/json"),
                    _ => ((byte[])null, "")
                };
                if (body == null || context.Request.HttpMethod != "GET")
                {
                    response.StatusCode = body == null ? 404 : 405;
                    continue;
                }
                response.ContentType = type;
                response.ContentLength64 = body.Length;
                await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Closes the local listening endpoint.</summary>
    public void Dispose() => listener.Close();
}
