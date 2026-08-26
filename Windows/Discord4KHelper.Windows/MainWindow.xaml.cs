using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;

namespace Discord4KHelper.Windows;

public partial class MainWindow : Window
{
    private readonly SolidColorBrush _success = (SolidColorBrush)Application.Current.Resources["SuccessBrush"];
    private readonly SolidColorBrush _inactive = (SolidColorBrush)Application.Current.Resources["InactiveBrush"];
    private bool _busy;
    private GitHubRelease? _latestRelease;
    private bool _pluginUpdateAvailable;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            RefreshState();
            await CheckForUpdatesAsync(silent: true);
        };
    }

    private static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.1.0";

    private void RefreshState()
    {
        var discordInstalled = DiscordService.IsInstalled;
        var vencordInstalled = VencordService.IsInstalled;
        var bypassEnabled = VencordSettings.IsBypassEnabled();

        DiscordDot.Fill = discordInstalled ? _success : _inactive;
        DiscordStatusText.Text = discordInstalled ? "已安裝" : "找不到應用程式";
        VencordDot.Fill = vencordInstalled ? _success : _inactive;
        VencordStatusText.Text = vencordInstalled ? "已安裝" : "尚未安裝";
        BypassDot.Fill = bypassEnabled ? _success : _inactive;
        BypassStatusText.Text = bypassEnabled ? "已啟用（仍受伺服器控制）" : "未啟用";
        VersionText.Text = $"v{CurrentVersion}";
        var plugin = SoundClonerService.Installed(VencordService.Root);
        var soundEnabled = plugin is not null && VencordSettings.IsSoundClonerEnabled();
        SoundDot.Fill = soundEnabled ? _success : _inactive;
        SoundStatusText.Text = plugin is null ? "尚未安裝" : $"v{plugin.PluginVersion} · {(soundEnabled ? "已啟用" : "未啟用")}";
        RestoreButton.IsEnabled = !_busy && File.Exists(Path.Combine(SoundClonerService.Backup(VencordService.Root), "patcher.js"));

        var primaryLabel = "安裝／更新 4K 與音效複製功能";
        PrimaryButton.Content = primaryLabel;
        AutomationProperties.SetName(PrimaryButton, primaryLabel);
        PrimaryButton.IsEnabled = !_busy && discordInstalled;
        DisableButton.IsEnabled = !_busy && vencordInstalled && bypassEnabled;
        RefreshButton.IsEnabled = !_busy;
        UpdateButton.IsEnabled = !_busy;
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        await InstallFeaturesAsync();
    }

    private async Task InstallFeaturesAsync()
    {
        if (_busy) return;
        if (SoundClonerService.NeedsCustomWarning(VencordService.Root) &&
            MessageBox.Show(this, "偵測到其他自訂外掛，或無法確認目前建置來源。替換後這些外掛可能無法使用；原本的 dist 會先備份，其他設定保留。是否繼續？",
                "替換自訂 Vencord？", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunBusyAsync(async () =>
        {
            if (!DiscordService.IsInstalled)
                throw new InvalidOperationException("找不到 Discord，請先安裝 Discord Desktop。");
            ShowNotice("正在下載並驗證音效外掛…");
            var release = await UpdateService.GetLatestReleaseAsync();
            var stage = await SoundClonerService.PrepareAsync(release, CurrentVersion);
            try
            {
                ShowNotice("正在備份、安裝並重新啟動 Discord…");
                await DiscordService.QuitAsync();
                if (!VencordService.IsInstalled) await VencordService.InstallAsync();
                if (!VencordService.IsInstalled) throw new InvalidOperationException("Vencord 安裝未完成，請稍後再試。");
                SoundClonerService.Install(stage, VencordService.Root, VencordSettings.SettingsPath);
                DiscordService.Launch();
                _pluginUpdateAvailable = false;
                ShowNotice("安裝完成。在伺服器音效上按右鍵，選擇「複製到其他伺服器…」。");
            }
            finally { Directory.Delete(stage, true); }
        });
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "將重新啟動 Discord 並移除音效複製功能。4K 與其他 Vencord 設定不變。",
            "還原安裝前的 Vencord？", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunBusyAsync(async () =>
        {
            ShowNotice("正在還原安裝前的 Vencord…");
            await DiscordService.QuitAsync();
            SoundClonerService.Restore(VencordService.Root, VencordSettings.SettingsPath);
            DiscordService.Launch();
            ShowNotice("已還原原本的 Vencord，移除音效複製設定；其他設定及 4K 設定保持不變。");
        });
    }

    private async void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            await DiscordService.QuitAsync();
            VencordSettings.Update(enabled: false);
            DiscordService.Launch();
            ShowNotice("已關閉串流畫質繞過。");
        });
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshState();
        ShowNotice("狀態已重新檢查。");
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestRelease?.Version is { } latest && latest > AppVersion.Parse(CurrentVersion))
        {
            await RunBusyAsync(async () =>
            {
                UpdateProgress.Visibility = Visibility.Visible;
                ShowNotice("正在下載並安裝更新…");
                string? pluginStage = null;
                try
                {
                    if (SoundClonerService.Installed(VencordService.Root) is not null)
                        pluginStage = await SoundClonerService.PrepareAsync(_latestRelease, _latestRelease.Version!.ToString());
                    await UpdateService.InstallAsync(_latestRelease, async () =>
                    {
                        if (pluginStage is null) return;
                        ShowNotice("正在更新音效外掛並重新啟動 Discord…");
                        await DiscordService.QuitAsync();
                        SoundClonerService.Install(pluginStage, VencordService.Root, VencordSettings.SettingsPath, enableFeatures: false);
                        DiscordService.Launch();
                    });
                }
                finally { if (pluginStage is not null) Directory.Delete(pluginStage, true); }
                Application.Current.Shutdown();
            }, showOperationProgress: false);
            return;
        }

        if (_pluginUpdateAvailable) { await InstallFeaturesAsync(); return; }
        await CheckForUpdatesAsync(silent: false);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_busy) return;
        _busy = true;
        RefreshState();
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = false;
        try
        {
            if (!silent) ShowNotice("正在檢查更新…");
            _latestRelease = await UpdateService.GetLatestReleaseAsync();
            _pluginUpdateAvailable = false;
            if (_latestRelease.Assets.Any(a => a.Name == SoundClonerService.ManifestName))
            {
                var manifest = await SoundClonerService.GetManifestAsync(_latestRelease);
                var installed = SoundClonerService.Installed(VencordService.Root);
                _pluginUpdateAvailable = installed is not null && !installed.SameBuild(manifest);
            }
            var current = AppVersion.Parse(CurrentVersion);
            if (_latestRelease.Version is { } latest && latest > current)
            {
                VersionDot.Fill = _inactive;
                UpdateStatusText.Text = $"可更新至 v{latest}";
                UpdateButton.Content = "下載並安裝更新";
                ShowNotice($"發現新版本 v{latest}。");
            }
            else if (_pluginUpdateAvailable)
            {
                UpdateStatusText.Text = "音效外掛有更新";
                UpdateButton.Content = "安裝功能更新";
                ShowNotice("發現音效外掛更新，按「安裝功能更新」即可安裝並重啟 Discord。");
            }
            else
            {
                VersionDot.Fill = _success;
                UpdateStatusText.Text = $"目前已是最新版本 v{CurrentVersion}";
                UpdateButton.Content = "檢查更新";
                if (!silent) ShowNotice("目前已是最新版本。");
            }
        }
        catch (Exception ex)
        {
            if (!silent) ShowError($"無法檢查更新：{ex.Message}");
        }
        finally
        {
            _busy = false;
            UpdateProgress.Visibility = Visibility.Collapsed;
            RefreshState();
        }
    }

    private async Task RunBusyAsync(Func<Task> action, bool showOperationProgress = true)
    {
        if (_busy) return;
        _busy = true;
        if (showOperationProgress) OperationProgress.Visibility = Visibility.Visible;
        RefreshState();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            OperationProgress.Visibility = Visibility.Collapsed;
            UpdateProgress.Visibility = Visibility.Collapsed;
            RefreshState();
        }
    }

    private void ShowNotice(string message)
    {
        NoticeBorder.Background = new SolidColorBrush(Color.FromRgb(232, 235, 255));
        NoticeText.Foreground = (Brush)Application.Current.Resources["TextBrush"];
        NoticeText.Text = message;
        NoticeBorder.Visibility = Visibility.Visible;
    }

    private void ShowError(string message)
    {
        NoticeBorder.Background = new SolidColorBrush(Color.FromRgb(255, 228, 229));
        NoticeText.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
        NoticeText.Text = message;
        NoticeBorder.Visibility = Visibility.Visible;
    }
}
