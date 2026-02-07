using FluentAssertions;
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
    public async Task AddTorrentAsync_WhenSameCredentialFailsTwice_ThirdAttemptIsBlockedWithoutHttpRequest()
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
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        var sut = CreateSut(handlerMock.Object, username, password);

        // Act
        var first = await sut.AddTorrentAsync("magnet:?xt=urn:btih:HASH1");
        var second = await sut.AddTorrentAsync("magnet:?xt=urn:btih:HASH2");
        var third = await sut.AddTorrentAsync("magnet:?xt=urn:btih:HASH3");

        // Assert
        first.Should().BeFalse();
        second.Should().BeFalse();
        third.Should().BeFalse();

        // Only first two attempts should hit network; third attempt is blocked by policy.
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    private backend.Services.Implementations.QBittorrentService CreateSut(
        HttpMessageHandler handler,
        string username,
        string password)
    {
        var httpClient = new HttpClient(handler);
        var config = new QBittorrentConfiguration
        {
            Host = "localhost",
            Port = 8080,
            Username = username,
            Password = password,
            TimeoutSeconds = 5
        };

        return new backend.Services.Implementations.QBittorrentService(
            httpClient,
            _loggerMock.Object,
            Options.Create(config));
    }
}
