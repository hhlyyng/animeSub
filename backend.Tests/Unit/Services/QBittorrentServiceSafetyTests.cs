using FluentAssertions;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using backend.Models.Configuration;
namespace backend.Tests.Unit.Services;

public class QBittorrentServiceSafetyTests
{
    private readonly Mock<ILogger<backend.Services.Implementations.QBittorrentService>> _loggerMock = new();

    [Theory]
    [InlineData("", "password")]
    [InlineData("admin", "")]
    [InlineData("   ", "password")]
    [InlineData("admin", "   ")]
    public async Task AddTorrentAsync_WhenCredentialMissing_ReturnsFalse_AndSkipsHttpRequest(
        string username,
        string password)
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var result = await sut.AddTorrentAsync("magnet:?xt=urn:btih:TESTHASH");

        // Assert
        result.Should().BeFalse();
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AddTorrentAsync_WhenSameCredentialAuthFailsTwice_ThirdAttemptIsBlockedWithoutHttpRequest()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("Fails.")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var first = await sut.AddTorrentAsync("magnet:?xt=urn:btih:HASH1");
        var second = await sut.AddTorrentAsync("magnet:?xt=urn:btih:HASH2");
        var third = await sut.AddTorrentAsync("magnet:?xt=urn:btih:HASH3");

        // Assert
        first.Should().BeFalse();
        second.Should().BeFalse();
        third.Should().BeFalse();

        // Third attempt is blocked by credential policy, so only two login requests are sent.
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.RequestUri != null &&
                r.RequestUri.AbsolutePath.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase)),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AddTorrentAsync_WhenCredentialLockoutExpires_AllowsLoginRetry()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("Fails.")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(
            handlerMock.Object,
            username,
            password,
            failedLoginBlockSeconds: 1);

        // Act
        var first = await sut.AddTorrentAsync("magnet:?xt=urn:btih:LOCKOUT1");
        var second = await sut.AddTorrentAsync("magnet:?xt=urn:btih:LOCKOUT2");
        var third = await sut.AddTorrentAsync("magnet:?xt=urn:btih:LOCKOUT3");

        await Task.Delay(1200);
        var fourth = await sut.AddTorrentAsync("magnet:?xt=urn:btih:LOCKOUT4");

        // Assert
        first.Should().BeFalse();
        second.Should().BeFalse();
        third.Should().BeFalse();
        fourth.Should().BeFalse();

        // Third attempt is blocked; fourth attempt retries after the lockout window.
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(3),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.RequestUri != null &&
                r.RequestUri.AbsolutePath.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase)),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AddTorrentAsync_WhenQbEndpointOffline_SuspendsEndpointAndAvoidsImmediateRetry()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var sut = CreateSut(
            handlerMock.Object,
            username,
            password,
            offlineSuspendSeconds: 60);

        // Act
        var first = () => sut.AddTorrentAsync("magnet:?xt=urn:btih:OFFLINE1");
        var second = () => sut.AddTorrentAsync("magnet:?xt=urn:btih:OFFLINE2");

        // Assert
        await first.Should().ThrowAsync<backend.Services.Exceptions.QBittorrentUnavailableException>();
        await second.Should().ThrowAsync<backend.Services.Exceptions.QBittorrentUnavailableException>();

        // Suspension is active after first failure; second call does not hit HTTP handler.
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AddTorrentAsync_WhenConfiguredTagsExist_SendsTagsToQbittorrentApi()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";
        var configuredTags = "AnimeSub,weekly";
        string? addRequestBody = null;

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;

                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=test-session-id; path=/; HttpOnly");
                    return loginResponse;
                }

                if (path.EndsWith("/api/v2/torrents/add", StringComparison.OrdinalIgnoreCase))
                {
                    addRequestBody = request.Content == null
                        ? null
                        : await request.Content.ReadAsStringAsync();
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var sut = CreateSut(handlerMock.Object, username, password, configuredTags);

        // Act
        var result = await sut.AddTorrentAsync("magnet:?xt=urn:btih:TAGTEST");

        // Assert
        result.Should().BeTrue();
        addRequestBody.Should().NotBeNull();
        addRequestBody.Should().Contain("tags=AnimeSub%2Cweekly");
    }

    [Fact]
    public async Task AddTorrentAsync_WhenFirstLoginHasNoSid_RetriesLoginAndSucceeds()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";
        var loginCallCount = 0;

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    loginCallCount++;
                    if (loginCallCount == 1)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("Ok.")
                        });
                    }

                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=recovered-session; path=/; HttpOnly");
                    return Task.FromResult(loginResponse);
                }

                if (path.EndsWith("/api/v2/torrents/add", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var result = await sut.AddTorrentAsync("magnet:?xt=urn:btih:RETRYLOGIN");

        // Assert
        result.Should().BeTrue();
        loginCallCount.Should().Be(2);
    }

    [Fact]
    public async Task AddTorrentAsync_WhenAddApiReturnsFailsBody_ReturnsFalse()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=test-session-id; path=/; HttpOnly");
                    return Task.FromResult(loginResponse);
                }

                if (path.EndsWith("/api/v2/torrents/add", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Fails.")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var result = await sut.AddTorrentAsync("magnet:?xt=urn:btih:FAILSBODY");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddTorrentWithTrackingAsync_WhenTorrentNotVisibleAfterAdd_ReturnsFalse()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";
        var targetHash = "1234567890ABCDEF1234567890ABCDEF12345678";

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=test-session-id; path=/; HttpOnly");
                    return Task.FromResult(loginResponse);
                }

                if (path.EndsWith("/api/v2/torrents/add", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    });
                }

                if (path.EndsWith("/api/v2/torrents/info", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var result = await sut.AddTorrentWithTrackingAsync(
            "https://example.com/test.torrent",
            targetHash,
            "Test",
            0,
            backend.Data.Entities.DownloadSource.Manual);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddTorrentWithTrackingAsync_WhenTorrentVisibleAfterAdd_ReturnsTrue()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";
        var targetHash = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=test-session-id; path=/; HttpOnly");
                    return Task.FromResult(loginResponse);
                }

                if (path.EndsWith("/api/v2/torrents/add", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    });
                }

                if (path.EndsWith("/api/v2/torrents/info", StringComparison.OrdinalIgnoreCase))
                {
                    var torrentJson = $"[{{\"hash\":\"{targetHash.ToLowerInvariant()}\",\"name\":\"Test\",\"size\":1,\"progress\":0.01,\"state\":\"downloading\"}}]";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(torrentJson)
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var result = await sut.AddTorrentWithTrackingAsync(
            "https://example.com/test.torrent",
            targetHash,
            "Test",
            0,
            backend.Data.Entities.DownloadSource.Manual);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task AddTorrentWithTrackingAsync_WhenUrlAddNotVisible_FileUploadFallbackCanSucceed()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";
        var targetHash = "FEDCBA1234567890FEDCBA1234567890FEDCBA12";
        var fallbackUploadTriggered = false;
        var addRequestCount = 0;

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                var uri = request.RequestUri;
                var path = uri?.AbsolutePath ?? string.Empty;

                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=test-session-id; path=/; HttpOnly");
                    return loginResponse;
                }

                if (uri?.Host == "example.com" && path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 })
                    };
                }

                if (path.EndsWith("/api/v2/torrents/add", StringComparison.OrdinalIgnoreCase))
                {
                    addRequestCount++;
                    var mediaType = request.Content?.Headers.ContentType?.MediaType ?? string.Empty;
                    if (mediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                    {
                        fallbackUploadTriggered = true;
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                }

                if (path.EndsWith("/api/v2/torrents/info", StringComparison.OrdinalIgnoreCase))
                {
                    if (fallbackUploadTriggered)
                    {
                        var torrentJson =
                            $"[{{\"hash\":\"{targetHash.ToLowerInvariant()}\",\"name\":\"Test\",\"size\":1,\"progress\":0.01,\"state\":\"downloading\",\"category\":\"anime\"}}]";
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(torrentJson)
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var result = await sut.AddTorrentWithTrackingAsync(
            "https://example.com/test.torrent",
            targetHash,
            "Test",
            0,
            backend.Data.Entities.DownloadSource.Manual);

        // Assert
        result.Should().BeTrue();
        fallbackUploadTriggered.Should().BeTrue();
        addRequestCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task PauseTorrentAsync_WhenPauseEndpointNotFound_FallbacksToStopEndpoint()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";
        var calledEndpoints = new List<string>();

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                calledEndpoints.Add(path);

                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=test-session-id; path=/; HttpOnly");
                    return Task.FromResult(loginResponse);
                }

                if (path.EndsWith("/api/v2/torrents/pause", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("Not Found")
                    });
                }

                if (path.EndsWith("/api/v2/torrents/stop", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(string.Empty)
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        Func<Task> act = () => sut.PauseTorrentAsync("ABCDEF123");

        // Assert
        await act.Should().NotThrowAsync();
        calledEndpoints.Should().Contain(path => path.EndsWith("/api/v2/torrents/pause", StringComparison.OrdinalIgnoreCase));
        calledEndpoints.Should().Contain(path => path.EndsWith("/api/v2/torrents/stop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResumeTorrentAsync_WhenResumeEndpointNotFound_FallbacksToStartEndpoint()
    {
        // Arrange
        var username = $"user_{Guid.NewGuid():N}";
        var password = $"pass_{Guid.NewGuid():N}";
        var calledEndpoints = new List<string>();

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                calledEndpoints.Add(path);

                if (path.EndsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var loginResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("Ok.")
                    };
                    loginResponse.Headers.Add("Set-Cookie", "SID=test-session-id; path=/; HttpOnly");
                    return Task.FromResult(loginResponse);
                }

                if (path.EndsWith("/api/v2/torrents/resume", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("Not Found")
                    });
                }

                if (path.EndsWith("/api/v2/torrents/start", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(string.Empty)
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        Func<Task> act = () => sut.ResumeTorrentAsync("ABCDEF456");

        // Assert
        await act.Should().NotThrowAsync();
        calledEndpoints.Should().Contain(path => path.EndsWith("/api/v2/torrents/resume", StringComparison.OrdinalIgnoreCase));
        calledEndpoints.Should().Contain(path => path.EndsWith("/api/v2/torrents/start", StringComparison.OrdinalIgnoreCase));
    }

    private backend.Services.Implementations.QBittorrentService CreateSut(
        HttpMessageHandler handler,
        string username,
        string password,
        string tags = "AnimeSub",
        int failedLoginBlockSeconds = 300,
        int offlineSuspendSeconds = 45)
    {
        var httpClient = new HttpClient(handler);
        var config = new QBittorrentConfiguration
        {
            Host = "localhost",
            Port = 8080,
            Username = username,
            Password = password,
            Tags = tags,
            FailedLoginBlockSeconds = failedLoginBlockSeconds,
            OfflineSuspendSeconds = offlineSuspendSeconds,
            TimeoutSeconds = 5
        };

        return new backend.Services.Implementations.QBittorrentService(
            httpClient,
            _loggerMock.Object,
            new StaticOptionsMonitor<QBittorrentConfiguration>(config));
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
