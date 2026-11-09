using GovUK.Dfe.ClamAV.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovUK.Dfe.ClamAV.Tests;

public class ScanJobServiceTests
{
    private readonly ScanJobService _service = new(NullLogger<ScanJobService>.Instance);

    [Fact]
    public void CreateJobStoresQueuedJob()
    {
        var jobId = _service.CreateJob("sample.txt", 42);

        var job = Assert.IsType<Models.ScanJob>(_service.GetJob(jobId));
        Assert.Equal("sample.txt", job.FileName);
        Assert.Equal(42, job.FileSize);
        Assert.Equal("queued", job.Status);
        Assert.Equal("clamav", job.Engine);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void UpdateJobStatusStoresResultDetails()
    {
        var jobId = _service.CreateJob("sample.txt", 42);

        _service.UpdateJobStatus(jobId, "infected", malware: "Eicar-Test-Signature", error: "detail");

        var job = Assert.IsType<Models.ScanJob>(_service.GetJob(jobId));
        Assert.Equal("infected", job.Status);
        Assert.Equal("Eicar-Test-Signature", job.Malware);
        Assert.Equal("detail", job.Error);
    }

    [Fact]
    public void CompleteJobSetsCompletionTimeAndDuration()
    {
        var jobId = _service.CreateJob("sample.txt", 42);

        _service.CompleteJob(jobId);

        var job = Assert.IsType<Models.ScanJob>(_service.GetJob(jobId));
        Assert.NotNull(job.CompletedAt);
        Assert.NotNull(job.ScanDuration);
        Assert.True(job.ScanDuration >= TimeSpan.Zero);
    }

    [Fact]
    public void CleanupOldJobsOnlyRemovesExpiredJobs()
    {
        var oldId = _service.CreateJob("old.txt", 1);
        var currentId = _service.CreateJob("current.txt", 1);
        _service.GetJob(oldId)!.CreatedAt = DateTime.UtcNow.AddHours(-2);

        _service.CleanupOldJobs(TimeSpan.FromHours(1));

        Assert.Null(_service.GetJob(oldId));
        Assert.NotNull(_service.GetJob(currentId));
    }

    [Fact]
    public void GetAllJobsReturnsNewestFirst()
    {
        var olderId = _service.CreateJob("older.txt", 1);
        var newerId = _service.CreateJob("newer.txt", 1);
        _service.GetJob(olderId)!.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        _service.GetJob(newerId)!.CreatedAt = DateTime.UtcNow;

        var jobs = _service.GetAllJobs().ToList();

        Assert.Equal(new[] { newerId, olderId }, jobs.Select(job => job.JobId));
    }
}
