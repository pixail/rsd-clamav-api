using nClam;

namespace GovUK.Dfe.ClamAV.Services;

public record ClamAvScanResult(bool IsSuccess, bool IsClean, string? MalwareName = null, string? Error = null);

public interface IClamAvScanner
{
    Task<ClamAvScanResult> ScanAsync(Stream stream, CancellationToken cancellationToken = default);
}

public class ClamAvScanner(IConfiguration configuration) : IClamAvScanner
{
    public async Task<ClamAvScanResult> ScanAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var host = configuration["CLAMD_HOST"] ?? Environment.GetEnvironmentVariable("CLAMD_HOST") ?? "127.0.0.1";
        var port = int.TryParse(
            configuration["CLAMD_PORT"] ?? Environment.GetEnvironmentVariable("CLAMD_PORT"),
            out var configuredPort) ? configuredPort : 3310;

        var clam = new ClamClient(host, port) { MaxStreamSize = stream.Length };
        var result = await clam.SendAndScanFileAsync(stream, cancellationToken);

        return result.Result switch
        {
            ClamScanResults.Clean => new ClamAvScanResult(true, true),
            ClamScanResults.VirusDetected => new ClamAvScanResult(
                true,
                false,
                result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "unknown"),
            _ => new ClamAvScanResult(false, false, Error: $"Unexpected result: {result.RawResult}")
        };
    }
}
