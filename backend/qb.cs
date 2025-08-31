public class QBittorrentService
{
    private readonly HttpClient _httpClient;
    private string _host;
    private int _port;
    private string _username;
    private string _password;
    private string _sessionCookie;
    private DateTime _sessionExpiry;
    
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
        
        var response = await _httpClient.PostAsync(loginUrl, formData);
        
        if (response.IsSuccessStatusCode)
        {
            _sessionCookie = ExtractSessionCookie(response);
            _sessionExpiry = DateTime.Now.AddHours(1); // qBittorrent session 通常1小时过期
            return true;
        }
        
        return false;
    }
    
    private async Task EnsureLoggedInAsync()
    {
        if (string.IsNullOrEmpty(_sessionCookie) || DateTime.Now >= _sessionExpiry)
        {
            await LoginAsync();
        }
    }
    
    public async Task<bool> AddTorrentAsync(string magnetLink)
    {
        await EnsureLoggedInAsync();
        
        var request = new HttpRequestMessage(HttpMethod.Post, 
            $"http://{_host}:{_port}/api/v2/torrents/add");
        
        request.Headers.Add("Cookie", _sessionCookie);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("urls", magnetLink)
        });
        
        var response = await _httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }
    
    public async Task<List<TorrentInfo>> GetTorrentsAsync()
    {
        await EnsureLoggedInAsync();
        
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://{_host}:{_port}/api/v2/torrents/info");
        request.Headers.Add("Cookie", _sessionCookie);
        
        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<TorrentInfo>>(json);
        }
        
        return new List<TorrentInfo>();
    }
    
    private string ExtractSessionCookie(HttpResponseMessage response)
    {
        // 从响应头提取 SID cookie
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                if (cookie.StartsWith("SID="))
                {
                    return cookie.Split(';')[0];
                }
            }
        }
        return null;
    }
}