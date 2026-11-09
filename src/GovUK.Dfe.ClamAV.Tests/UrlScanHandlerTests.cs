using GovUK.Dfe.ClamAV.Handlers;
using GovUK.Dfe.ClamAV.Models;
using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.CoreLibs.AsyncProcessing.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.ClamAV.Tests;

public class UrlScanHandlerTests
{
    private readonly IScanJobService _jobs = Substitute.For<IScanJobService>();
    private readonly IScanProcessingService _processing = Substitute.For<IScanProcessingService>();
    private readonly IBackgroundServiceFactory _background = Substitute.For<IBackgroundServiceFactory>();
    private readonly UrlScanHandler _handler;

    public UrlScanHandlerTests()
    {
        _jobs.CreateJob(Arg.Any<string>(), Arg.Any<long>()).Returns("job-123");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MAX_FILE_SIZE_MB"] = "25" })
            .Build();
        _handler = new UrlScanHandler(_jobs, _processing, _background, configuration);
    }

    [Fact]
    public async Task EmptyUrlReturnsBadRequest()
    {
        var result = await _handler.HandleAsync(new ScanUrlRequest());

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
        Assert.Equal("URL is required", Value<ErrorResponse>(result).Error);
    }

    [Fact]
    public async Task InvalidBase64ReturnsBadRequest()
    {
        var result = await _handler.HandleAsync(new ScanUrlRequest { Url = "%%%", IsBase64 = true });

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
        Assert.Equal("Invalid Base64 encoded URL", Value<ErrorResponse>(result).Error);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("not-a-url")]
    public async Task NonHttpUrlReturnsBadRequest(string url)
    {
        var result = await _handler.HandleAsync(new ScanUrlRequest { Url = url });

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
    }

    [Fact]
    public async Task ValidUrlCreatesDownloadingJob()
    {
        var result = await _handler.HandleAsync(new ScanUrlRequest { Url = "https://example.com/files/report.pdf" });

        Assert.Equal(StatusCodes.Status202Accepted, StatusCode(result));
        var response = Value<AsyncScanResponse>(result);
        Assert.Equal("job-123", response.JobId);
        Assert.Equal("downloading", response.Status);
        Assert.Equal("report.pdf", response.FileName);
        Assert.Equal("https://example.com/files/report.pdf", response.SourceUrl);
        _jobs.Received(1).UpdateJobStatus("job-123", "downloading");
    }

    [Fact]
    public async Task Base64UrlIsDecodedBeforeCreatingJob()
    {
        const string url = "https://example.com/file.txt";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url));

        var result = await _handler.HandleAsync(new ScanUrlRequest { Url = encoded, IsBase64 = true });

        Assert.Equal(url, Value<AsyncScanResponse>(result).SourceUrl);
        _jobs.Received(1).CreateJob("file.txt", 0);
    }

    private static int? StatusCode(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static T Value<T>(IResult result) =>
        Assert.IsType<T>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
}
