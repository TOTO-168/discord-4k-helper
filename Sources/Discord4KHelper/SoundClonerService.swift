import CryptoKit
import Foundation

struct PluginManifest: Codable, Equatable {
    struct File: Codable, Equatable {
        let path: String
        let sha256: String
    }
    let schemaVersion: Int
    let helperVersion: String
    let pluginVersion: String
    let vencordCommit: String
    let files: [File]

    func validate() throws {
        guard schemaVersion == 1,
              helperVersion.range(of: "^[0-9]{1,5}\\.[0-9]{1,5}\\.[0-9]{1,5}$", options: .regularExpression) != nil,
              pluginVersion.range(of: "^[0-9]{1,5}\\.[0-9]{1,5}\\.[0-9]{1,5}$", options: .regularExpression) != nil,
              vencordCommit.range(of: "^[a-f0-9]{40}$", options: .regularExpression) != nil,
              !files.isEmpty, files.count <= 100,
              Set(files.map { $0.path.lowercased() }).count == files.count,
              Set(["dist/patcher.js", "dist/preload.js", "dist/renderer.js", "dist/renderer.css", "dist/package.json"])
                .isSubset(of: Set(files.map(\.path))),
              files.allSatisfy({ file in
                  file.path.range(of: "^dist/[A-Za-z0-9_-][A-Za-z0-9._-]*$", options: .regularExpression) != nil
                    && file.sha256.range(of: "^[a-f0-9]{64}$", options: .regularExpression) != nil
              })
        else { throw HelperError.invalidPluginPackage }
    }

    static func hash(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    func verify(directory: URL) throws {
        try validate()
        let fm = FileManager.default
        let actual = try fm.contentsOfDirectory(atPath: directory.appendingPathComponent("dist").path)
        guard Set(actual.map { "dist/" + $0 }) == Set(files.map(\.path)) else {
            throw HelperError.invalidPluginPackage
        }
        for file in files {
            let url = directory.appendingPathComponent(file.path)
            let attrs = try fm.attributesOfItem(atPath: url.path)
            guard attrs[.type] as? FileAttributeType == .typeRegular,
                  Self.hash(try Data(contentsOf: url)) == file.sha256 else {
                throw HelperError.invalidPluginPackage
            }
        }
    }
}

// Validate central AND local names before invoking the system ZIP extractor.
// This format deliberately only supports a small flat, non-ZIP64 package.
enum PluginZip {
    static func validate(_ data: Data) throws {
        let bytes = [UInt8](data)
        func number(_ offset: Int, _ length: Int) throws -> Int {
            guard offset >= 0, offset + length <= bytes.count else { throw HelperError.invalidPluginPackage }
            return (0..<length).reduce(0) { $0 | Int(bytes[offset + $1]) << ($1 * 8) }
        }
        guard bytes.count >= 22, bytes.count <= 128 * 1024 * 1024 else { throw HelperError.invalidPluginPackage }
        var end: Int?
        for offset in stride(from: bytes.count - 22, through: max(0, bytes.count - 65557), by: -1) {
            if try number(offset, 4) == 0x06054b50,
               try offset + 22 + number(offset + 20, 2) == bytes.count { end = offset; break }
        }
        guard let end, try number(end + 4, 4) == 0 else { throw HelperError.invalidPluginPackage }
        let count = try number(end + 10, 2)
        guard count > 0, count <= 110, try number(end + 8, 2) == count else { throw HelperError.invalidPluginPackage }
        var cursor = try number(end + 16, 4)
        guard try cursor + number(end + 12, 4) == end else { throw HelperError.invalidPluginPackage }
        var seen = Set<String>()
        var total = 0
        for _ in 0..<count {
            guard try number(cursor, 4) == 0x02014b50,
                  try number(cursor + 8, 2) & 1 == 0,
                  try [0, 8].contains(number(cursor + 10, 2)),
                  try number(cursor + 34, 2) == 0 else { throw HelperError.invalidPluginPackage }
            let nameLength = try number(cursor + 28, 2)
            let nameStart = cursor + 46
            guard nameLength > 0, nameStart + nameLength <= end else { throw HelperError.invalidPluginPackage }
            let name = String(decoding: bytes[nameStart..<nameStart + nameLength], as: UTF8.self)
            let allowed = ["Vencord-SoundCloner/", "Vencord-SoundCloner/dist/", "Vencord-SoundCloner/manifest.json", "Vencord-SoundCloner/Vencord-LICENSE.txt"]
            guard allowed.contains(name) || name.range(of: "^Vencord-SoundCloner/dist/[A-Za-z0-9_-][A-Za-z0-9._-]*$", options: .regularExpression) != nil,
                  seen.insert(name.lowercased()).inserted else { throw HelperError.invalidPluginPackage }
            let kind = try (number(cursor + 38, 4) >> 16) & 0xf000
            guard [0, 0x4000, 0x8000].contains(kind),
                  kind != 0x4000 || name.hasSuffix("/") else { throw HelperError.invalidPluginPackage }
            total += try number(cursor + 24, 4)
            guard total <= 128 * 1024 * 1024 else { throw HelperError.invalidPluginPackage }
            let local = try number(cursor + 42, 4)
            guard try number(local, 4) == 0x04034b50,
                  try number(local + 26, 2) == nameLength,
                  try number(local + 6, 2) == number(cursor + 8, 2),
                  try number(local + 8, 2) == number(cursor + 10, 2),
                  local + 30 + nameLength <= cursor else { throw HelperError.invalidPluginPackage }
            let localName = String(decoding: bytes[local + 30..<local + 30 + nameLength], as: UTF8.self)
            guard localName == name else { throw HelperError.invalidPluginPackage }
            cursor += try 46 + nameLength + number(cursor + 30, 2) + number(cursor + 32, 2)
        }
        guard cursor == end else { throw HelperError.invalidPluginPackage }
    }
}

enum SoundClonerService {
    static let archiveName = "Vencord-SoundCloner.zip"
    static let manifestName = "Vencord-SoundCloner-manifest.json"
    static let markerName = ".soundcloner-manifest.json"
    static let fm = FileManager.default

    static func manifest(for release: GitHubRelease) async throws -> PluginManifest {
        guard let asset = release.asset(named: manifestName) else { throw HelperError.missingUpdateAsset }
        let file = try await NetworkService.download(asset.downloadURL)
        defer { try? fm.removeItem(at: file) }
        let manifest = try JSONDecoder().decode(PluginManifest.self, from: Data(contentsOf: file))
        try manifest.validate()
        guard AppVersion(manifest.helperVersion) == release.version else { throw HelperError.invalidPluginPackage }
        return manifest
    }

    static func prepare(_ release: GitHubRelease, helperVersion: String) async throws -> URL {
        let expected = try await manifest(for: release)
        guard let current = AppVersion(helperVersion), let required = AppVersion(expected.helperVersion),
              current >= required, let asset = release.asset(named: archiveName) else { throw HelperError.invalidPluginPackage }
        let archive = try await NetworkService.download(asset.downloadURL)
        defer { try? fm.removeItem(at: archive) }
        try PluginZip.validate(Data(contentsOf: archive))
        let stage = fm.temporaryDirectory.appendingPathComponent("soundcloner-\(UUID().uuidString)")
        try fm.createDirectory(at: stage, withIntermediateDirectories: false)
        do {
            _ = try await ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/ditto"), arguments: ["-x", "-k", archive.path, stage.path])
            let package = stage.appendingPathComponent("Vencord-SoundCloner")
            let manifest = try JSONDecoder().decode(PluginManifest.self, from: Data(contentsOf: package.appendingPathComponent("manifest.json")))
            guard manifest == expected else { throw HelperError.invalidPluginPackage }
            try manifest.verify(directory: package)
            return stage
        } catch { try? fm.removeItem(at: stage); throw error }
    }

    static func installed(in root: URL) -> PluginManifest? {
        guard let data = try? Data(contentsOf: root.appendingPathComponent("dist/\(markerName)")),
              let manifest = try? JSONDecoder().decode(PluginManifest.self, from: data),
              (try? manifest.validate()) != nil,
              manifest.files.allSatisfy({ file in
                  guard let bytes = try? Data(contentsOf: root.appendingPathComponent(file.path)) else { return false }
                  return PluginManifest.hash(bytes) == file.sha256
              }) else { return nil }
        return manifest
    }

    static func needsCustomWarning(in root: URL) -> Bool {
        guard installed(in: root) == nil, fm.fileExists(atPath: root.appendingPathComponent("dist/patcher.js").path) else { return false }
        guard let data = try? Data(contentsOf: root.appendingPathComponent("dist/renderer.js.map")),
              let map = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let sources = map["sources"] as? [String] else { return true }
        return sources.contains { $0.replacingOccurrences(of: "\\", with: "/").contains("/userplugins/") }
    }

    static func backup(in root: URL) -> URL { root.appendingPathComponent("discord-4k-helper-backup/dist") }

    static func install(stage: URL, root: URL, settings: URL, enableFeatures: Bool = true) throws {
        let package = stage.appendingPathComponent("Vencord-SoundCloner")
        let data = try Data(contentsOf: package.appendingPathComponent("manifest.json"))
        let manifest = try JSONDecoder().decode(PluginManifest.self, from: data)
        try manifest.verify(directory: package)
        let original = fm.fileExists(atPath: settings.path) ? try Data(contentsOf: settings) : Data("{}".utf8)
        let updated = enableFeatures ? try SettingsEditor.updating(original, enabled: true, soundCloner: true) : original
        let backupURL = backup(in: root)
        if !fm.fileExists(atPath: backupURL.path) {
            try fm.createDirectory(at: backupURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            let temporaryBackup = root.appendingPathComponent("backup-\(UUID().uuidString)")
            defer { try? fm.removeItem(at: temporaryBackup) }
            try fm.copyItem(at: root.appendingPathComponent("dist"), to: temporaryBackup)
            try fm.moveItem(at: temporaryBackup, to: backupURL)
        }
        try swap(source: package.appendingPathComponent("dist"), root: root, settings: settings, updated: updated, marker: data)
    }

    static func restore(root: URL, settings: URL) throws {
        let source = backup(in: root)
        guard fm.fileExists(atPath: source.appendingPathComponent("patcher.js").path) else { throw HelperError.missingBackup }
        let original = fm.fileExists(atPath: settings.path) ? try Data(contentsOf: settings) : Data("{}".utf8)
        try swap(source: source, root: root, settings: settings,
                 updated: SettingsEditor.updating(original, removeSoundCloner: true), marker: nil)
    }

    // A failed settings write also rolls back dist. The original backup is never overwritten.
    static func swap(source: URL, root: URL, settings: URL, updated: Data, marker: Data?) throws {
        let candidate = root.appendingPathComponent("dist-new-\(UUID().uuidString)")
        let previous = root.appendingPathComponent("dist-old-\(UUID().uuidString)")
        let target = root.appendingPathComponent("dist")
        defer { try? fm.removeItem(at: candidate) }
        try fm.copyItem(at: source, to: candidate)
        if let marker { try marker.write(to: candidate.appendingPathComponent(markerName), options: .atomic) }
        try fm.createDirectory(at: settings.deletingLastPathComponent(), withIntermediateDirectories: true)
        try fm.moveItem(at: target, to: previous)
        do {
            try fm.moveItem(at: candidate, to: target)
            try updated.write(to: settings, options: .atomic)
        } catch {
            if fm.fileExists(atPath: target.path) { try fm.removeItem(at: target) }
            try fm.moveItem(at: previous, to: target)
            throw error
        }
        try? fm.removeItem(at: previous)
    }
}
