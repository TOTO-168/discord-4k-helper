import AppKit
import Foundation

@MainActor
final class HelperModel: ObservableObject {
    @Published private(set) var discordInstalled = false
    @Published private(set) var vencordInstalled = false
    @Published private(set) var bypassEnabled = false
    @Published private(set) var isBusy = false
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

    func openVencordDownload() {
        guard let url = URL(string: "https://vencord.dev/download/") else { return }
        NSWorkspace.shared.open(url)
    }

    func openDiscord() {
        guard let discordURL else {
            showError(HelperError.discordNotFound.localizedDescription)
            return
        }
        NSWorkspace.shared.openApplication(at: discordURL, configuration: .init())
    }

    private func updateSettings(enabled: Bool) throws {
        let original = try Data(contentsOf: settingsURL)
        let backupURL = settingsURL.deletingLastPathComponent()
            .appendingPathComponent("settings.before-discord-4k-helper.json")
        if !fileManager.fileExists(atPath: backupURL.path) {
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
