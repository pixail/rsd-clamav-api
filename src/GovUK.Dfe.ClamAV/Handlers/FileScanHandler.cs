using GovUK.Dfe.ClamAV.Models;
using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.CoreLibs.AsyncProcessing.Interfaces;
using System.Net;

namespace GovUK.Dfe.ClamAV.Handlers;

public class FileScanHandler(
    IScanJobService jobService,
    IScanProcessingService scanProcessing,
    IBackgroundServiceFactory backgroundService,
    IClamAvScanner clamAvScanner)
{
    public async Task<IResult> HandleSyncAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest(new ErrorResponse { Error = "Missing or empty file" });

        await using var stream = file.OpenReadStream();
        var startTime = DateTime.UtcNow;
        var result = await clamAvScanner.ScanAsync(stream);
        var scanDuration = DateTime.UtcNow - startTime;

        if (!result.IsSuccess)
            return Results.Problem($"Scan error: {result.Error}", statusCode: (int)HttpStatusCode.InternalServerError);

        return result.IsClean
            ? Results.Ok(new ScanResponse
            {
                Status = "clean",
                Engine = "clamav",
                FileName = file.FileName,
                Size = file.Length,
                ScanDurationMs = scanDuration.TotalMilliseconds
            })
            : Results.Ok(new ScanResponse
            {
                Status = "infected",
                Engine = "clamav",
                Malware = result.MalwareName ?? "unknown",
                FileName = file.FileName,
                Size = file.Length,
                ScanDurationMs = scanDuration.TotalMilliseconds
            });
    }

    public async Task<IResult> HandleAsyncAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest(new ErrorResponse { Error = "Missing or empty file" });

        // Create job first
        var jobId = jobService.CreateJob(file.FileName, file.Length);

        // Save to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"clamav_{jobId}_{Path.GetFileName(file.FileName)}");

        try
        {
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
            await file.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            jobService.UpdateJobStatus(jobId, "error", error: $"Failed to save file: {ex.Message}");
            return Results.Problem("Failed to process upload", statusCode: 500);
        }

        // Enqueue for background processing
        _ = backgroundService.EnqueueTask(async (ct) =>
        {
            return await scanProcessing.ProcessFileScanAsync(jobId, tempPath, ct);
        });

        return Results.Accepted($"/scan/async/{jobId}", new AsyncScanResponse
        {
            JobId = jobId,
            Status = "queued",
            Message = "File uploaded successfully. Use the jobId to check scan status.",
            StatusUrl = $"/scan/async/{jobId}"
        });
    }
}

