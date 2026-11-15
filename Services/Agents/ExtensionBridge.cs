using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

/// <summary>
/// The door for the browser extension (extension/ in this repository), on the same localhost listener as the
/// Claude Code bridge but a separate route (/ext/*) and a separate secret.
/// The Claude Code bridge refuses anything that carries an Origin header (a web page); the extension always does,
/// so it gets its own checks: it is OFF until the user switches it on, the Origin must be a browser extension
/// (a web page can never send that), the secret from Settings must match, CORS answers go only to that Origin,
/// and bodies are small. Routes: /ext/ping, /ext/note, /ext/check.
/// </summary>
public sealed class ExtensionBridge
{
    private const long MaxBody = 100_000;
    private static readonly Regex ExtensionOrigin = new(@"^(chrome|moz)-extension://[a-z0-9\-]{8,64}$", RegexOptions.Compiled);

    /// <summary>Set by the window: is the extension switched on in Settings?</summary>
    public Func<bool> IsEnabled { get; set; } = () => false;

    public sealed record ExtNote(string Text, string Title, string? Url);
    public sealed record ExtCheck(int Issues, string Lang);

    /// <summary>Raised on a background thread.</summary>
    public event Action<ExtNote>? NoteReceived;
    public event Action<ExtCheck>? CheckReceived;

    // ------------------------------------------------------------------ the secret

    public static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "ext.token");

    private static readonly object TokenLock = new();
    private static string? _token;

    /// <summary>The key the user pastes into the extension (created on first use, readable only by this Windows user).</summary>
    public static string Token()
    {
        lock (TokenLock)
        {
            if (_token != null) return _token;
            try
            {
                if (File.Exists(TokenPath))
                {
                    var existing = File.ReadAllText(TokenPath).Trim();
                    if (existing.Length >= 32) return _token = existing;
                }
            }
            catch { /* make a new one */ }
            return _token = WriteFresh();
        }
    }

    /// <summary>A new key: the old one stops working at once (the extension has to be paired again).</summary>
    public static string RotateToken()
    {
        lock (TokenLock) return _token = WriteFresh();
    }

    private static string WriteFresh()
    {
        var fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            File.WriteAllText(TokenPath, fresh);
        }
        catch { /* in-memory only: the user can't read it, so the door stays closed */ }
        return fresh;
    }

    // ------------------------------------------------------------------ requests

    public static bool IsExtensionOrigin(string? origin) => origin != null && origin.Length <= 100 && ExtensionOrigin.IsMatch(origin);

    public async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var origin = req.Headers["Origin"];
        if (!IsExtensionOrigin(origin)) { Reply(res, 403, null, null); return; }   // a web page, a script: not us

        // CORS: answer only this extension (the browser reads the answer, a web page never gets it)
        if (req.HttpMethod == "OPTIONS") { Reply(res, 204, origin, null); return; }

        if (!IsEnabled()) { Reply(res, 403, origin, """{"error":"disabled"}"""); return; }
        var sent = Encoding.ASCII.GetBytes(req.Headers["X-Island-Ext-Token"] ?? "");
        var ok = req.HttpMethod == "POST" && req.IsLocal
                 && (req.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                 && req.ContentLength64 is >= 0 and <= MaxBody
                 && CryptographicOperations.FixedTimeEquals(sent, Encoding.ASCII.GetBytes(Token()));
        if (!ok) { Reply(res, 401, origin, """{"error":"unauthorized"}"""); return; }

        JsonObject json;
        try
        {
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            json = JsonNode.Parse(await reader.ReadToEndAsync()) as JsonObject ?? new JsonObject();
        }
        catch { Reply(res, 400, origin, """{"error":"bad json"}"""); return; }

        switch (req.Url?.AbsolutePath)
        {
            case "/ext/ping":
                Reply(res, 200, origin, """{"ok":true,"app":"Atoll"}""");
                return;
            case "/ext/note":
            {
                var text = Str(json, "text", 20000)?.Trim();
                if (string.IsNullOrEmpty(text)) { Reply(res, 400, origin, """{"error":"empty"}"""); return; }
                var url = Str(json, "url", 500);
                NoteReceived?.Invoke(new ExtNote(text, Str(json, "title", 200) ?? "", SafeShell.IsWebUrl(url) ? url : null));
                Reply(res, 200, origin, """{"ok":true}""");
                return;
            }
            case "/ext/check":
            {
                var issues = json["issues"] is JsonValue v && v.TryGetValue<int>(out var n) ? Math.Clamp(n, 0, 9999) : 0;
                CheckReceived?.Invoke(new ExtCheck(issues, Str(json, "lang", 20) ?? ""));
                Reply(res, 200, origin, """{"ok":true}""");
                return;
            }
            default:
                Reply(res, 404, origin, """{"error":"not found"}""");
                return;
        }
    }

    private static string? Str(JsonObject o, string key, int max) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? (s.Length > max ? s[..max] : s) : null;

    private static void Reply(HttpListenerResponse res, int status, string? corsOrigin, string? body)
    {
        try
        {
            res.StatusCode = status;
            if (corsOrigin != null)
            {
                res.Headers["Access-Control-Allow-Origin"] = corsOrigin;
                res.Headers["Vary"] = "Origin";
                res.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
                res.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Island-Ext-Token";
                res.Headers["Access-Control-Allow-Private-Network"] = "true";
                res.Headers["Access-Control-Max-Age"] = "600";
            }
            var bytes = body == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);
            if (bytes.Length > 0) res.ContentType = "application/json";
            res.ContentLength64 = bytes.Length;
            if (bytes.Length > 0) res.OutputStream.Write(bytes);
            res.Close();
        }
        catch { /* the browser went away */ }
    }
}
