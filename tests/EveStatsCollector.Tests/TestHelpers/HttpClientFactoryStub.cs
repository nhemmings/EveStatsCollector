namespace EveStatsCollector.Tests.TestHelpers;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> that returns a single pre-configured HttpClient
/// built around our deterministic <see cref="StubHttpMessageHandler"/>.
/// </summary>
public sealed class HttpClientFactoryStub : IHttpClientFactory
{
    private readonly HttpClient _client;

    public HttpClientFactoryStub(StubHttpMessageHandler handler, string baseAddress = "https://esi.evetech.net/latest/")
    {
        _client = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
    }

    public HttpClient CreateClient(string name) => _client;
}
