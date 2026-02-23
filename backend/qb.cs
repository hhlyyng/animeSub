using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class QBittorrentService
{
    private readonly HttpClient _httpClient;

    private string _host = string.Empty;
    private int _port;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _sessionCookie = string.Empty; // 允许为空字符串作兜底
    private DateTime _sessionExpiry;

    public QBittorrentService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<bool> InitializeAsync(string host, int port, string username, string password)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;

        return await LoginAsync();
    }

    private async Task<bool> LoginAsync()
    {
        var loginUrl = $"http://{_host}:{_port}/api/v2/auth/login";
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", _username),
            new KeyValuePair<string, string>("password", _password)
        });

        var resp = await _httpClient.PostAsync(loginUrl, formData);
        if (!resp.IsSuccessStatusCode) return false;

        _sessionCookie = ExtractSessionCookie(resp) ?? string.Empty;
        _sessionExpiry = DateTime.Now.AddHours(1);
        return !string.IsNullOrEmpty(_sessionCookie);
    }

    private async Task EnsureLoggedInAsync()
    {
        if (string.IsNullOrEmpty(_sessionCookie) || DateTime.Now >= _sessionExpiry)
        {
            var ok = await LoginAsync();
            if (!ok) throw new InvalidOperationException("qBittorrent 登录失败。");
        }
    }

    public async Task<bool> AddTorrentAsync(string magnetLink)
    {
        await EnsureLoggedInAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, $"http://{_host}:{_port}/api/v2/torrents/add");
        req.Headers.Add("Cookie", _sessionCookie);
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", magnetLink)
        });

        var resp = await _httpClient.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<TorrentInfo>> GetTorrentsAsync()
    {
        await EnsureLoggedInAsync();

        var req = new HttpRequestMessage(HttpMethod.Get, $"http://{_host}:{_port}/api/v2/torrents/info");
        req.Headers.Add("Cookie", _sessionCookie);

        var resp = await _httpClient.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return new List<TorrentInfo>();

        var json = await resp.Content.ReadAsStringAsync();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<TorrentInfo>>(json, opts) ?? new List<TorrentInfo>();
    }

    private static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                if (cookie.StartsWith("SID=", StringComparison.OrdinalIgnoreCase))
                    return cookie.Split(';')[0];
            }
        }
        return null;
    }
}

// /api/v2/torrents/info DTO（可按需扩展/调整）
public class TorrentInfo
{
    public string Hash { get; set; } = default!;
    public string Name { get; set; } = default!;
    public long Size { get; set; }
    public double Progress { get; set; }      // 0..1
    public string State { get; set; } = default!;
    public long Dlspeed { get; set; }
    public long Upspeed { get; set; }
    public int NumSeeds { get; set; }
    public int NumLeechs { get; set; }
}
