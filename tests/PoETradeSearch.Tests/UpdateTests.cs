using PoETradeSearch.Core.Update;

namespace PoETradeSearch.Tests;

public class UpdateTests
{
    private const string LatestJson = """
        {
          "tag_name": "v0.7.2", "draft": false, "prerelease": false,
          "html_url": "https://github.com/gomdolit-jeong/PoE-TradeSearch/releases/tag/v0.7.2",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://x/notes.txt", "size": 3 },
            { "name": "PoETradeSearch-0.7.2.zip", "size": 63442633,
              "digest": "sha256:FFE60A8AFA22AF3F8C2ADA51D10E14A43C58A2D5A95AC76EA1DB48E5ABBBD8BA",
              "browser_download_url": "https://github.com/gomdolit-jeong/PoE-TradeSearch/releases/download/v0.7.2/PoETradeSearch-0.7.2.zip" }
          ]
        }
        """;

    [Fact]
    public void Reads_the_zip_and_checksum_from_the_latest_release()
    {
        var r = ReleaseInfo.Parse(LatestJson)!;
        Assert.Equal(new Version(0, 7, 2), r.Version);
        Assert.EndsWith("PoETradeSearch-0.7.2.zip", r.ZipUrl);
        Assert.Equal(63442633, r.ZipSize);
        Assert.Equal("ffe60a8afa22af3f8c2ada51d10e14a43c58a2d5a95ac76ea1db48e5abbbd8ba", r.Sha256);
    }

    [Theory]
    [InlineData("0.7.1.0", true)]
    [InlineData("0.7.1", true)]
    [InlineData("0.7.2.0", false)]
    [InlineData("0.8.0", false)]
    public void Only_a_higher_version_is_an_update(string current, bool expected)
    {
        var r = ReleaseInfo.Parse(LatestJson)!;
        Assert.Equal(expected, ReleaseInfo.IsNewer(r, Version.Parse(current)));
    }

    [Theory]
    [InlineData("""{ "tag_name": "v0.8.0", "prerelease": true, "assets": [] }""")]
    [InlineData("""{ "tag_name": "v0.8.0-beta", "assets": [ { "name": "PoETradeSearch-0.8.0.zip", "browser_download_url": "u" } ] }""")]
    [InlineData("""{ "tag_name": "v0.8.0", "assets": [] }""")]
    public void Unusable_releases_are_ignored(string json) => Assert.Null(ReleaseInfo.Parse(json));
}
