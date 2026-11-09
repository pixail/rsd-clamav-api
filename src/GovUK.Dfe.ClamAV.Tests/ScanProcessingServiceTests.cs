using GovUK.Dfe.ClamAV.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;

namespace GovUK.Dfe.ClamAV.Tests;

public class ScanProcessingServiceTests
{
    private readonly IClamAvScanner _scanner = Substitute.For<IClamAvScanner>();
    private readonly ScanJobService _jobs = new(NullLogger<ScanJobService>.Instance);

    [Fact]
    public async Task ProcessFileScanMarksCleanAndDeletesTemporaryFile()
    {
        var path = TemporaryFile("clean content");
        var jobId = _jobs.CreateJob("clean.txt", new FileInfo(path).Length);
        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ClamAvScanResult(true, true));
        var service = CreateService();

        var succeeded = await service.ProcessFileScanAsync(jobId, path, CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal("clean", _jobs.GetJob(jobId)!.Status);
        Assert.NotNull(_jobs.GetJob(jobId)!.CompletedAt);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ProcessFileScanStoresDetectedMalware()
    {
        var path = TemporaryFile("infected content");
        var jobId = _jobs.CreateJob("infected.txt", new FileInfo(path).Length);
        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ClamAvScanResult(true, false, "Test.Malware"));

        await CreateService().ProcessFileScanAsync(jobId, path, CancellationToken.None);

        Assert.Equal("infected", _jobs.GetJob(jobId)!.Status);
        Assert.Equal("Test.Malware", _jobs.GetJob(jobId)!.Malware);
    }

    [Fact]
    public async Task ProcessFileScanStoresScannerError()
    {
        var path = TemporaryFile("content");
        var jobId = _jobs.CreateJob("failed.txt", new FileInfo(path).Length);
        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ClamAvScanResult(false, false, Error: "clamd unavailable"));

        var succeeded = await CreateService().ProcessFileScanAsync(jobId, path, CancellationToken.None);

        Assert.False(succeeded);
        Assert.Equal("error", _jobs.GetJob(jobId)!.Status);
        Assert.Equal("clamd unavailable", _jobs.GetJob(jobId)!.Error);
    }

    [Fact]
    public async Task UrlScanRejectsContentLengthAboveLimitWithoutScanning()
    {
        var jobId = _jobs.CreateJob("large.bin", 0);
        var path = Path.Combine(Path.GetTempPath(), $"clamav_test_{Guid.NewGuid():N}");
        var handler = new DelegateHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            response.Content.Headers.ContentLength = 101;
            return response;
        });

        var succeeded = await CreateService(handler).ProcessUrlScanAsync(
            jobId, "https://example.com/large.bin", path, 100, CancellationToken.None);

        Assert.False(succeeded);
        Assert.Equal("error", _jobs.GetJob(jobId)!.Status);
        Assert.Contains("exceeds maximum", _jobs.GetJob(jobId)!.Error);
        await _scanner.DidNotReceive().ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task UrlScanDownloadsScansAndCleansUpFile()
    {
        var jobId = _jobs.CreateJob("remote.txt", 0);
        var path = Path.Combine(Path.GetTempPath(), $"clamav_test_{Guid.NewGuid():N}");
        var content = "downloaded content"u8.ToArray();
        var handler = new DelegateHttpMessageHandler(request => request.Method == HttpMethod.Head
            ? ResponseWithoutContentLength()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ClamAvScanResult(true, true));

        var succeeded = await CreateService(handler).ProcessUrlScanAsync(
            jobId, "https://example.com/remote.txt", path, 1024, CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal("clean", _jobs.GetJob(jobId)!.Status);
        Assert.Equal(content.Length, _jobs.GetJob(jobId)!.FileSize);
        Assert.False(File.Exists(path));
        await _scanner.Received(1).ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    private ScanProcessingService CreateService(HttpMessageHandler? handler = null)
    {
        var client = new HttpClient(handler ?? new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        return new ScanProcessingService(
            _jobs,
            _scanner,
            new StubHttpClientFactory(client),
            NullLogger<ScanProcessingService>.Instance);
    }

    private static string TemporaryFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"clamav_test_{Guid.NewGuid():N}");
        File.WriteAllText(path, content);
        return path;
    }

    private static HttpResponseMessage ResponseWithoutContentLength()
    {
        var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.ContentLength = null;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
