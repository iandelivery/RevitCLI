using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RevitCliBridge.Models;

namespace RevitCliBridge
{
    /// <summary>
    /// Shared HTTP/SSE response writers used by the server and the SSE
    /// execute handler.
    /// </summary>
    public static class HttpIo
    {
        public static async Task WriteJsonResponseAsync(HttpListenerResponse response, object data, string contentType = "application/json")
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            if (response.StatusCode == 0)
                response.StatusCode = 200;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        public static async Task WriteJsonResponseAsync(HttpListenerResponse response, string json, string contentType = "application/json")
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            if (response.StatusCode == 0)
                response.StatusCode = 200;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Write event frame to SSE stream.
        /// Format: "event: {name}\ndata: {json}\n\n"
        /// </summary>
        public static async Task WriteSseEventAsync(Stream outputStream, string eventName, string dataJson)
        {
            var frame = $"event: {eventName}\ndata: {dataJson}\n\n";
            var buffer = Encoding.UTF8.GetBytes(frame);
            await outputStream.WriteAsync(buffer, 0, buffer.Length);
            await outputStream.FlushAsync();
        }

        /// <summary>
        /// Reads the request body, answering 413 when it exceeds the
        /// configured maximum. Returns empty string after writing the 413 —
        /// callers must check response.StatusCode.
        /// </summary>
        public static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            long maxBytes = CliBridgeConfigLoader.Config.MaxRequestBodySizeBytes;
            if (request.ContentLength64 > maxBytes)
            {
                response.StatusCode = 413;
                await WriteJsonResponseAsync(response, new { error = "Request body too large." });
                return string.Empty;
            }

            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }
    }
}
