using System.Net;
using System.Net.Http.Headers;

namespace PoETradeSearch.Core.Trade;

/// <summary>Thrown instead of sending a request that would break GGG's rate limit.</summary>
public sealed class RateLimitedException(TimeSpan wait)
    : Exception($"Rate limited, retry in {Math.Ceiling(wait.TotalSeconds)}s.")
{
    public TimeSpan Wait { get; } = wait;
}

/// <summary>
/// Follows the X-Rate-Limit-* headers the trade API returns for each policy (search, fetch, ...).
/// <para>
/// "X-Rate-Limit-Ip: 5:10:60,15:60:300" means at most 5 requests per 10 s (else a 60 s ban), 15 per 60 s, ...
/// "X-Rate-Limit-Ip-State: 1:10:0,1:60:0" is how many requests were already made in each window and any active ban.
/// When a window is full we stop sending until it has passed, so the user never gets banned.
/// </para>
/// </summary>
public sealed class RateLimiter
{
    /// <summary>Short waits are absorbed silently; longer ones are reported to the user.</summary>
    public static readonly TimeSpan MaxSilentWait = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, DateTimeOffset> _blockedUntil = new();
    private readonly Func<DateTimeOffset> _now;

    public RateLimiter(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task WaitAsync(string policy, CancellationToken ct = default)
    {
        TimeSpan wait;
        lock (_blockedUntil)
            wait = _blockedUntil.TryGetValue(policy, out var until) ? until - _now() : TimeSpan.Zero;

        if (wait <= TimeSpan.Zero)
            return;
        if (wait > MaxSilentWait)
            throw new RateLimitedException(wait);
        await Task.Delay(wait, ct);
    }

    public void Update(string policy, HttpStatusCode status, HttpResponseHeaders headers)
    {
        var block = TimeSpan.Zero;

        if (status == HttpStatusCode.TooManyRequests)
        {
            var retry = headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
            block = Max(block, retry);
        }

        foreach (var rule in RuleNames(headers))
        {
            var limits = Header(headers, $"X-Rate-Limit-{rule}");
            var state = Header(headers, $"X-Rate-Limit-{rule}-State");
            if (limits is null || state is null)
                continue;
            block = Max(block, ComputeBlock(limits, state));
        }

        if (block <= TimeSpan.Zero)
            return;
        lock (_blockedUntil)
        {
            var until = _now() + block;
            if (!_blockedUntil.TryGetValue(policy, out var current) || current < until)
                _blockedUntil[policy] = until;
        }
    }

    /// <summary>How long to wait given "max:period:penalty,..." limits and "count:period:activeBan,..." state.</summary>
    public static TimeSpan ComputeBlock(string limits, string state)
    {
        var limitParts = limits.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stateParts = state.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var block = TimeSpan.Zero;

        for (int i = 0; i < Math.Min(limitParts.Length, stateParts.Length); i++)
        {
            var l = limitParts[i].Split(':');
            var s = stateParts[i].Split(':');
            if (l.Length < 3 || s.Length < 3)
                continue;
            if (!int.TryParse(l[0], out var max) || !int.TryParse(l[1], out var period) ||
                !int.TryParse(s[0], out var count) || !int.TryParse(s[2], out var activeBan))
                continue;

            if (activeBan > 0)
                block = Max(block, TimeSpan.FromSeconds(activeBan));
            else if (count >= max)
                block = Max(block, TimeSpan.FromSeconds(period));
        }
        return block;
    }

    private static IEnumerable<string> RuleNames(HttpResponseHeaders headers) =>
        (Header(headers, "X-Rate-Limit-Rules") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Header(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
