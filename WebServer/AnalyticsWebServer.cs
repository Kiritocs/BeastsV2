using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeastsV2;

internal sealed class AnalyticsWebServer
{
    private static readonly byte[] DefaultBeastIconPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9h0AAAAASUVORK5CYII=");

    private readonly Func<SessionCurrentResponseV2> _currentSessionProvider;
    private readonly Func<int, int, MapListResponseV2> _mapPageProvider;
    private readonly Func<IReadOnlyList<SessionSaveListItemV2>> _listSaves;
    private readonly Func<CreateSessionSaveRequestV2, ApiActionResponseV2> _createSave;
    private readonly Func<string, SessionSaveDetailV2> _getSave;
    private readonly Func<string, ApiActionResponseV2> _loadSave;
    private readonly Func<string, ApiActionResponseV2> _unloadSave;
    private readonly Func<string, ApiActionResponseV2> _deleteSave;
    private readonly Func<CompareSessionsRequestV2, CompareSessionsResponseV2> _compareSessions;
    private readonly Action<string> _log;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private Task _listenTask;
    private int _port;
    private bool _allowNetworkAccess;

    public AnalyticsWebServer(
        Func<SessionCurrentResponseV2> currentSessionProvider,
        Func<int, int, MapListResponseV2> mapPageProvider,
        Func<IReadOnlyList<SessionSaveListItemV2>> listSaves,
        Func<CreateSessionSaveRequestV2, ApiActionResponseV2> createSave,
        Func<string, SessionSaveDetailV2> getSave,
        Func<string, ApiActionResponseV2> loadSave,
        Func<string, ApiActionResponseV2> unloadSave,
        Func<string, ApiActionResponseV2> deleteSave,
        Func<CompareSessionsRequestV2, CompareSessionsResponseV2> compareSessions,
        Action<string> log)
    {
        _currentSessionProvider = currentSessionProvider;
        _mapPageProvider = mapPageProvider;
        _listSaves = listSaves;
        _createSave = createSave;
        _getSave = getSave;
        _loadSave = loadSave;
        _unloadSave = unloadSave;
        _deleteSave = deleteSave;
        _compareSessions = compareSessions;
        _log = log;
    }

    public bool IsRunning => _listener?.IsListening == true;
    public string Url => $"http://localhost:{_port}/";

    public void Start(int port, bool allowNetworkAccess)
    {
        if (IsRunning && _port == port && _allowNetworkAccess == allowNetworkAccess)
            return;

        Stop();

        _port = port;
        _allowNetworkAccess = allowNetworkAccess;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");

        if (allowNetworkAccess)
        {
            try
            {
                _listener.Prefixes.Add($"http://+:{port}/");
            }
            catch (Exception ex)
            {
                _log($"Analytics web server network prefix failed. {ex.GetType().Name}: {ex.Message}");
            }
        }

        _listener.Start();
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        _log($"Analytics web server started at {Url}");
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _listener = null;
            _listenTask = null;
        }
    }

    public void Dispose() => Stop();

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"Analytics web server listener error. {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath ?? "/";

        try
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (path.Equals("/", StringComparison.Ordinal) || path.EqualsIgnoreCase("/index.html"))
            {
                await WriteHtmlAsync(response, AnalyticsWebDashboard.Page);
                return;
            }

            if (path.EqualsIgnoreCase("/beast-icon.png") && request.HttpMethod == "GET")
            {
                await WritePngAsync(response, GetBeastIconBytes());
                return;
            }

            if (path.EqualsIgnoreCase("/api/health") && request.HttpMethod == "GET")
            {
                await WriteJsonAsync(response, new { ok = true, serverTimeUtc = DateTime.UtcNow });
                return;
            }

            if (path.EqualsIgnoreCase("/api/session/current") && request.HttpMethod == "GET")
            {
                await WriteJsonAsync(response, _currentSessionProvider());
                return;
            }

            if (path.EqualsIgnoreCase("/api/session/maps") && request.HttpMethod == "GET")
            {
                var limit = ParseInt(request.QueryString["limit"], 200, 1, 1000);
                var offset = ParseInt(request.QueryString["offset"], 0, 0, 100000);
                await WriteJsonAsync(response, _mapPageProvider(offset, limit));
                return;
            }

            if (path.EqualsIgnoreCase("/api/session/saves") && request.HttpMethod == "GET")
            {
                await WriteJsonAsync(response, _listSaves());
                return;
            }

            if (path.EqualsIgnoreCase("/api/session/saves") && request.HttpMethod == "POST")
            {
                var req = await ReadBodyAsync<CreateSessionSaveRequestV2>(request) ?? new CreateSessionSaveRequestV2();
                await WriteActionResponseAsync(response, _createSave(req));
                return;
            }

            if (path.EqualsIgnoreCase("/api/session/compare") && request.HttpMethod == "POST")
            {
                var req = await ReadBodyAsync<CompareSessionsRequestV2>(request) ?? new CompareSessionsRequestV2();
                var result = _compareSessions(req);
                if (!result.Success)
                {
                    await WriteErrorAsync(response, CodeToStatus(result.Code), result.Code, result.Message);
                    return;
                }

                await WriteJsonAsync(response, result);
                return;
            }

            var segments = GetSegments(path);
            if (segments.Length >= 4 &&
                segments[0].EqualsIgnoreCase("api") &&
                segments[1].EqualsIgnoreCase("session") &&
                segments[2].EqualsIgnoreCase("saves"))
            {
                var sessionId = WebUtility.UrlDecode(segments[3] ?? string.Empty);

                if (segments.Length == 4 && request.HttpMethod == "GET")
                {
                    var detail = _getSave(sessionId);
                    if (detail?.Session == null)
                    {
                        await WriteErrorAsync(response, 404, "not_found", "Session not found.");
                        return;
                    }

                    await WriteJsonAsync(response, detail);
                    return;
                }

                if (segments.Length == 5 && request.HttpMethod == "POST" && segments[4].EqualsIgnoreCase("load"))
                {
                    await WriteActionResponseAsync(response, _loadSave(sessionId));
                    return;
                }

                if (segments.Length == 5 && request.HttpMethod == "POST" && segments[4].EqualsIgnoreCase("unload"))
                {
                    await WriteActionResponseAsync(response, _unloadSave(sessionId));
                    return;
                }

                if (segments.Length == 4 && request.HttpMethod == "DELETE")
                {
                    await WriteActionResponseAsync(response, _deleteSave(sessionId));
                    return;
                }
            }

            await WriteErrorAsync(response, 404, "not_found", "Not found.");
        }
        catch (Exception ex)
        {
            _log($"Analytics web server request error ({path}). {ex.GetType().Name}: {ex.Message}");
            if (response.OutputStream.CanWrite)
            {
                await WriteErrorAsync(response, 500, "internal_error", "Internal server error.");
            }
        }
        finally
        {
            try { response.OutputStream.Close(); } catch { }
        }
    }

    private async Task WriteActionResponseAsync(HttpListenerResponse response, ApiActionResponseV2 result)
    {
        if (result?.Success == true)
        {
            await WriteJsonAsync(response, result);
            return;
        }

        var code = result?.Code ?? "request_failed";
        await WriteErrorAsync(response, CodeToStatus(code), code, result?.Message ?? "Request failed.", result?.Details);
    }

    private async Task<T> ReadBodyAsync<T>(HttpListenerRequest request) where T : class
    {
        if (request?.InputStream == null || !request.HasEntityBody)
            return null;

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }

    private async Task WriteErrorAsync(HttpListenerResponse response, int statusCode, string code, string message, object details = null)
    {
        response.StatusCode = statusCode;
        await WriteJsonAsync(response, new ApiErrorResponseV2
        {
            Code = code ?? "error",
            Message = message ?? "Request failed.",
            Details = details,
        });
    }

    private static int ParseInt(string value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
            return fallback;

        return Math.Clamp(parsed, min, max);
    }

    private static string[] GetSegments(string path)
        => (path ?? string.Empty)
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static int CodeToStatus(string code) => code switch
    {
        "invalid_id" or "invalid_request" => 400,
        "not_found" => 404,
        "duplicate" or "not_loaded" => 409,
        _ => 400,
    };

    private async Task WriteJsonAsync(HttpListenerResponse response, object payload)
    {
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html)
    {
        response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(html ?? string.Empty);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    private static async Task WritePngAsync(HttpListenerResponse response, byte[] pngBytes)
    {
        response.ContentType = "image/png";
        response.ContentLength64 = pngBytes?.Length ?? 0;
        if (pngBytes is { Length: > 0 })
            await response.OutputStream.WriteAsync(pngBytes, 0, pngBytes.Length);
    }

    private static byte[] GetBeastIconBytes()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "source", "BeastsV2", "Resources", "beast.png");
            if (File.Exists(path))
                return File.ReadAllBytes(path);

            var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "beast.png");
            return File.Exists(fallback) ? File.ReadAllBytes(fallback) : DefaultBeastIconPng;
        }
        catch
        {
            return DefaultBeastIconPng;
        }
    }
}