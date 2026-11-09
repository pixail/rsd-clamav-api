using GovUK.Dfe.ClamAV.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;

namespace GovUK.Dfe.ClamAV.Tests;

public class ApiContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task HealthEndpointReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body?.Status);
    }

    [Fact]
    public async Task UnknownJobReturnsNotFound()
    {
        var response = await _client.GetAsync("/scan/async/does-not-exist");
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Job not found", body?.Error);
    }

    [Fact]
    public async Task JobsEndpointStartsEmpty()
    {
        var response = await _client.GetFromJsonAsync<JobsListResponse>("/scan/jobs");

        Assert.NotNull(response);
        Assert.Equal(0, response.Count);
        Assert.Empty(response.Jobs);
    }

    [Fact]
    public async Task InvalidUrlRequestReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/scan/async/url", new ScanUrlRequest { Url = "file:///etc/passwd" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerDocumentIsAvailable()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
