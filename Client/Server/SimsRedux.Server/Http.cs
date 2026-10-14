using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SimsRedux.Server
{
    /// <summary>One HTTP request, read off the connection by hand (no web framework needed).</summary>
    public class Request
    {
        public string Method, Path, Query, RawTarget, Version;
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = Array.Empty<byte>();
        /// <summary>The player's address: the real one Cloudflare passes along when it comes through the tunnel.</summary>
        public string From;
        /// <summary>Everything read from the connection so far (for passing a connection on unchanged).</summary>
        public byte[] Raw;

        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

        public string QueryValue(string name)
        {
            foreach (var part in (Query ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = part.IndexOf('=');
                var k = Uri.UnescapeDataString((i < 0 ? part : part.Substring(0, i)).Replace('+', ' '));
                if (k == name) return i < 0 ? "" : Uri.UnescapeDataString(part.Substring(i + 1).Replace('+', ' '));
            }
            return null;
        }

        public string BearerToken()
        {
            var h = Header("Authorization");
            if (h != null && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return h.Substring(7).Trim();
            return null;
        }

        public JsonElement? JsonBody()
        {
            try { return JsonDocument.Parse(Body).RootElement; } catch { return null; }
        }
    }

    /// <summary>Counts attempts per address (or account) so password guessing and spam get slowed right down.</summary>
    public class RateLimiter
    {
        private readonly int Max;
        private readonly TimeSpan Window;
        private readonly ConcurrentDictionary<string, Queue<DateTime>> Hits = new ConcurrentDictionary<string, Queue<DateTime>>();

        public RateLimiter(int max, TimeSpan window) { Max = max; Window = window; }

        /// <summary>Records an attempt. False if this key has used up its attempts for now.</summary>
        public bool Allow(string key)
        {
            var now = DateTime.UtcNow;
            var q = Hits.GetOrAdd(key ?? "?", _ => new Queue<DateTime>());
            lock (q)
            {
                while (q.Count > 0 && now - q.Peek() > Window) q.Dequeue();
                if (q.Count >= Max) return false;
                q.Enqueue(now);
                return true;
            }
        }

        public void Tidy()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in Hits)
                lock (kv.Value) { if (kv.Value.Count == 0 || now - kv.Value.Last() > Window) Hits.TryRemove(kv.Key, out _); }
        }
    }

    /// <summary>
    /// The server's front door: plain TCP, with HTTP read by hand. Serves the account and game-file
    /// services, the health check the panel reads, and passes online-lot connections along.
    /// Runs without admin rights and needs nothing installed beyond .NET.
    /// </summary>
    public class HttpServer
    {
        private const int MaxHeader = 16 * 1024, MaxBody = 64 * 1024, MaxOpen = 400;
        private TcpListener Listener;
        private readonly Func<Request, Stream, TcpClient, Task> Handle;
        private readonly CancellationTokenSource Stop = new CancellationTokenSource();
        public int Open;

        public HttpServer(Func<Request, Stream, TcpClient, Task> handle) { Handle = handle; }

        public bool Start(string bind, int port, out string error)
        {
            error = null;
            try
            {
                var ip = bind == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(bind);
                Listener = new TcpListener(ip, port);
                // never quietly share the port with the game hosting a lot (Windows allows that otherwise)
                if (OperatingSystem.IsWindows()) Listener.ExclusiveAddressUse = true;
                Listener.Start();
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                error = "port " + port + " is already in use - is another server (or the game, hosting online) running?";
                return false;
            }
            catch (Exception e) { error = e.Message; return false; }
            _ = AcceptLoop();
            return true;
        }

        public void Shutdown()
        {
            Stop.Cancel();
            try { Listener?.Stop(); } catch { }
        }

        private async Task AcceptLoop()
        {
            while (!Stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await Listener.AcceptTcpClientAsync(Stop.Token); }
                catch { break; }
                if (Volatile.Read(ref Open) >= MaxOpen) { try { client.Close(); } catch { } continue; } // too busy: drop it
                _ = Serve(client);
            }
        }

        private async Task Serve(TcpClient client)
        {
            Interlocked.Increment(ref Open);
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var req = await ReadRequest(stream, client);
                if (req == null) return;
                await Handle(req, stream, client);
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Log.Warn("request failed: " + e.Message); }
            finally
            {
                Interlocked.Decrement(ref Open);
                try { client.Close(); } catch { }
            }
        }

        private static async Task<Request> ReadRequest(Stream stream, TcpClient client)
        {
            var buf = new byte[MaxHeader + MaxBody];
            int len = 0, headerEnd = -1;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (headerEnd < 0)
            {
                if (len >= MaxHeader) return null;
                int n = await stream.ReadAsync(buf.AsMemory(len, MaxHeader - len), cts.Token);
                if (n <= 0) return null;
                len += n;
                headerEnd = IndexOf(buf, len, "\r\n\r\n"u8);
            }
            var head = Encoding.ASCII.GetString(buf, 0, headerEnd).Split("\r\n");
            var first = head[0].Split(' ');
            if (first.Length < 3) return null;
            var req = new Request { Method = first[0].ToUpperInvariant(), RawTarget = first[1], Version = first[2] };
            var q = req.RawTarget.IndexOf('?');
            req.Path = Uri.UnescapeDataString(q < 0 ? req.RawTarget : req.RawTarget.Substring(0, q));
            req.Query = q < 0 ? "" : req.RawTarget.Substring(q + 1);
            for (int i = 1; i < head.Length; i++)
            {
                var c = head[i].IndexOf(':');
                if (c > 0) req.Headers[head[i].Substring(0, c).Trim()] = head[i].Substring(c + 1).Trim();
            }

            int bodyStart = headerEnd + 4;
            if ((req.Header("Transfer-Encoding") ?? "").IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Cloudflare can pass a request body along in chunks instead of saying its length
                var body = await ReadChunked(stream, buf, bodyStart, len, cts.Token);
                if (body == null) return null;
                req.Body = body;
                req.Raw = buf.AsSpan(0, bodyStart).ToArray();
            }
            else
            {
                int.TryParse(req.Header("Content-Length") ?? "0", out var contentLength);
                if (contentLength < 0 || contentLength > MaxBody) return null;
                while (len - bodyStart < contentLength)
                {
                    int n = await stream.ReadAsync(buf.AsMemory(len, bodyStart + contentLength - len), cts.Token);
                    if (n <= 0) return null;
                    len += n;
                }
                req.Body = buf.AsSpan(bodyStart, Math.Min(contentLength, len - bodyStart)).ToArray();
                req.Raw = buf.AsSpan(0, len).ToArray();
            }

            // the real address: Cloudflare's tunnel connects from this PC and says who it's for
            var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
            req.From = remote?.ToString() ?? "?";
            if (remote != null && IPAddress.IsLoopback(remote))
            {
                var cf = req.Header("CF-Connecting-IP");
                if (!string.IsNullOrEmpty(cf) && cf.Length < 64) req.From = cf;
            }
            return req;
        }

        /// <summary>Reads a "chunked" body: [size in hex]\r\n[data]\r\n ... 0\r\n\r\n. Null if it's too big or broken.</summary>
        private static async Task<byte[]> ReadChunked(Stream stream, byte[] buf, int start, int len, CancellationToken ct)
        {
            var pending = new MemoryStream();
            pending.Write(buf, start, len - start);
            var chunk = new byte[8192];
            var body = new MemoryStream();
            long pos = 0;
            while (true)
            {
                // a size line
                var data = pending.GetBuffer();
                int lineEnd;
                while ((lineEnd = data.AsSpan((int)pos, (int)(pending.Length - pos)).IndexOf("\r\n"u8)) < 0)
                {
                    if (pending.Length - pos > 64) return null;
                    int n = await stream.ReadAsync(chunk, ct);
                    if (n <= 0) return null;
                    pending.Write(chunk, 0, n);
                    data = pending.GetBuffer();
                }
                var sizeText = System.Text.Encoding.ASCII.GetString(data, (int)pos, lineEnd).Split(';')[0].Trim();
                if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size < 0) return null;
                pos += lineEnd + 2;
                if (size == 0) return body.ToArray();
                if (body.Length + size > MaxBody) return null;
                // the chunk and its \r\n
                while (pending.Length - pos < size + 2)
                {
                    int n = await stream.ReadAsync(chunk, ct);
                    if (n <= 0) return null;
                    pending.Write(chunk, 0, n);
                }
                body.Write(pending.GetBuffer(), (int)pos, size);
                pos += size + 2;
            }
        }

        private static int IndexOf(byte[] buf, int len, ReadOnlySpan<byte> what)
        {
            var i = buf.AsSpan(0, len).IndexOf(what);
            return i;
        }

        // ---------- answers ----------

        public static async Task Send(Stream stream, int status, string contentType, byte[] body, Dictionary<string, string> headers = null)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("X-Content-Type-Options: nosniff\r\n");
            sb.Append("Connection: close\r\n");
            if (headers != null) foreach (var h in headers) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
            sb.Append("\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(head);
            if (body != null && body.Length > 0) await stream.WriteAsync(body);
            await stream.FlushAsync();
        }

        public static Task SendJson(Stream stream, int status, object value, Dictionary<string, string> headers = null)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
            return Send(stream, status, "application/json; charset=utf-8", body, headers);
        }

        public static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static async Task SendFile(Stream stream, string path, long size, Dictionary<string, string> headers)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n");
            sb.Append("Content-Length: ").Append(size).Append("\r\n");
            sb.Append("Cache-Control: private, max-age=0, no-store\r\nConnection: close\r\n");
            foreach (var h in headers) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
            sb.Append("\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true))
                await f.CopyToAsync(stream, 81920);
            await stream.FlushAsync();
        }

        private static string Reason(int status) => status switch
        {
            200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden",
            404 => "Not Found", 405 => "Method Not Allowed", 409 => "Conflict", 429 => "Too Many Requests",
            500 => "Internal Server Error", 502 => "Bad Gateway", 503 => "Service Unavailable", _ => "Status"
        };
    }
}
