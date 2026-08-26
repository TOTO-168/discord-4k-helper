import Foundation

enum Distribution {
    static let repository = "TOTO-168/discord-4k-helper"
    static let macAssetName = "Discord-4K-Helper-macOS-arm64.zip"
    static let macInstallerAssetName = "VencordInstallerCli-macOS-arm64"
    static let apiURL = URL(string: "https://api.github.com/repos/\(repository)/releases/latest")!
    static let installerURL = URL(
        string: "https://github.com/\(repository)/releases/latest/download/\(macInstallerAssetName)"
    )!
}

struct AppVersion: Comparable, CustomStringConvertible {
    let parts: [Int]

    init?(_ value: String) {
        guard let core = value.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
            .split(separator: "-", maxSplits: 1).first else { return nil }
        let parsed = core.split(separator: ".").map(String.init).compactMap(Int.init)
        guard !parsed.isEmpty, parsed.count == core.split(separator: ".").count else { return nil }
        parts = parsed
    }

    var description: String { parts.map(String.init).joined(separator: ".") }

    static func == (lhs: AppVersion, rhs: AppVersion) -> Bool {
        !(lhs < rhs) && !(rhs < lhs)
    }

    static func < (lhs: AppVersion, rhs: AppVersion) -> Bool {
        let count = max(lhs.parts.count, rhs.parts.count)
        for index in 0..<count {
            let left = index < lhs.parts.count ? lhs.parts[index] : 0
            let right = index < rhs.parts.count ? rhs.parts[index] : 0
            if left != right { return left < right }
        }
        return false
    }
}

struct GitHubRelease: Decodable {
    let tagName: String
    let assets: [GitHubAsset]

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case assets
    }

    var version: AppVersion? { AppVersion(tagName) }
    func asset(named name: String) -> GitHubAsset? { assets.first { $0.name == name } }
}

struct GitHubAsset: Decodable {
    let name: String
    let downloadURL: URL

    enum CodingKeys: String, CodingKey {
        case name
        case downloadURL = "browser_download_url"
    }
}

enum NetworkService {
    static func latestRelease() async throws -> GitHubRelease {
        var request = URLRequest(url: Distribution.apiURL)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("Discord4KHelper", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        try validate(response)
        return try JSONDecoder().decode(GitHubRelease.self, from: data)
    }

    static func download(_ url: URL) async throws -> URL {
        guard url.scheme == "https", ["github.com", "objects.githubusercontent.com"].contains(url.host) else {
            throw HelperError.untrustedDownload
        }
        var request = URLRequest(url: url)
        request.setValue("Discord4KHelper", forHTTPHeaderField: "User-Agent")
        let (temporaryURL, response) = try await URLSession.shared.download(for: request)
        try validate(response)

        let destination = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
        try FileManager.default.moveItem(at: temporaryURL, to: destination)
        return destination
    }

    private static func validate(_ response: URLResponse) throws {
        guard let http = response as? HTTPURLResponse, 200..<300 ~= http.statusCode else {
            throw HelperError.downloadFailed
        }
    }
}

enum ProcessRunner {
    static func run(_ executable: URL, arguments: [String]) async throws -> String {
        try await Task.detached {
            let process = Process()
            let pipe = Pipe()
            process.executableURL = executable
            process.arguments = arguments
            process.standardOutput = pipe
            process.standardError = pipe
            try process.run()
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            let output = String(decoding: data, as: UTF8.self)
            guard process.terminationStatus == 0 else {
                throw HelperError.processFailed(output.isEmpty ? "外部程式執行失敗。" : output)
            }
            return output
        }.value
    }
}

enum VencordInstallService {
    static func install() async throws {
        let installer = try await NetworkService.download(Distribution.installerURL)
        defer { try? FileManager.default.removeItem(at: installer) }
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: installer.path)
        _ = try await ProcessRunner.run(installer, arguments: ["--install", "--branch", "stable"])
    }
}

enum AppUpdateService {
    static func target(for bundle: URL, parentWritable: Bool, home: URL) -> URL {
        if bundle.path.contains("/AppTranslocation/") || !parentWritable {
            return home.appendingPathComponent("Applications/Discord 4K Helper.app")
        }
        return bundle
    }

    static func prepare(_ release: GitHubRelease, beforeRelaunch: () async throws -> Void = {}) async throws {
        guard let asset = release.asset(named: Distribution.macAssetName) else {
            throw HelperError.missingUpdateAsset
        }

        let archive = try await NetworkService.download(asset.downloadURL)
        defer { try? FileManager.default.removeItem(at: archive) }
        let stage = FileManager.default.temporaryDirectory
            .appendingPathComponent("discord-4k-update-\(UUID().uuidString)", isDirectory: true)
        var updaterStarted = false
        defer { if !updaterStarted { try? FileManager.default.removeItem(at: stage) } }
        try FileManager.default.createDirectory(at: stage, withIntermediateDirectories: true)
        _ = try await ProcessRunner.run(
            URL(fileURLWithPath: "/usr/bin/ditto"),
            arguments: ["-x", "-k", archive.path, stage.path]
        )

        let replacement = stage.appendingPathComponent("Discord 4K Helper.app", isDirectory: true)
        guard
            let bundle = Bundle(url: replacement),
            bundle.bundleIdentifier == "tw.codex.discord4khelper",
            let bundledVersion = bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
            let version = AppVersion(bundledVersion), let expected = release.version,
            version == expected
        else { throw HelperError.invalidUpdate }

        _ = try await ProcessRunner.run(
            URL(fileURLWithPath: "/usr/bin/codesign"),
            arguments: ["--verify", "--deep", "--strict", replacement.path]
        )

        let target = target(for: Bundle.main.bundleURL,
                            parentWritable: FileManager.default.isWritableFile(atPath: Bundle.main.bundleURL.deletingLastPathComponent().path),
                            home: FileManager.default.homeDirectoryForCurrentUser)
        try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        guard target.pathExtension == "app",
              FileManager.default.isWritableFile(atPath: target.deletingLastPathComponent().path)
        else { throw HelperError.updateLocationNotWritable }
        if FileManager.default.fileExists(atPath: target.path),
           Bundle(url: target)?.bundleIdentifier != "tw.codex.discord4khelper" { throw HelperError.invalidUpdate }

        // Finish an existing managed plugin update only after the app archive is verified.
        try await beforeRelaunch()

        let scriptURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("discord-4k-updater-\(UUID().uuidString).sh")
        let script = """
        #!/bin/sh
        target="$1"
        replacement="$2"
        stage="$3"
        pid="$4"
        backup="${target}.old-\(UUID().uuidString)"
        candidate="${target}.new-\(UUID().uuidString)"
        while kill -0 "$pid" 2>/dev/null; do sleep 0.2; done
        if /usr/bin/ditto "$replacement" "$candidate"; then
            if [ ! -e "$target" ] || mv "$target" "$backup"; then
                if mv "$candidate" "$target"; then
                    /usr/bin/open "$target"
                    [ -e "$backup" ] && rm -rf "$backup"
                else
                    [ -e "$backup" ] && mv "$backup" "$target"
                    /usr/bin/open "$target"
                fi
            fi
        fi
        [ -e "$candidate" ] && rm -rf "$candidate"
        rm -rf "$stage"
        rm -f "$0"
        """
        try Data(script.utf8).write(to: scriptURL, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: scriptURL.path)

        let updater = Process()
        updater.executableURL = URL(fileURLWithPath: "/bin/sh")
        updater.arguments = [
            scriptURL.path,
            target.path,
            replacement.path,
            stage.path,
            String(ProcessInfo.processInfo.processIdentifier)
        ]
        try updater.run()
        updaterStarted = true
    }
}
