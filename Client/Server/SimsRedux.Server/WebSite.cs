using System.Text;
using System.Text.RegularExpressions;

namespace SimsRedux.Server
{
    /// <summary>
    /// The website (the game in the browser) served from this PC instead of GitHub Pages.
    ///
    /// The build on this PC (GitHub's "self-hosted runner") publishes the website into the site
    /// folder (server.json "siteFolder"); requests for the website's address (server.json
    /// "siteHosts", normally tsr.mbrew.studio, pointed at this PC's Cloudflare tunnel) get its files.
    /// On this PC it's also at http://localhost:PORT/ for testing.
    ///
    ///   - files are sent compressed when the build made a compressed copy (.br or .gz next to it)
    ///     and the browser can read it
    ///   - files whose names change whenever they do (the game's code, the custom content bundles)
    ///     are kept by browsers for good; everything else is checked each visit (and only sent again
    ///     when it changed)
    ///   - nothing outside the site folder can be reached
    /// </summary>
    public class WebSite
    {
        private readonly ServerConfig Config;

        public WebSite(ServerConfig config) { Config = config; }

        /// <summary>Where the website's files are (the build puts them there).</summary>
        public string Folder
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Config.SiteFolder)) return Path.GetFullPath(Config.SiteFolder.Trim().Trim('"'));
                return OperatingSystem.IsWindows() ? @"C:\SimsRedux\site" : Path.Combine(Program.DataDir, "site");
            }
        }

        public bool Ready => File.Exists(Path.Combine(Folder, "index.html"));

        /// <summary>Is this request for the website (rather than the game server's own services)?</summary>
        public bool Wants(Request req)
        {
            if (req.Method != "GET" && req.Method != "HEAD") return false;
            if (req.Path.StartsWith("/api/") || req.Path == "/healthz" || req.Path == "/lot" || req.Path.StartsWith("/lot/")) return false;
            var host = (req.Header("Host") ?? "").Split(':')[0].Trim().ToLowerInvariant();
            if (Config.SiteHosts != null && Config.SiteHosts.Any(h => string.Equals(h?.Trim(), host, StringComparison.OrdinalIgnoreCase))) return true;
            // on this PC: http://localhost:PORT/ shows the website too (for testing)
            return !req.Public && (host == "localhost" || host == "127.0.0.1") && Ready;
        }

        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8", [".htm"] = "text/html; charset=utf-8",
            [".js"] = "text/javascript; charset=utf-8", [".mjs"] = "text/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8", [".json"] = "application/json; charset=utf-8",
            [".txt"] = "text/plain; charset=utf-8", [".xml"] = "text/xml; charset=utf-8",
            [".wasm"] = "application/wasm", [".dll"] = "application/octet-stream", [".pdb"] = "application/octet-stream",
            [".dat"] = "application/octet-stream", [".blat"] = "application/octet-stream", [".webcil"] = "application/octet-stream",
            [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
            [".bmp"] = "image/bmp", [".ico"] = "image/x-icon", [".svg"] = "image/svg+xml", [".webp"] = "image/webp",
            [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".ttf"] = "font/ttf",
            [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".mp4"] = "video/mp4", [".webm"] = "video/webm",
        };

        // the code's and the custom content's file names carry a fingerprint that changes with them
        private static readonly Regex Fingerprinted = new Regex(@"\.[a-z0-9]{8,}\.(wasm|dll|js|dat|pdb|webcil|blat|json|css)$|\.srxpack\.gz$", RegexOptions.IgnoreCase);

        public async Task Serve(Request req, Stream stream)
        {
            var root = Folder;
            if (!Ready)
            {
                await Plain(stream, 503, "The website isn't on this server yet - it's put here by the build on this PC.");
                return;
            }
            var rel = req.Path.Replace('\\', '/');
            if (rel.Contains('\0')) { await Plain(stream, 400, "Bad request"); return; }
            if (rel.EndsWith("/")) rel += "index.html";
            var full = Path.GetFullPath(Path.Combine(root, rel.TrimStart('/')));
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                await Plain(stream, 404, "Not found");
                return;
            }
            if (Directory.Exists(full)) full = Path.Combine(full, "index.html");
            if (!File.Exists(full))
            {
                await Plain(stream, 404, "Not found");
                return;
            }

            var ext = Path.GetExtension(full);
            var name = Path.GetFileName(full);
            var type = Types.TryGetValue(ext, out var t) ? t : "application/octet-stream";

            // a ready-made compressed copy, if the browser can take it
            var accept = req.Header("Accept-Encoding") ?? "";
            string send = full, encoding = null;
            if (!name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                if (accept.Contains("br") && File.Exists(full + ".br")) { send = full + ".br"; encoding = "br"; }
                else if (accept.Contains("gzip") && File.Exists(full + ".gz")) { send = full + ".gz"; encoding = "gzip"; }
            }
            var info = new FileInfo(send);
            var etag = "\"" + info.Length.ToString("x") + "-" + info.LastWriteTimeUtc.Ticks.ToString("x") + (encoding ?? "") + "\"";

            var headers = new StringBuilder();
            headers.Append("Content-Type: ").Append(type).Append("\r\n");
            if (encoding != null) headers.Append("Content-Encoding: ").Append(encoding).Append("\r\n");
            headers.Append("Vary: Accept-Encoding\r\n");
            headers.Append("ETag: ").Append(etag).Append("\r\n");
            headers.Append("Cache-Control: ").Append(Fingerprinted.IsMatch(name) ? "public, max-age=31536000, immutable" : "no-cache").Append("\r\n");
            headers.Append("X-Content-Type-Options: nosniff\r\n");

            if (req.Header("If-None-Match") == etag)
            {
                await Head(stream, 304, headers.ToString(), -1);
                return;
            }
            await Head(stream, 200, headers.ToString(), info.Length);
            if (req.Method == "HEAD") { await stream.FlushAsync(); return; }
            using (var f = new FileStream(send, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, true))
                await f.CopyToAsync(stream, 128 * 1024);
            await stream.FlushAsync();
        }

        private static async Task Head(Stream stream, int status, string headers, long length)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(status == 304 ? " Not Modified" : status == 200 ? " OK" : " Status").Append("\r\n");
            sb.Append(headers);
            if (length >= 0) sb.Append("Content-Length: ").Append(length).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
            if (status == 304) await stream.FlushAsync();
        }

        private static Task Plain(Stream stream, int status, string text)
        {
            return HttpServer.Send(stream, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text + "\n"));
        }
    }
}
