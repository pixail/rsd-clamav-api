using GovUK.Dfe.ClamAV.Handlers;
using GovUK.Dfe.ClamAV.Models;
using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.CoreLibs.AsyncProcessing.Interfaces;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace GovUK.Dfe.ClamAV.Tests;

public class FileScanHandlerTests
{
    private readonly IClamAvScanner _scanner = Substitute.For<IClamAvScanner>();
    private readonly FileScanHandler _handler;

    public FileScanHandlerTests()
    {
        _handler = new FileScanHandler(
            Substitute.For<IScanJobService>(),
            Substitute.For<IScanProcessingService>(),
            Substitute.For<IBackgroundServiceFactory>(),
            _scanner);
    }

    [Fact]
    public async Task EmptyFileReturnsBadRequest()
    {
        var result = await _handler.HandleSyncAsync(FormFile(Array.Empty<byte>()));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
    }

    [Fact]
    public async Task CleanResultReturnsFileMetadata()
    {
        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ClamAvScanResult(true, true));

        var result = await _handler.HandleSyncAsync(FormFile("hello"u8.ToArray(), "clean.txt"));

        var response = Value<ScanResponse>(result);
        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        Assert.Equal("clean", response.Status);
        Assert.Equal("clean.txt", response.FileName);
        Assert.Equal(5, response.Size);
    }

    [Fact]
    public async Task InfectedResultIncludesMalwareName()
    {
        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ClamAvScanResult(true, false, "Eicar-Test-Signature"));

        var result = await _handler.HandleSyncAsync(FormFile("content"u8.ToArray(), "infected.txt"));

        var response = Value<ScanResponse>(result);
        Assert.Equal("infected", response.Status);
        Assert.Equal("Eicar-Test-Signature", response.Malware);
    }

    [Fact]
    public async Task ScannerFailureReturnsServerError()
    {
        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ClamAvScanResult(false, false, Error: "clamd unavailable"));

        var result = await _handler.HandleSyncAsync(FormFile("content"u8.ToArray()));

        Assert.Equal(StatusCodes.Status500InternalServerError, StatusCode(result));
    }

    private static FormFile FormFile(byte[] bytes, string name = "sample.txt") =>
        new(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    private static int? StatusCode(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static T Value<T>(IResult result) =>
        Assert.IsType<T>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
}
