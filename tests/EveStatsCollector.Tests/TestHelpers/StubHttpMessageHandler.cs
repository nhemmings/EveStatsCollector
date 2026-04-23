using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace EveStatsCollector.Tests.TestHelpers;

/// <summary>
/// Deterministic HTTP handler that records every outgoing request and returns a queue
/// of scripted responses. Avoids Moq's Protected() reflection for ease of debugging.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public int RequestCount => Requests.Count;

    public void EnqueueResponse(HttpResponseMessage response) =>
        _responders.Enqueue(_ => response);

    public void EnqueueResponder(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responders.Enqueue(responder);

    public void EnqueueJson<T>(T payload, HttpStatusCode status = HttpStatusCode.OK,
        string? etag = null, DateTimeOffset? expires = null, DateTimeOffset? lastModified = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        EnqueueResponder(_ =>
        {
            var response = new HttpResponseMessage(status);
            var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
            });
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (etag is not null)
                response.Headers.TryAddWithoutValidation("ETag", etag);
            if (expires is not null)
                response.Content.Headers.Expires = expires;
            if (lastModified is not null)
                response.Content.Headers.LastModified = lastModified;
            if (extraHeaders is not null)
                foreach (var kv in extraHeaders)
                    response.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            return response;
        });
    }

    public void EnqueueStatus(HttpStatusCode status, IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? retryAfter = null, string? etag = null)
    {
        EnqueueResponder(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(string.Empty)
            };
            if (retryAfter is not null)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            if (etag is not null)
                response.Headers.TryAddWithoutValidation("ETag", etag);
            if (headers is not null)
                foreach (var kv in headers)
                    response.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            return response;
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Capture a snapshot (the original message may be disposed by the caller).
        var snapshot = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            snapshot.Headers.TryAddWithoutValidation(header.Key, header.Value);
        Requests.Enqueue(snapshot);

        if (_responders.Count == 0)
            throw new InvalidOperationException(
                $"No scripted response for {request.Method} {request.RequestUri}");

        return Task.FromResult(_responders.Dequeue().Invoke(request));
    }
}
