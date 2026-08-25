using System;
using System.Diagnostics;
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
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.1";

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

        var primaryLabel = vencordInstalled
            ? bypassEnabled ? "4K 畫質選項已啟用" : "啟用 4K 選項並重啟 Discord"
            : "安裝 Vencord 並啟用 4K";
        PrimaryButton.Content = primaryLabel;
        AutomationProperties.SetName(PrimaryButton, primaryLabel);
        PrimaryButton.IsEnabled = !_busy && discordInstalled && (!vencordInstalled || !bypassEnabled);
        DisableButton.IsEnabled = !_busy && vencordInstalled && bypassEnabled;
        RefreshButton.IsEnabled = !_busy;
        UpdateButton.IsEnabled = !_busy;
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            if (!DiscordService.IsInstalled)
                throw new InvalidOperationException("找不到 Discord，請先安裝 Discord Desktop。");
            if (VencordService.IsInstalled && VencordSettings.IsBypassEnabled())
            {
                ShowNotice("4K 畫質選項已經啟用。");
                return;
            }

            await DiscordService.QuitAsync();
            if (!VencordService.IsInstalled)
            {
                ShowNotice("正在下載並安裝 Vencord…");
                await VencordService.InstallAsync();
            }

            if (!VencordService.IsInstalled)
                throw new InvalidOperationException("Vencord 安裝未完成，請稍後再試。");

            VencordSettings.Update(enabled: true);
            DiscordService.Launch();
            ShowNotice("Vencord 與 4K 畫質選項已安裝完成。");
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
                await UpdateService.InstallAsync(_latestRelease);
                Application.Current.Shutdown();
            }, showOperationProgress: false);
            return;
        }

        await CheckForUpdatesAsync(silent: false);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_busy) return;
        _busy = true;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = false;
        try
        {
            if (!silent) ShowNotice("正在檢查更新…");
            _latestRelease = await UpdateService.GetLatestReleaseAsync();
            var current = AppVersion.Parse(CurrentVersion);
            if (_latestRelease.Version is { } latest && latest > current)
            {
                VersionDot.Fill = _inactive;
                UpdateStatusText.Text = $"可更新至 v{latest}";
                UpdateButton.Content = "下載並安裝更新";
                ShowNotice($"發現新版本 v{latest}。");
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
