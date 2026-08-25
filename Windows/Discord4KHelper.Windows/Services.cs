using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
        var core = value.TrimStart('v', 'V').Split('-', 2)[0];
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
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Discord4KHelper", "2.0.0"));
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

    private static string? FindDiscordExe()
    {
        if (!Directory.Exists(DiscordRoot)) return null;
        return Directory.EnumerateDirectories(DiscordRoot, "app-*", SearchOption.TopDirectoryOnly)
            .OrderDescending()
            .Select(directory => Path.Combine(directory, "Discord.exe"))
            .FirstOrDefault(File.Exists);
    }
}

internal static class VencordService
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vencord");
    internal static bool IsInstalled => File.Exists(Path.Combine(Root, "dist", "patcher.js"));

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
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

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

    internal static void UpdateFile(string path, bool enabled, bool createBackup)
    {
        var existed = File.Exists(path);
        var root = existed
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            : new JsonObject();
        if (root is null) throw new InvalidDataException("Vencord 設定檔格式無法辨識。");

        var plugins = root["plugins"] as JsonObject;
        if (plugins is null)
        {
            plugins = new JsonObject();
            root["plugins"] = plugins;
        }

        var fakeNitro = plugins["FakeNitro"] as JsonObject;
        if (fakeNitro is null)
        {
            fakeNitro = new JsonObject();
            plugins["FakeNitro"] = fakeNitro;
        }
        fakeNitro["enabled"] = true;
        fakeNitro["enableStreamQualityBypass"] = enabled;

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var backup = Path.Combine(directory, "settings.before-discord-4k-helper.json");
        if (createBackup && existed && !File.Exists(backup)) File.Copy(path, backup);
        var temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
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
        await Web.DownloadAsync(asset.DownloadUrl, replacement);
        VencordService.ValidateExecutable(replacement);

        var downloadedVersion = FileVersionInfo.GetVersionInfo(replacement).FileVersion;
        if (release.Version is null || downloadedVersion is null ||
            AppVersion.Parse(downloadedVersion).CompareTo(release.Version) != 0)
        {
            File.Delete(replacement);
            throw new InvalidDataException("下載的更新版本無法驗證。");
        }

        var target = Environment.ProcessPath
            ?? throw new InvalidOperationException("找不到目前程式路徑。");
        EnsureDirectoryIsWritable(Path.GetDirectoryName(target)!);
        StartUpdater(target, replacement);
    }

    private static void EnsureDirectoryIsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".discord-4k-write-test-{Guid.NewGuid():N}");
        try { File.WriteAllText(probe, "ok"); }
        catch { throw new UnauthorizedAccessException("目前程式所在資料夾無法寫入，請移到桌面後再更新。"); }
        finally { try { File.Delete(probe); } catch { } }
    }

    private static void StartUpdater(string target, string replacement)
    {
        var script = Path.Combine(Path.GetTempPath(), $"Discord4KHelper-updater-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, """
            param([string]$Target, [string]$Replacement, [int]$ParentProcessId)
            Wait-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
            $Backup = "$Target.discord-4k-helper-old"
            try {
                Copy-Item -LiteralPath $Target -Destination $Backup -Force
                Copy-Item -LiteralPath $Replacement -Destination $Target -Force
                Start-Process -FilePath $Target
                Remove-Item -LiteralPath $Backup -Force -ErrorAction SilentlyContinue
            } catch {
                if (Test-Path -LiteralPath $Backup) {
                    Copy-Item -LiteralPath $Backup -Destination $Target -Force
                    Start-Process -FilePath $Target
                }
            }
            Remove-Item -LiteralPath $Replacement -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            """, Encoding.UTF8);

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
        Process.Start(startInfo);
    }
}

internal static class SelfTest
{
    internal static void Run()
    {
        if (!(AppVersion.Parse("v2.0.0") > AppVersion.Parse("1.9.9")))
            throw new InvalidOperationException("版本比較測試失敗。");

        var directory = Path.Combine(Path.GetTempPath(), $"Discord4KHelper-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settings = Path.Combine(directory, "settings.json");
            File.WriteAllText(settings, "{\"theme\":\"dark\",\"plugins\":{\"Other\":{\"enabled\":true}}}");
            VencordSettings.UpdateFile(settings, enabled: true, createBackup: false);
            var root = JsonNode.Parse(File.ReadAllText(settings));
            if (root?["theme"]?.GetValue<string>() != "dark" ||
                root?["plugins"]?["Other"]?["enabled"]?.GetValue<bool>() != true ||
                root?["plugins"]?["FakeNitro"]?["enableStreamQualityBypass"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("設定檔測試失敗。");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
