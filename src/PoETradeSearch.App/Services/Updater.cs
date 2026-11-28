using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using PoETradeSearch.Core.Update;

namespace PoETradeSearch.App.Services;

/// <summary>
/// Checks GitHub for a newer release and installs it when the user asks.
/// The running exe can't be overwritten, but it can be renamed: it becomes PoETradeSearch.exe.old, the new exe
/// takes its place and is started; the new process waits for this one to exit and deletes the .old file.
/// </summary>
public static class Updater
{
    private const string AfterUpdateArg = "--after-update";

    // Must come before Http: static fields are set in this order and the client's User-Agent uses the version.
    public static Version CurrentVersion { get; } =
        ReleaseInfo.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    private static readonly HttpClient Http = CreateHttpClient();

    private static string ExePath => Environment.ProcessPath!;
    private static string OldExePath => ExePath + ".old";
    private static string WorkDirectory => Path.Combine(TradeDataCache.CacheDirectory, "update");

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoETradeSearch/" + CurrentVersion); // GitHub requires one
        return http;
    }

    /// <summary>The newest release if it is newer than this exe; null if not, or if GitHub can't be reached.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var json = await Http.GetStringAsync(ReleaseInfo.LatestApiUrl, cts.Token);
            var release = ReleaseInfo.Parse(json);
            return release is not null && ReleaseInfo.IsNewer(release, CurrentVersion) ? release : null;
        }
        catch (Exception ex)
        {
            Log.Warn("업데이트 확인 실패: " + ex.Message);
            return null;
        }
    }

    /// <summary>False when the exe sits in a folder this user can't write to (e.g. Program Files).</summary>
    public static bool CanReplaceExe()
    {
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(ExePath)!, $".update-test-{Environment.ProcessId}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads and checks the zip, swaps the exe and starts the new one. The caller shuts the app down afterwards.
    /// </summary>
    /// <param name="progress">0.0–1.0 while downloading.</param>
    public static async Task InstallAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(WorkDirectory);
        var zipPath = Path.Combine(WorkDirectory, $"PoETradeSearch-{release.Version}.zip");
        var newExe = Path.Combine(WorkDirectory, "PoETradeSearch.exe.new");

        await DownloadAsync(release, zipPath, progress, ct);
        await VerifyAsync(release, zipPath, ct);

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("PoETradeSearch.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("받은 파일에 PoETradeSearch.exe가 없습니다");
            entry.ExtractToFile(newExe, overwrite: true);
        }

        // Swap: rename the running exe out of the way, put the new one in its place, roll back if that fails.
        if (File.Exists(OldExePath))
            File.Delete(OldExePath);
        File.Move(ExePath, OldExePath);
        try
        {
            File.Move(newExe, ExePath);
        }
        catch
        {
            File.Move(OldExePath, ExePath);
            throw;
        }

        Process.Start(new ProcessStartInfo(ExePath, $"{AfterUpdateArg} {Environment.ProcessId}") { UseShellExecute = false });
    }

    private static async Task DownloadAsync(ReleaseInfo release, string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? release.ZipSize;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0)
                progress?.Report(Math.Min(1.0, (double)done / total));
        }
    }

    private static async Task VerifyAsync(ReleaseInfo release, string path, CancellationToken ct)
    {
        if (release.ZipSize > 0 && new FileInfo(path).Length != release.ZipSize)
            throw new InvalidDataException("받은 파일 크기가 맞지 않습니다. 다시 시도해 주세요.");
        if (release.Sha256 is null)
            return;
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        if (hash != release.Sha256)
            throw new InvalidDataException("받은 파일이 GitHub에 올라간 파일과 다릅니다. 다시 시도해 주세요.");
    }

    /// <summary>
    /// Called at startup. After an update: waits for the old process to exit (it still holds the hotkey) and
    /// removes the old exe and the downloaded files.
    /// </summary>
    /// <returns>True if this start is right after an update.</returns>
    public static bool FinishPendingUpdate(string[] args)
    {
        bool afterUpdate = false;
        int i = Array.IndexOf(args, AfterUpdateArg);
        if (i >= 0)
        {
            afterUpdate = true;
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
            {
                try
                {
                    using var old = Process.GetProcessById(pid);
                    old.WaitForExit(15_000);
                }
                catch
                {
                    // Already gone.
                }
            }
        }

        if (File.Exists(OldExePath))
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    File.Delete(OldExePath);
                    break;
                }
                catch
                {
                    Thread.Sleep(300);
                }
            }
        }

        try
        {
            if (Directory.Exists(WorkDirectory))
                Directory.Delete(WorkDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warn("업데이트 임시 파일 삭제 실패: " + ex.Message);
        }
        return afterUpdate;
    }
}
