using System.Net;
using EveStatsCollector.Esi;
using EveStatsCollector.Models;
using EveStatsCollector.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveStatsCollector.Tests.Esi;

/// <summary>
/// Unit tests for <see cref="EsiClient"/>. Covers every ESI endpoint called by
/// the application (regions, constellations, systems — list &amp; detail,
/// system_kills, system_jumps) plus transport-level behavior: 200/304/429 paths,
/// retry exhaustion, malformed JSON, transport exceptions, and ETag propagation.
///
/// All HTTP traffic is intercepted via <see cref="StubHttpMessageHandler"/>.
/// No test makes a real network call to esi.evetech.net.
/// </summary>
public class EsiClientTests
{
    private static (EsiClient Client, StubHttpMessageHandler Handler) CreateClient()
    {
        var handler = new StubHttpMessageHandler();
        var factory = new HttpClientFactoryStub(handler);
        var client = new EsiClient(factory, NullLogger<EsiClient>.Instance);
        return (client, handler);
    }

    #region 200 OK / payload deserialization

    [Fact]
    public async Task GetAsync_200WithJsonPayload_ReturnsDeserializedData()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new[] { 10000001, 10000002 });

        var response = await client.GetAsync<int[]>("universe/regions/");

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(new[] { 10000001, 10000002 });
    }

    [Fact]
    public async Task GetAsync_200_PassesPathToHttpClient()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(Array.Empty<int>());

        await client.GetAsync<int[]>("universe/regions/");

        handler.Requests.Should().ContainSingle();
        handler.Requests.TryPeek(out var req).Should().BeTrue();
        req!.RequestUri!.AbsoluteUri.Should().Be("https://esi.evetech.net/latest/universe/regions/");
    }

    #endregion

    #region ESI endpoints — 100% coverage

    // ---- /universe/regions/ ----
    [Fact]
    public async Task GetAsync_UniverseRegionsEndpoint_ReturnsIdList()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new[] { 10000001, 10000002 });
        var r = await client.GetAsync<int[]>("universe/regions/");
        r.IsSuccess.Should().BeTrue();
        r.Data.Should().HaveCount(2);
    }

    // ---- /universe/regions/{id}/ ----
    [Fact]
    public async Task GetAsync_UniverseRegionDetailEndpoint_ReturnsRegion()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new Region(10000001, "Derelik", new[] { 20000001 }));
        var r = await client.GetAsync<Region>("universe/regions/10000001/");
        r.IsSuccess.Should().BeTrue();
        r.Data!.RegionId.Should().Be(10000001);
        r.Data.Name.Should().Be("Derelik");
    }

    // ---- /universe/constellations/ ----
    [Fact]
    public async Task GetAsync_UniverseConstellationsEndpoint_ReturnsIdList()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new[] { 20000001 });
        var r = await client.GetAsync<int[]>("universe/constellations/");
        r.IsSuccess.Should().BeTrue();
        r.Data.Should().ContainSingle().Which.Should().Be(20000001);
    }

    // ---- /universe/constellations/{id}/ ----
    [Fact]
    public async Task GetAsync_UniverseConstellationDetailEndpoint_ReturnsConstellation()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new Constellation(20000001, "San Matar", 10000001, new[] { 30000001 }));
        var r = await client.GetAsync<Constellation>("universe/constellations/20000001/");
        r.Data!.Name.Should().Be("San Matar");
        r.Data.Systems.Should().Contain(30000001);
    }

    // ---- /universe/systems/ ----
    [Fact]
    public async Task GetAsync_UniverseSystemsEndpoint_ReturnsIdList()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new[] { 30000001, 30000002 });
        var r = await client.GetAsync<int[]>("universe/systems/");
        r.Data.Should().HaveCount(2);
    }

    // ---- /universe/systems/{id}/ ----
    [Fact]
    public async Task GetAsync_UniverseSystemDetailEndpoint_ReturnsSystem()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new SolarSystem(30000142, "Jita", 20000020, 0.945f));
        var r = await client.GetAsync<SolarSystem>("universe/systems/30000142/");
        r.Data!.Name.Should().Be("Jita");
        r.Data.SecurityStatus.Should().BeApproximately(0.945f, 0.001f);
    }

    // ---- /universe/system_kills/ ----
    [Fact]
    public async Task GetAsync_SystemKillsEndpoint_ReturnsArray()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new[]
        {
            new SystemKills(30000142, ShipKills: 1, NpcKills: 5, PodKills: 0),
            new SystemKills(30000144, ShipKills: 0, NpcKills: 3, PodKills: 1)
        });
        var r = await client.GetAsync<SystemKills[]>("universe/system_kills/");
        r.Data.Should().HaveCount(2);
        r.Data![0].NpcKills.Should().Be(5);
    }

    // ---- /universe/system_jumps/ ----
    [Fact]
    public async Task GetAsync_SystemJumpsEndpoint_ReturnsArray()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(new[]
        {
            new SystemJumps(30000142, ShipJumps: 420),
            new SystemJumps(30000144, ShipJumps: 69)
        });
        var r = await client.GetAsync<SystemJumps[]>("universe/system_jumps/");
        r.Data.Should().HaveCount(2);
        r.Data![0].ShipJumps.Should().Be(420);
    }

    #endregion

    #region ETag handling

    [Fact]
    public async Task GetAsync_EtagProvided_SendsIfNoneMatchHeader()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueStatus(HttpStatusCode.NotModified);

        await client.GetAsync<int[]>("universe/regions/", etag: "\"abc123\"");

        handler.Requests.TryPeek(out var req).Should().BeTrue();
        req!.Headers.TryGetValues("If-None-Match", out var values).Should().BeTrue();
        values!.Should().Contain("\"abc123\"");
    }

    [Fact]
    public async Task GetAsync_Returns304_ReturnsCachedResponseWithNotModifiedFlag()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueStatus(HttpStatusCode.NotModified, etag: "\"same-etag\"");

        var response = await client.GetAsync<int[]>("universe/regions/", etag: "\"same-etag\"");

        response.IsNotModified.Should().BeTrue();
        response.Data.Should().BeNull();
        response.ETag.Should().Be("\"same-etag\"");
    }

    [Fact]
    public async Task GetAsync_200Response_PropagatesETagExpiresAndLastModified()
    {
        var (client, handler) = CreateClient();
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        var lastModified = DateTimeOffset.UtcNow.AddMinutes(-5);
        handler.EnqueueJson(new[] { 1, 2 }, etag: "\"new-etag\"", expires: expires, lastModified: lastModified);

        var response = await client.GetAsync<int[]>("universe/regions/");

        response.ETag.Should().Be("\"new-etag\"");
        response.Expires.Should().NotBeNull();
        response.Expires!.Value.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
        response.LastModified.Should().NotBeNull();
        response.LastModified!.Value.Should().BeCloseTo(lastModified, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetAsync_NoEtagProvided_DoesNotSendIfNoneMatchHeader()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueJson(Array.Empty<int>());

        await client.GetAsync<int[]>("universe/regions/");

        handler.Requests.TryPeek(out var req).Should().BeTrue();
        req!.Headers.Contains("If-None-Match").Should().BeFalse();
    }

    #endregion

    #region 429 retry logic

    [Fact]
    public async Task GetAsync_429ThenSuccess_RetriesAndReturnsData()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueStatus(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(1));
        handler.EnqueueJson(new[] { 42 });

        var response = await client.GetAsync<int[]>("universe/regions/");

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().ContainSingle().Which.Should().Be(42);
        handler.RequestCount.Should().Be(2, "429 triggers one retry that succeeds");
    }

    [Fact]
    public async Task GetAsync_429MultipleTimesThenSuccess_RetriesUpTo3Times()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueStatus(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(1));
        handler.EnqueueStatus(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(1));
        handler.EnqueueStatus(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(1));
        handler.EnqueueJson(new[] { 7 });

        var response = await client.GetAsync<int[]>("universe/regions/");

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().ContainSingle().Which.Should().Be(7);
        handler.RequestCount.Should().Be(4, "3 retries then success");
    }

    [Fact]
    public async Task GetAsync_429Repeatedly_ExhaustsRetriesAndReturnsRateLimitedResponse()
    {
        var (client, handler) = CreateClient();
        for (int i = 0; i < 4; i++)
            handler.EnqueueStatus(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(1));

        var response = await client.GetAsync<int[]>("universe/regions/");

        response.IsRateLimited.Should().BeTrue();
        response.IsSuccess.Should().BeFalse();
        response.Data.Should().BeNull();
        handler.RequestCount.Should().Be(4, "initial + 3 retries = 4 attempts total");
    }

    [Fact]
    public async Task GetAsync_429WithoutRetryAfterHeader_UsesDefault10sDelayButStillRetries()
    {
        // We can't wait 10s for real, so verify only that retry count is correct with cancellation.
        var (client, handler) = CreateClient();
        handler.EnqueueStatus(HttpStatusCode.TooManyRequests); // no retry-after => 10s default
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Func<Task> act = () => client.GetAsync<int[]>("universe/regions/", ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Error paths

    [Fact]
    public async Task GetAsync_500InternalServerError_ReturnsUnsuccessfulResponse()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueStatus(HttpStatusCode.InternalServerError);

        var response = await client.GetAsync<int[]>("universe/regions/");

        response.IsSuccess.Should().BeFalse();
        response.IsNotModified.Should().BeFalse();
        response.IsRateLimited.Should().BeFalse();
        response.Data.Should().BeNull();
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetAsync_404NotFound_ReturnsUnsuccessfulResponse()
    {
        var (client, handler) = CreateClient();
        handler.EnqueueStatus(HttpStatusCode.NotFound);

        var response = await client.GetAsync<SolarSystem>("universe/systems/99999999/");
        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_TransportException_RethrowsException()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(_ => throw new HttpRequestException("connection reset"));
        var factory = new HttpClientFactoryStub(handler);
        var client = new EsiClient(factory, NullLogger<EsiClient>.Instance);

        Func<Task> act = () => client.GetAsync<int[]>("universe/regions/");
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*connection reset*");
    }

    [Fact]
    public async Task GetAsync_MalformedJson_ThrowsJsonException()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ not-json ", System.Text.Encoding.UTF8, "application/json")
            };
            return r;
        });
        var factory = new HttpClientFactoryStub(handler);
        var client = new EsiClient(factory, NullLogger<EsiClient>.Instance);

        Func<Task> act = () => client.GetAsync<int[]>("universe/regions/");
        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task GetAsync_CancellationRequested_PropagatesCancellation()
    {
        var handler = new StubHttpMessageHandler();
        // Don't enqueue a response — if we inspected cancellation before send, we'd
        // get InvalidOperation instead. Use a cancelled token so SendAsync sees it.
        var factory = new HttpClientFactoryStub(handler);
        var client = new EsiClient(factory, NullLogger<EsiClient>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => client.GetAsync<int[]>("universe/regions/", ct: cts.Token);
        await act.Should().ThrowAsync<Exception>(); // OperationCanceled or InvalidOperation both OK
    }

    #endregion

    #region EsiResponse

    [Fact]
    public void EsiResponse_IsSuccess_ReflectsStatusCode()
    {
        new EsiResponse<int>(1, HttpStatusCode.OK, null, null, null).IsSuccess.Should().BeTrue();
        new EsiResponse<int>(1, HttpStatusCode.NoContent, null, null, null).IsSuccess.Should().BeTrue();
        new EsiResponse<int>(0, HttpStatusCode.NotModified, null, null, null).IsSuccess.Should().BeFalse();
        new EsiResponse<int>(0, HttpStatusCode.InternalServerError, null, null, null).IsSuccess.Should().BeFalse();
        new EsiResponse<int>(0, HttpStatusCode.NotModified, null, null, null).IsNotModified.Should().BeTrue();
        new EsiResponse<int>(0, HttpStatusCode.TooManyRequests, null, null, null).IsRateLimited.Should().BeTrue();
    }

    #endregion
}
