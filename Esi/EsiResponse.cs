using System.Net;

namespace EveStatsCollector.Esi;

public record EsiResponse<T>(
    T? Data,
    HttpStatusCode StatusCode,
    DateTimeOffset? Expires,
    DateTimeOffset? LastModified,
    string? ETag,
    int RateLimitRemaining,
    int RateLimitUsed,
    string? RateLimitGroup
)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
    public bool IsNotModified => StatusCode == HttpStatusCode.NotModified;
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;
}
