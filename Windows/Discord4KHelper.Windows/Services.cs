using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Discord4KHelper.Windows;

internal static class Distribution
{
    internal const string Repository = "TOTO-168/discord-4k-helper";
    internal const string WindowsAsset = "Discord-4K-Helper-Windows-x64.exe";
    internal const string ApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    internal const string VencordInstallerUrl =
        "https://github.com/Vencord/Installer/releases/latest/download/VencordInstallerCli.exe";
}

internal sealed record AppVersion(IReadOnlyList<int> Parts) : IComparable<AppVersion>
{
    internal static AppVersion Parse(string value)
    {
        if (!Regex.IsMatch(value, @"\A[vV]?[0-9]+(?:\.[0-9]+){0,3}\z"))
            throw new FormatException("版本編號格式錯誤。");
        var core = value.TrimStart('v', 'V');
        var pieces = core.Split('.');
        if (pieces.Length == 0 || pieces.Any(piece => !int.TryParse(piece, out _)))
            throw new FormatException("版本編號格式錯誤。");
        return new AppVersion(pieces.Select(int.Parse).ToArray());
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        for (var index = 0; index < Math.Max(Parts.Count, other.Parts.Count); index++)
        {
            var left = index < Parts.Count ? Parts[index] : 0;
            var right = index < other.Parts.Count ? other.Parts[index] : 0;
            if (left != right) return left.CompareTo(right);
        }
        return 0;
    }

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;
    public override string ToString() => string.Join('.', Parts);
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public required string TagName { get; init; }

    [JsonPropertyName("assets")]
    public required List<GitHubAsset> Assets { get; init; }

    [JsonIgnore]
    public AppVersion? Version
    {
        get
        {
            try { return AppVersion.Parse(TagName); }
            catch { return null; }
        }
    }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("browser_download_url")]
    public required Uri DownloadUrl { get; init; }
}

internal static class Web
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var version = typeof(Web).Assembly.GetName().Version?.ToString(3) ?? "2.2.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Discord4KHelper", version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal static async Task<T> GetJsonAsync<T>(Uri uri)
    {
        ValidateGitHubUrl(uri);
        using var response = await Client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidDataException("GitHub 回傳了空白資料。");
    }

    internal static async Task DownloadAsync(Uri uri, string destination)
    {
        ValidateGitHubUrl(uri);
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destination);
        await input.CopyToAsync(output);
    }

    private static void ValidateGitHubUrl(Uri uri)
    {
        var trustedHosts = new[] { "api.github.com", "github.com", "objects.githubusercontent.com" };
        if (uri.Scheme != Uri.UriSchemeHttps || !trustedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("下載來源不受信任。");
    }
}

internal static class DiscordService
{
    private static readonly string DiscordRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");

    internal static bool IsInstalled => File.Exists(UpdateExe) || FindDiscordExe() is not null;
    private static string UpdateExe => Path.Combine(DiscordRoot, "Update.exe");

    internal static async Task QuitAsync()
    {
        var processes = Process.GetProcessesByName("Discord");
        foreach (var process in processes)
        {
            try { process.CloseMainWindow(); }
            catch { /* process may already be exiting */ }
        }

        await Task.Delay(1500);
        foreach (var process in Process.GetProcessesByName("Discord"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch { /* process may have exited between checks */ }
        }
        if (Process.GetProcessesByName("Discord").Length != 0)
            throw new InvalidOperationException("Discord 尚未完全結束，請手動結束後再試一次。");
    }

    internal static void Launch()
    {
        if (File.Exists(UpdateExe))
        {
            Process.Start(new ProcessStartInfo(UpdateExe, "--processStart Discord.exe") { UseShellExecute = true });
            return;
        }

        var executable = FindDiscordExe()
            ?? throw new InvalidOperationException("找不到 Discord，請先安裝 Discord Desktop。");
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    internal static string? FindDiscordExe(string? root = null)
    {
        root ??= DiscordRoot;
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root, "app-*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(directory => Version.TryParse(Path.GetFileName(directory)[4..], out var version) ? version : new Version())
            .Select(directory => Path.Combine(directory, "Discord.exe"))
            .FirstOrDefault(File.Exists);
    }
}

internal static class VencordService
{
    internal static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vencord");
    internal static bool IsInstalled => IsInstalledAt(Root);

    internal static bool IsInstalledAt(string root) =>
        new[] { "patcher.js", "preload.js", "renderer.js", "renderer.css", "package.json" }
            .All(name => { var file = new FileInfo(Path.Combine(root, "dist", name)); return file.Exists && file.Length > 0; });

    internal static async Task MigrateLegacyBuildAsync(string root, Func<Task> reinstall)
    {
        var dist = Path.Combine(root, "dist");
        if (!File.Exists(Path.Combine(dist, ".soundcloner-manifest.json"))) return;
        var previous = Path.Combine(root, $"dist-before-migration-{Guid.NewGuid():N}");
        Directory.Move(dist, previous);
        try
        {
            await reinstall();
            if (!IsInstalledAt(root)) throw new InvalidDataException("Vencord 安裝未完成，請稍後再試。");
        }
        catch
        {
            if (Directory.Exists(dist)) Directory.Delete(dist, true);
            Directory.Move(previous, dist);
            throw;
        }
        try { Directory.Delete(previous, true); } catch { /* Preserve the backup if cleanup fails. */ }
    }

    internal static async Task InstallAsync()
    {
        var installer = Path.Combine(Path.GetTempPath(), $"VencordInstallerCli-{Guid.NewGuid():N}.exe");
        try
        {
            await Web.DownloadAsync(new Uri(Distribution.VencordInstallerUrl), installer);
            ValidateExecutable(installer);
            await ProcessTools.RunAsync(installer, "--install", "--branch", "stable");
        }
        finally
        {
            try { File.Delete(installer); } catch { }
        }
    }

    internal static void ValidateExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("下載的 Windows 程式格式無法驗證。");
    }
}

internal static class VencordSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vencord", "settings");
    internal static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    internal static bool IsBypassEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
            return root?["plugins"]?["FakeNitro"]?["enabled"]?.GetValue<bool>() == true
                && root?["plugins"]?["FakeNitro"]?["enableStreamQualityBypass"]?.GetValue<bool>() == true;
        }
        catch { return false; }
    }

    internal static void Update(bool enabled) => UpdateFile(SettingsPath, enabled, createBackup: true);

    internal static string Edit(string json, bool enabled, bool removeLegacySoundCloner = false)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root is null) throw new InvalidDataException("Vencord 設定檔格式無法辨識。");
        if (root.ContainsKey("plugins") && root["plugins"] is not JsonObject) throw new InvalidDataException("Vencord 設定檔格式無法辨識。");

        var plugins = root["plugins"] as JsonObject;
        if (plugins is null)
        {
            plugins = new JsonObject();
            root["plugins"] = plugins;
        }

        if (plugins.ContainsKey("FakeNitro") && plugins["FakeNitro"] is not JsonObject) throw new InvalidDataException("Vencord 設定檔格式無法辨識。");
        var fakeNitro = plugins["FakeNitro"] as JsonObject;
        if (fakeNitro is null) { fakeNitro = new JsonObject(); plugins["FakeNitro"] = fakeNitro; }
        if (enabled) fakeNitro["enabled"] = true;
        fakeNitro["enableStreamQualityBypass"] = enabled;
        if (removeLegacySoundCloner) plugins.Remove("SoundCloner");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    internal static void UpdateFile(string path, bool enabled, bool createBackup, bool removeLegacySoundCloner = false)
    {
        var existed = File.Exists(path);
        var updated = Edit(existed ? File.ReadAllText(path) : "{}", enabled, removeLegacySoundCloner);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var backup = Path.Combine(directory, "settings.before-discord-4k-helper.json");
        if (createBackup && existed && !File.Exists(backup)) File.Copy(path, backup);
        WriteAtomic(path, updated);
    }

    internal static void WriteAtomic(string path, string text)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $"settings-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllText(temporary, text); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class ProcessTools
{
    internal static async Task<string> RunAsync(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("無法啟動外部程式。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask + await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(output) ? "外部程式執行失敗。" : output[^Math.Min(800, output.Length)..]);
        return output;
    }
}

internal static class UpdateService
{
    internal static Task<GitHubRelease> GetLatestReleaseAsync() =>
        Web.GetJsonAsync<GitHubRelease>(new Uri(Distribution.ApiUrl));

    internal static async Task InstallAsync(GitHubRelease release)
    {
        var asset = release.Assets.FirstOrDefault(item => item.Name == Distribution.WindowsAsset)
            ?? throw new InvalidOperationException("這個版本沒有相容的 Windows 更新檔。");
        var replacement = Path.Combine(Path.GetTempPath(), $"Discord4KHelper-update-{Guid.NewGuid():N}.exe");
        var updaterStarted = false;
        try
        {
            await Web.DownloadAsync(asset.DownloadUrl, replacement);
            VencordService.ValidateExecutable(replacement);
            var downloadedVersion = FileVersionInfo.GetVersionInfo(replacement).FileVersion;
            if (release.Version is null || downloadedVersion is null ||
                AppVersion.Parse(downloadedVersion).CompareTo(release.Version) != 0)
                throw new InvalidDataException("下載的更新版本無法驗證。");

            var target = Environment.ProcessPath
                ?? throw new InvalidOperationException("找不到目前程式路徑。");
            EnsureDirectoryIsWritable(Path.GetDirectoryName(target)!);
            StartUpdater(target, replacement);
            updaterStarted = true;
        }
        finally
        {
            if (!updaterStarted) { try { File.Delete(replacement); } catch { } }
        }
    }

    private static void EnsureDirectoryIsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".discord-4k-write-test-{Guid.NewGuid():N}");
        try { File.WriteAllText(probe, "ok"); }
        catch { throw new UnauthorizedAccessException("目前程式所在資料夾無法寫入，請移到桌面後再更新。"); }
        finally { try { File.Delete(probe); } catch { } }
    }

    internal const string UpdaterScript = """
            param([string]$Target, [string]$Replacement, [int]$ParentProcessId)
            Wait-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
            $ErrorActionPreference = "Stop"
            $Backup = "$Target.discord-4k-helper-old-$([guid]::NewGuid())"
            $BackedUp = $false
            try {
                Copy-Item -LiteralPath $Target -Destination $Backup -Force
                $BackedUp = $true
                Copy-Item -LiteralPath $Replacement -Destination $Target -Force
                Start-Process -FilePath $Target
                Remove-Item -LiteralPath $Backup -Force -ErrorAction SilentlyContinue
            } catch {
                if ($BackedUp) {
                    Copy-Item -LiteralPath $Backup -Destination $Target -Force
                    Start-Process -FilePath $Target
                    Remove-Item -LiteralPath $Backup -Force -ErrorAction SilentlyContinue
                }
            }
            Remove-Item -LiteralPath $Replacement -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            """;

    private static void StartUpdater(string target, string replacement)
    {
        var script = Path.Combine(Path.GetTempPath(), $"Discord4KHelper-updater-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, UpdaterScript, Encoding.UTF8);

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Target");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add("-Replacement");
        startInfo.ArgumentList.Add(replacement);
        startInfo.ArgumentList.Add("-ParentProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        try
        {
            using var updater = Process.Start(startInfo) ?? throw new InvalidOperationException("無法啟動更新程式。");
        }
        catch { File.Delete(script); throw; }
    }
}

internal static class SelfTest
{
    internal static void Run()
    {
        if (!(AppVersion.Parse("v2.0.0") > AppVersion.Parse("1.9.9")))
            throw new InvalidOperationException("版本比較測試失敗。");
        foreach (var invalid in new[] { "2..2", "2.", "vv2", "2v", "2.2-beta", "1.-2", "999999999999999999999999" })
        {
            try { AppVersion.Parse(invalid); throw new InvalidOperationException("接受了錯誤版本：" + invalid); }
            catch (FormatException) { }
        }

        var directory = Path.Combine(Path.GetTempPath(), $"Discord4KHelper-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settings = Path.Combine(directory, "settings.json");
            File.WriteAllText(settings, "{\"theme\":\"dark\",\"plugins\":{\"Other\":{\"enabled\":true}}}");
            var original = File.ReadAllText(settings);
            VencordSettings.UpdateFile(settings, enabled: true, createBackup: true);
            var root = JsonNode.Parse(File.ReadAllText(settings));
            if (root?["theme"]?.GetValue<string>() != "dark" ||
                root?["plugins"]?["Other"]?["enabled"]?.GetValue<bool>() != true ||
                root?["plugins"]?["FakeNitro"]?["enableStreamQualityBypass"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("設定檔測試失敗。");
            VencordSettings.UpdateFile(settings, enabled: false, createBackup: true);
            if (File.ReadAllText(Path.Combine(directory, "settings.before-discord-4k-helper.json")) != original)
                throw new InvalidOperationException("原始設定備份被覆寫。");
            var disabled = JsonNode.Parse(VencordSettings.Edit("""{"plugins":{"FakeNitro":{"enabled":false,"enableEmojiBypass":true},"SoundCloner":{"enabled":true}}}""", false));
            if (disabled?["plugins"]?["FakeNitro"]?["enabled"]?.GetValue<bool>() != false ||
                disabled?["plugins"]?["FakeNitro"]?["enableEmojiBypass"]?.GetValue<bool>() != true ||
                disabled?["plugins"]?["SoundCloner"] is null)
                throw new InvalidOperationException("關閉畫質繞過改動了其他功能。");
            var migrated = JsonNode.Parse(VencordSettings.Edit(disabled!.ToJsonString(), true, removeLegacySoundCloner: true));
            if (migrated?["plugins"]?["SoundCloner"] is not null)
                throw new InvalidOperationException("舊音效設定未清除。");
            foreach (var json in new[] { "[]", "null", "{\"plugins\":[]}", "{\"plugins\":null}", "{\"plugins\":{\"FakeNitro\":false}}" })
            {
                File.WriteAllText(settings, json);
                try { VencordSettings.UpdateFile(settings, true, false); throw new InvalidOperationException("錯誤設定被接受。"); }
                catch (InvalidDataException) { }
                if (File.ReadAllText(settings) != json) throw new InvalidOperationException("錯誤設定被覆寫。");
            }

            var dist = Path.Combine(directory, "dist");
            Directory.CreateDirectory(dist);
            var marker = Path.Combine(dist, ".soundcloner-manifest.json");
            File.WriteAllText(marker, "{}");
            File.WriteAllText(Path.Combine(dist, "patcher.js"), "old");
            if (VencordService.IsInstalledAt(directory)) throw new InvalidOperationException("不完整安裝被接受。");
            foreach (var throwsError in new[] { true, false })
            {
                try
                {
                    VencordService.MigrateLegacyBuildAsync(directory, () =>
                    {
                        Directory.CreateDirectory(dist);
                        File.WriteAllText(Path.Combine(dist, "patcher.js"), "partial");
                        if (throwsError) throw new InvalidDataException("Simulated download failure");
                        return Task.CompletedTask;
                    }).GetAwaiter().GetResult();
                    throw new InvalidOperationException("轉換失敗未回報。");
                }
                catch (InvalidDataException) { }
                if (File.ReadAllText(Path.Combine(dist, "patcher.js")) != "old" || !File.Exists(marker))
                    throw new InvalidOperationException("轉換失敗未還原。");
            }
            VencordService.MigrateLegacyBuildAsync(directory, () =>
            {
                Directory.CreateDirectory(dist);
                foreach (var name in new[] { "patcher.js", "preload.js", "renderer.js", "renderer.css", "package.json" })
                    File.WriteAllText(Path.Combine(dist, name), "official");
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            if (!VencordService.IsInstalledAt(directory) || File.Exists(marker))
                throw new InvalidOperationException("轉換後狀態不正確。");
            VencordService.MigrateLegacyBuildAsync(directory, () => throw new InvalidOperationException("官方建置不應再次轉換。")).GetAwaiter().GetResult();

            foreach (var version in new[] { "1.0.9", "1.0.10" })
            {
                var app = Path.Combine(directory, "app-" + version);
                Directory.CreateDirectory(app);
                File.WriteAllText(Path.Combine(app, "Discord.exe"), "test");
            }
            if (DiscordService.FindDiscordExe(directory) != Path.Combine(directory, "app-1.0.10", "Discord.exe"))
                throw new InvalidOperationException("Discord 執行檔版本排序錯誤。");

            // Execute the production PowerShell script with a failed copy; launching is stubbed.
            if (OperatingSystem.IsWindows())
            {
                var updater = Path.Combine(directory, "updater.ps1");
                var runner = Path.Combine(directory, "check-updater.ps1");
                File.WriteAllText(updater, UpdateService.UpdaterScript);
                File.WriteAllText(runner, """
                    param([string]$Directory)
                    $ErrorActionPreference = 'Continue'
                    function Start-Process { param($FilePath) }
                    function Copy-Item {
                        param($LiteralPath, $Destination, [switch]$Force)
                        if (!(Test-Path -LiteralPath $LiteralPath)) {
                            Set-Content -LiteralPath $Destination -Value 'partial' -NoNewline
                            Write-Error 'Simulated interrupted copy'
                        } else { Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force }
                    }
                    $target = Join-Path $Directory 'target.exe'
                    Set-Content -LiteralPath $target -Value 'original' -NoNewline
                    & (Join-Path $Directory 'updater.ps1') -Target $target -Replacement (Join-Path $Directory 'missing.exe') -ParentProcessId 0
                    $ErrorActionPreference = 'Stop'
                    if ((Get-Content -Raw -LiteralPath $target) -ne 'original') { throw 'Original executable lost' }
                    if (Get-ChildItem $Directory -Filter '*.discord-4k-helper-old-*') { throw 'Rollback did not finish' }
                    """);
                Task.Run(() => ProcessTools.RunAsync("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", runner, directory)).GetAwaiter().GetResult();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
