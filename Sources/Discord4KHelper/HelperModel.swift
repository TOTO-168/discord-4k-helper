import AppKit
import Foundation

@MainActor
final class HelperModel: ObservableObject {
    @Published private(set) var discordInstalled = false
    @Published private(set) var vencordInstalled = false
    @Published private(set) var bypassEnabled = false
    @Published private(set) var isBusy = false
    @Published private(set) var isCheckingUpdate = false
    @Published private(set) var updateAvailable = false
    @Published private(set) var latestVersion: String?
    @Published var notice: String?
    @Published var noticeIsError = false

    private let fileManager = FileManager.default
    private let settingsURL: URL
    private let vencordDirectory: URL

    init() {
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        vencordDirectory = appSupport.appendingPathComponent("Vencord", isDirectory: true)
        settingsURL = vencordDirectory
            .appendingPathComponent("settings", isDirectory: true)
            .appendingPathComponent("settings.json")
        refresh()
    }

    var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "2.0.1"
    }

    var discordURL: URL? {
        let candidates = [
            URL(fileURLWithPath: "/Applications/Discord.app", isDirectory: true),
            fileManager.homeDirectoryForCurrentUser
                .appendingPathComponent("Applications", isDirectory: true)
                .appendingPathComponent("Discord.app", isDirectory: true)
        ]
        return candidates.first { fileManager.fileExists(atPath: $0.path) }
    }

    func refresh() {
        discordInstalled = discordURL != nil
        vencordInstalled = fileManager.fileExists(
            atPath: vencordDirectory.appendingPathComponent("dist/patcher.js").path
        )
        bypassEnabled = (try? Data(contentsOf: settingsURL)).map(SettingsEditor.isBypassEnabled) ?? false
    }

    func applyBypass(_ enabled: Bool) {
        guard vencordInstalled else {
            showError(HelperError.missingVencord.localizedDescription)
            return
        }
        guard discordURL != nil else {
            showError(HelperError.discordNotFound.localizedDescription)
            return
        }
        guard bypassEnabled != enabled else {
            notice = enabled ? "4K 畫質選項已經啟用。" : "畫質繞過已經關閉。"
            noticeIsError = false
            return
        }

        isBusy = true
        notice = enabled ? "正在啟用並重新啟動 Discord…" : "正在還原並重新啟動 Discord…"
        noticeIsError = false

        Task {
            do {
                try await quitDiscord()
                try updateSettings(enabled: enabled)
                try await launchDiscord()
                refresh()
                notice = enabled
                    ? "已啟用。請在直播畫質中選擇 4K 與 60 FPS。"
                    : "已關閉串流畫質繞過。"
                noticeIsError = false
            } catch {
                refresh()
                showError(error.localizedDescription)
            }
            isBusy = false
        }
    }

    func installVencordAndEnable() {
        guard discordURL != nil else {
            showError(HelperError.discordNotFound.localizedDescription)
            return
        }

        isBusy = true
        notice = "正在下載並安裝 Vencord…"
        noticeIsError = false

        Task {
            do {
                try await quitDiscord()
                try await VencordInstallService.install()
                refresh()
                guard vencordInstalled else { throw HelperError.missingVencord }
                try updateSettings(enabled: true)
                try await launchDiscord()
                refresh()
                notice = "Vencord 與 4K 畫質選項已安裝完成。"
                noticeIsError = false
            } catch {
                refresh()
                showError(error.localizedDescription)
            }
            isBusy = false
        }
    }

    func checkForUpdates(silent: Bool = false) async {
        guard !isCheckingUpdate else { return }
        isCheckingUpdate = true
        if !silent {
            notice = "正在檢查更新…"
            noticeIsError = false
        }

        do {
            let release = try await NetworkService.latestRelease()
            latestVersion = release.version?.description ?? release.tagName
            if let latest = release.version, let current = AppVersion(currentVersion), latest > current {
                updateAvailable = true
                notice = "發現新版本 v\(latest)。"
            } else if !silent {
                updateAvailable = false
                notice = "目前已是最新版本。"
            }
        } catch {
            if !silent { showError("無法檢查更新：\(error.localizedDescription)") }
        }
        isCheckingUpdate = false
    }

    func installUpdate() {
        guard updateAvailable else {
            Task { await checkForUpdates() }
            return
        }

        isBusy = true
        notice = "正在下載並安裝更新…"
        noticeIsError = false
        Task {
            do {
                let release = try await NetworkService.latestRelease()
                try await AppUpdateService.prepare(release)
                notice = "更新已下載，正在重新啟動…"
                NSApplication.shared.terminate(nil)
            } catch {
                showError("更新失敗：\(error.localizedDescription)")
                isBusy = false
            }
        }
    }

    func openDiscord() {
        guard let discordURL else {
            showError(HelperError.discordNotFound.localizedDescription)
            return
        }
        NSWorkspace.shared.openApplication(at: discordURL, configuration: .init())
    }

    private func updateSettings(enabled: Bool) throws {
        let existed = fileManager.fileExists(atPath: settingsURL.path)
        let original = existed ? try Data(contentsOf: settingsURL) : Data("{\"plugins\":{}}".utf8)
        try fileManager.createDirectory(
            at: settingsURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let backupURL = settingsURL.deletingLastPathComponent()
            .appendingPathComponent("settings.before-discord-4k-helper.json")
        if existed && !fileManager.fileExists(atPath: backupURL.path) {
            try original.write(to: backupURL, options: .atomic)
        }

        let updated = try SettingsEditor.updating(original, enabled: enabled)
        try updated.write(to: settingsURL, options: .atomic)
    }

    private func quitDiscord() async throws {
        let bundleIdentifiers = ["com.hnc.Discord", "com.discordapp.Discord"]
        let applications = bundleIdentifiers.flatMap(NSRunningApplication.runningApplications)
        applications.forEach { $0.terminate() }

        for _ in 0..<40 {
            if bundleIdentifiers.flatMap(NSRunningApplication.runningApplications).isEmpty {
                return
            }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        throw HelperError.discordWouldNotQuit
    }

    private func launchDiscord() async throws {
        guard let discordURL else { throw HelperError.discordNotFound }
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            NSWorkspace.shared.openApplication(at: discordURL, configuration: .init()) { _, error in
                if let error {
                    continuation.resume(throwing: error)
                } else {
                    continuation.resume(returning: ())
                }
            }
        }
    }

    private func showError(_ text: String) {
        notice = text
        noticeIsError = true
    }
}
