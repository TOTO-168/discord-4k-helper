import Foundation

enum SettingsEditor {
    static func updating(_ data: Data, enabled: Bool) throws -> Data {
        guard var root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw HelperError.invalidSettings
        }

        var plugins = root["plugins"] as? [String: Any] ?? [:]
        var fakeNitro = plugins["FakeNitro"] as? [String: Any] ?? [:]
        fakeNitro["enabled"] = true
        fakeNitro["enableStreamQualityBypass"] = enabled
        plugins["FakeNitro"] = fakeNitro
        root["plugins"] = plugins

        return try JSONSerialization.data(
            withJSONObject: root,
            options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        )
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

    var errorDescription: String? {
        switch self {
        case .invalidSettings:
            return "Vencord 設定檔格式無法辨識。"
        case .missingVencord:
            return "尚未偵測到 Vencord，請先使用官方安裝程式安裝。"
        case .discordWouldNotQuit:
            return "Discord 尚未完全結束，請手動結束後再試一次。"
        case .discordNotFound:
            return "在 Applications 資料夾中找不到 Discord。"
        }
    }
}
