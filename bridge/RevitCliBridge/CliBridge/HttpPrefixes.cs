using System;
using System.Collections.Generic;

namespace RevitCliBridge
{
    /// <summary>
    /// Loopback prefixes the bridge HTTP listener binds, so both host names
    /// answer: <c>http://localhost:{port}/</c> (what the Go client resolves
    /// to) and <c>http://127.0.0.1:{port}/</c> (what callers using the raw
    /// IP expect). Without the second prefix, HTTP.sys answers IP-based
    /// requests with 400 "Invalid Hostname" because the Host header matches
    /// no registered prefix. Both are loopback-only; no external interface
    /// is bound.
    /// </summary>
    public static class HttpPrefixes
    {
        public static IReadOnlyList<string> Compute(int port)
        {
            return new[]
            {
                $"http://localhost:{port}/",
                $"http://127.0.0.1:{port}/"
            };
        }
    }
}
