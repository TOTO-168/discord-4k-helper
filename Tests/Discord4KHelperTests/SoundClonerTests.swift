import Foundation
import XCTest
@testable import Discord4KHelper

final class SoundClonerTests: XCTestCase {
    private let fm = FileManager.default
    private var temporary: URL!

    override func setUpWithError() throws {
        temporary = fm.temporaryDirectory.appendingPathComponent("soundcloner-tests-\(UUID().uuidString)")
        try fm.createDirectory(at: temporary, withIntermediateDirectories: false)
    }
    override func tearDownWithError() throws { try fm.removeItem(at: temporary) }

    private func fixture() throws -> (URL, URL, URL, PluginManifest) {
        let stage = temporary.appendingPathComponent("stage")
        let package = stage.appendingPathComponent("Vencord-SoundCloner")
        let root = temporary.appendingPathComponent("Vencord")
        let settings = root.appendingPathComponent("settings/settings.json")
        try fm.createDirectory(at: package.appendingPathComponent("dist"), withIntermediateDirectories: true)
        try fm.createDirectory(at: root.appendingPathComponent("dist"), withIntermediateDirectories: true)
        try fm.createDirectory(at: settings.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data("original".utf8).write(to: root.appendingPathComponent("dist/patcher.js"))
        try Data(#"{"theme":"dark","plugins":{"Other":{"enabled":true},"FakeNitro":{"custom":"keep"}}}"#.utf8).write(to: settings)
        let names = ["patcher.js", "preload.js", "renderer.js", "renderer.css", "package.json"]
        let files = try names.map { name -> PluginManifest.File in
            let data = Data("test-\(name)".utf8)
            try data.write(to: package.appendingPathComponent("dist/\(name)"))
            return .init(path: "dist/\(name)", sha256: PluginManifest.hash(data))
        }
        let manifest = PluginManifest(schemaVersion: 1, helperVersion: "2.1.0", pluginVersion: "1.0.0",
                                      vencordCommit: String(repeating: "a", count: 40), files: files)
        try JSONEncoder().encode(manifest).write(to: package.appendingPathComponent("manifest.json"))
        return (stage, root, settings, manifest)
    }

    func testSettingsAreIndependent() throws {
        let input = Data(#"{"plugins":{"Other":{"enabled":true},"FakeNitro":{"custom":"keep"}}}"#.utf8)
        let enabled = try SettingsEditor.updating(input, enabled: true, soundCloner: true)
        XCTAssertTrue(SettingsEditor.isSoundClonerEnabled(in: enabled))
        let disabledSound = try SettingsEditor.updating(enabled, soundCloner: false)
        XCTAssertTrue(SettingsEditor.isBypassEnabled(in: disabledSound))
        let disabledBypass = try SettingsEditor.updating(enabled, enabled: false)
        XCTAssertTrue(SettingsEditor.isSoundClonerEnabled(in: disabledBypass))
        let restored = try SettingsEditor.updating(enabled, removeSoundCloner: true)
        XCTAssertTrue(SettingsEditor.isBypassEnabled(in: restored))
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: restored) as? [String: Any])
        let plugins = try XCTUnwrap(root["plugins"] as? [String: Any])
        XCTAssertNil(plugins["SoundCloner"])
        XCTAssertEqual((plugins["Other"] as? [String: Any])?["enabled"] as? Bool, true)
        XCTAssertEqual((plugins["FakeNitro"] as? [String: Any])?["custom"] as? String, "keep")
    }

    func testInstallUpdateRestorePreservesOriginalBackup() throws {
        let (stage, root, settings, manifest) = try fixture()
        try SoundClonerService.install(stage: stage, root: root, settings: settings)
        XCTAssertEqual(SoundClonerService.installed(in: root), manifest)
        XCTAssertTrue(SettingsEditor.isSoundClonerEnabled(in: try Data(contentsOf: settings)))
        try SoundClonerService.install(stage: stage, root: root, settings: settings)
        XCTAssertEqual(try String(contentsOf: SoundClonerService.backup(in: root).appendingPathComponent("patcher.js")), "original")
        try SoundClonerService.restore(root: root, settings: settings)
        XCTAssertEqual(try String(contentsOf: root.appendingPathComponent("dist/patcher.js")), "original")
        XCTAssertTrue(SettingsEditor.isBypassEnabled(in: try Data(contentsOf: settings)))
        XCTAssertFalse(SettingsEditor.isSoundClonerEnabled(in: try Data(contentsOf: settings)))
        XCTAssertNil(SoundClonerService.installed(in: root))
    }

    func testTamperDoesNotChangeInstall() throws {
        let (stage, root, settings, _) = try fixture()
        try Data("tampered".utf8).write(to: stage.appendingPathComponent("Vencord-SoundCloner/dist/renderer.js"))
        XCTAssertThrowsError(try SoundClonerService.install(stage: stage, root: root, settings: settings))
        XCTAssertEqual(try String(contentsOf: root.appendingPathComponent("dist/patcher.js")), "original")
        XCTAssertFalse(fm.fileExists(atPath: SoundClonerService.backup(in: root).path))
    }

    func testAutomaticUpdatePreservesDisabledFeatures() throws {
        let (stage, root, settings, _) = try fixture()
        try SoundClonerService.install(stage: stage, root: root, settings: settings)
        let disabled = try SettingsEditor.updating(Data(contentsOf: settings), enabled: false, soundCloner: false)
        try disabled.write(to: settings)
        try SoundClonerService.install(stage: stage, root: root, settings: settings, enableFeatures: false)
        XCTAssertEqual(try Data(contentsOf: settings), disabled)
    }

    func testSettingsFailureRollsBackDist() throws {
        let (stage, root, _, _) = try fixture()
        let badSettings = root.appendingPathComponent("directory.json")
        try fm.createDirectory(at: badSettings, withIntermediateDirectories: false)
        XCTAssertThrowsError(try SoundClonerService.swap(source: stage.appendingPathComponent("Vencord-SoundCloner/dist"),
            root: root, settings: badSettings, updated: Data("{}".utf8), marker: nil))
        XCTAssertEqual(try String(contentsOf: root.appendingPathComponent("dist/patcher.js")), "original")
    }

    func testRejectInvalidManifestAndVersion() throws {
        let (_, _, _, manifest) = try fixture()
        let invalid = PluginManifest(schemaVersion: 1, helperVersion: "2.1.0", pluginVersion: "1.0.0",
            vencordCommit: manifest.vencordCommit, files: manifest.files + [.init(path: "dist/../../escape", sha256: String(repeating: "0", count: 64))])
        XCTAssertThrowsError(try invalid.validate())
        XCTAssertNil(AppVersion(""))
        XCTAssertNil(AppVersion("v"))
    }

    func testReadOnlyUpdateTarget() {
        let home = URL(fileURLWithPath: "/Users/test")
        let app = URL(fileURLWithPath: "/Applications/Discord 4K Helper.app")
        XCTAssertEqual(AppUpdateService.target(for: app, parentWritable: false, home: home).path,
                       "/Users/test/Applications/Discord 4K Helper.app")
        XCTAssertEqual(AppUpdateService.target(for: app, parentWritable: true, home: home), app)
        XCTAssertTrue(AppUpdateService.target(for: URL(fileURLWithPath: "/tmp/AppTranslocation/abc/Discord 4K Helper.app"),
                                             parentWritable: true, home: home).path.hasPrefix(home.path))
    }

    func testZIPValidationAndTampering() async throws {
        let (stage, _, _, _) = try fixture()
        let archive = temporary.appendingPathComponent("test.zip")
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        process.currentDirectoryURL = stage
        process.arguments = ["-qr", archive.path, "Vencord-SoundCloner"]
        try process.run(); process.waitUntilExit()
        XCTAssertEqual(process.terminationStatus, 0)
        let data = try Data(contentsOf: archive)
        XCTAssertNoThrow(try PluginZip.validate(data))
        var malicious = data
        if let range = malicious.range(of: Data("dist/renderer.js".utf8)) {
            malicious.replaceSubrange(range, with: Data("../x/renderer.js".utf8))
        }
        XCTAssertThrowsError(try PluginZip.validate(malicious))
        XCTAssertThrowsError(try PluginZip.validate(Data()))
        let link = stage.appendingPathComponent("Vencord-SoundCloner/dist/link.js")
        try fm.createSymbolicLink(at: link, withDestinationURL: URL(fileURLWithPath: "/tmp"))
        let symlinkProcess = Process()
        symlinkProcess.executableURL = process.executableURL
        symlinkProcess.currentDirectoryURL = stage
        symlinkProcess.arguments = ["-qry", archive.path, "Vencord-SoundCloner"]
        try symlinkProcess.run(); symlinkProcess.waitUntilExit()
        XCTAssertThrowsError(try PluginZip.validate(Data(contentsOf: archive)))
    }

    func testReleasePackageWhenProvided() async throws {
        guard let path = ProcessInfo.processInfo.environment["SOUNDCLONER_PACKAGE"] else { throw XCTSkip("Set SOUNDCLONER_PACKAGE to verify a real release artifact") }
        try PluginZip.validate(Data(contentsOf: URL(fileURLWithPath: path)))
        _ = try await ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/ditto"), arguments: ["-x", "-k", path, temporary.path])
        let package = temporary.appendingPathComponent("Vencord-SoundCloner")
        let manifest = try JSONDecoder().decode(PluginManifest.self, from: Data(contentsOf: package.appendingPathComponent("manifest.json")))
        try manifest.verify(directory: package)
    }
}
