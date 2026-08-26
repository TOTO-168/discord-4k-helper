import Foundation

enum SettingsEditor {
    static func updating(_ data: Data, enabled: Bool? = nil, soundCloner: Bool? = nil, removeSoundCloner: Bool = false) throws -> Data {
        guard var root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw HelperError.invalidSettings
        }
        if let plugins = root["plugins"], !(plugins is [String: Any]) { throw HelperError.invalidSettings }

        var plugins = root["plugins"] as? [String: Any] ?? [:]
        if let enabled {
            if let existing = plugins["FakeNitro"], !(existing is [String: Any]) { throw HelperError.invalidSettings }
            var fakeNitro = plugins["FakeNitro"] as? [String: Any] ?? [:]
            fakeNitro["enabled"] = true
            fakeNitro["enableStreamQualityBypass"] = enabled
            plugins["FakeNitro"] = fakeNitro
        }
        if let soundCloner {
            if let existing = plugins["SoundCloner"], !(existing is [String: Any]) { throw HelperError.invalidSettings }
            var plugin = plugins["SoundCloner"] as? [String: Any] ?? [:]
            plugin["enabled"] = soundCloner
            plugins["SoundCloner"] = plugin
        }
        if removeSoundCloner { plugins.removeValue(forKey: "SoundCloner") }
        root["plugins"] = plugins

        return try JSONSerialization.data(
            withJSONObject: root,
            options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        )
    }

    static func isSoundClonerEnabled(in data: Data) -> Bool {
        let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
        let plugins = root?["plugins"] as? [String: Any]
        return (plugins?["SoundCloner"] as? [String: Any])?["enabled"] as? Bool == true
    }

    static func isBypassEnabled(in data: Data) -> Bool {
        guard
            let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
            let plugins = root["plugins"] as? [String: Any],
            let fakeNitro = plugins["FakeNitro"] as? [String: Any]
        else { return false }

        return fakeNitro["enabled"] as? Bool == true
            && fakeNitro["enableStreamQualityBypass"] as? Bool == true
    }
}

enum HelperError: LocalizedError {
    case invalidSettings
    case missingVencord
    case discordWouldNotQuit
    case discordNotFound
    case downloadFailed
    case untrustedDownload
    case missingUpdateAsset
    case invalidUpdate
    case updateLocationNotWritable
    case processFailed(String)
    case invalidPluginPackage
    case missingBackup

    var errorDescription: String? {
        switch self {
        case .invalidPluginPackage:
            return "音效外掛封裝驗證失敗，未變更現有安裝。請重新下載或更新 Helper。"
        case .missingBackup:
            return "找不到安裝前的 Vencord 備份，無法自動還原。"
        case .invalidSettings:
            return "Vencord 設定檔格式無法辨識。"
        case .missingVencord:
            return "尚未偵測到 Vencord，請先使用官方安裝程式安裝。"
        case .discordWouldNotQuit:
            return "Discord 尚未完全結束，請手動結束後再試一次。"
        case .discordNotFound:
            return "在 Applications 資料夾中找不到 Discord。"
        case .downloadFailed:
            return "下載失敗，請檢查網路後再試一次。"
        case .untrustedDownload:
            return "下載來源不受信任。"
        case .missingUpdateAsset:
            return "這個版本沒有相容的 macOS 更新檔。"
        case .invalidUpdate:
            return "下載的更新檔無法驗證。"
        case .updateLocationNotWritable:
            return "目前 App 所在資料夾無法寫入，請移到 Applications 或桌面後再試。"
        case .processFailed(let output):
            return String(output.suffix(800))
        }
    }
}
