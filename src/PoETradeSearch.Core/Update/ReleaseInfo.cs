using System.Text.Json.Nodes;

namespace PoETradeSearch.Core.Update;

/// <summary>The newest GitHub release and its zip (from /repos/{owner}/{repo}/releases/latest).</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string ZipUrl, long ZipSize, string? Sha256)
{
    public const string Repository = "gomdolit-jeong/PoE-TradeSearch";
    public const string LatestApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{Repository}/releases/latest";

    /// <summary>"v0.7.2" → 0.7.2. Pre-release or odd tags ("v0.8.0-beta") give null and are ignored.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var v) ? Normalize(v) : null;
    }

    /// <summary>Compares major.minor.build only, so 0.7.1 and 0.7.1.0 are the same version.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Normalize(current);

    /// <summary>Reads the GitHub API response. Null when it isn't a usable release (draft, pre-release, no zip).</summary>
    public static ReleaseInfo? Parse(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
            return null;
        if (root["draft"]?.GetValue<bool>() == true || root["prerelease"]?.GetValue<bool>() == true)
            return null;

        var tag = root["tag_name"]?.GetValue<string>();
        if (ParseTag(tag) is not { } version)
            return null;

        var zip = (root["assets"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(a => a["name"]?.GetValue<string>() is { } n
                                 && n.StartsWith("PoETradeSearch", StringComparison.OrdinalIgnoreCase)
                                 && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (zip?["browser_download_url"]?.GetValue<string>() is not { } zipUrl)
            return null;

        // GitHub gives "sha256:<hex>" for each uploaded file.
        var digest = zip["digest"]?.GetValue<string>();
        string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..].ToLowerInvariant()
            : null;

        return new ReleaseInfo(version, tag!, root["html_url"]?.GetValue<string>() ?? ReleasesPageUrl, zipUrl,
                               zip["size"]?.GetValue<long>() ?? 0, sha);
    }
}
