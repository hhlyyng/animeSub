using FluentAssertions;
using backend.Data.Entities;
using backend.Services.Background;

namespace backend.Tests.Unit.Services;

public class DownloadProgressSyncServiceTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 50.0)]
    [InlineData(1.0, 100.0)]
    [InlineData(12.34, 12.34)]
    [InlineData(120.0, 100.0)]
    [InlineData(-10.0, 0.0)]
    public void ToProgressPercent_NormalizesValue(double raw, double expected)
    {
        var result = DownloadProgressSyncService.ToProgressPercent(raw);
        result.Should().Be(expected);
    }

    [Fact]
    public void CalculateEtaSeconds_WhenSpeedIsZero_ReturnsNull()
    {
        var eta = DownloadProgressSyncService.CalculateEtaSeconds(
            sizeBytes: 1024 * 1024 * 1024,
            progressPercent: 20,
            downloadSpeedBytesPerSecond: 0);

        eta.Should().BeNull();
    }

    [Fact]
    public void CalculateEtaSeconds_WithValidInputs_ReturnsExpectedSeconds()
    {
        var eta = DownloadProgressSyncService.CalculateEtaSeconds(
            sizeBytes: 1000,
            progressPercent: 50,
            downloadSpeedBytesPerSecond: 100);

        eta.Should().Be(5);
    }

    [Theory]
    [InlineData("downloading", 30, DownloadStatus.Pending, DownloadStatus.Downloading)]
    [InlineData("stalledDL", 80, DownloadStatus.Downloading, DownloadStatus.Pending)]
    [InlineData("stalledDL", 100, DownloadStatus.Downloading, DownloadStatus.Completed)]
    [InlineData("uploading", 100, DownloadStatus.Downloading, DownloadStatus.Completed)]
    [InlineData("completed", 99.95, DownloadStatus.Downloading, DownloadStatus.Completed)]
    [InlineData("error", 20, DownloadStatus.Downloading, DownloadStatus.Failed)]
    [InlineData("unknown", 20, DownloadStatus.Pending, DownloadStatus.Pending)]
    public void MapStateToStatus_MapsExpectedStatus(
        string state,
        double progressPercent,
        DownloadStatus currentStatus,
        DownloadStatus expected)
    {
        var result = DownloadProgressSyncService.MapStateToStatus(state, progressPercent, currentStatus);
        result.Should().Be(expected);
    }
}
