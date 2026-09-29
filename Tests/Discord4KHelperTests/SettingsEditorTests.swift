import XCTest
@testable import Discord4KHelper

final class SettingsEditorTests: XCTestCase {
    func testEnablesBypassWithoutRemovingOtherSettings() throws {
        let input = Data(#"{"theme":"dark","plugins":{"FakeNitro":{"emojiSize":48},"Other":{"enabled":true}}}"#.utf8)
        let output = try SettingsEditor.updating(input, enabled: true)
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: output) as? [String: Any])
        let plugins = try XCTUnwrap(root["plugins"] as? [String: Any])
        let fakeNitro = try XCTUnwrap(plugins["FakeNitro"] as? [String: Any])

        XCTAssertEqual(root["theme"] as? String, "dark")
        XCTAssertEqual(fakeNitro["emojiSize"] as? Int, 48)
        XCTAssertEqual(fakeNitro["enabled"] as? Bool, true)
        XCTAssertEqual(fakeNitro["enableStreamQualityBypass"] as? Bool, true)
        XCTAssertTrue(SettingsEditor.isBypassEnabled(in: output))
    }

    func testVersionComparison() throws {
        XCTAssertLessThan(try XCTUnwrap(AppVersion("v1.9.9")), try XCTUnwrap(AppVersion("2.0.0")))
        XCTAssertEqual(AppVersion("2.0"), AppVersion("v2.0.0"))
        XCTAssertNil(AppVersion("latest"))
        for invalid in ["2..2", "2.", "vv2", "2v", "2.2-beta", "1.-2", "999999999999999999999999"] {
            XCTAssertNil(AppVersion(invalid), invalid)
        }
        XCTAssertEqual(AppVersion("2.2.0.0"), AppVersion("v2.2.0"))
    }

    func testDisablingPreservesPluginStateAndOtherSettings() throws {
        let input = Data(#"{"plugins":{"FakeNitro":{"enabled":false,"enableEmojiBypass":true},"SoundCloner":{"enabled":true}}}"#.utf8)
        let output = try SettingsEditor.updating(input, enabled: false)
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: output) as? [String: Any])
        let plugins = try XCTUnwrap(root["plugins"] as? [String: Any])
        let fake = try XCTUnwrap(plugins["FakeNitro"] as? [String: Any])
        XCTAssertEqual(fake["enabled"] as? Bool, false)
        XCTAssertEqual(fake["enableEmojiBypass"] as? Bool, true)
        XCTAssertFalse(SettingsEditor.isBypassEnabled(in: output))
        XCTAssertNotNil(plugins["SoundCloner"])
        let migrated = try SettingsEditor.updating(output, enabled: true, removeLegacySoundCloner: true)
        let migratedRoot = try XCTUnwrap(JSONSerialization.jsonObject(with: migrated) as? [String: Any])
        XCTAssertNil((migratedRoot["plugins"] as? [String: Any])?["SoundCloner"])
        XCTAssertTrue(SettingsEditor.isBypassEnabled(in: migrated))
    }

    func testRejectsMalformedSettings() {
        for json in ["[]", "null", "{", #"{"plugins":[]}"#, #"{"plugins":null}"#, #"{"plugins":{"FakeNitro":false}}"#] {
            XCTAssertThrowsError(try SettingsEditor.updating(Data(json.utf8), enabled: true), json)
        }
    }

    func testLegacyMigrationSuccessAndRollback() async throws {
        let fm = FileManager.default
        let root = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let dist = root.appendingPathComponent("dist")
        try fm.createDirectory(at: dist, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: root) }
        let marker = dist.appendingPathComponent(".soundcloner-manifest.json")
        try Data("old".utf8).write(to: dist.appendingPathComponent("patcher.js"))
        try Data("{}".utf8).write(to: marker)
        XCTAssertFalse(VencordInstallService.isInstalled(in: root))

        for throwsError in [true, false] {
            do {
                try await VencordInstallService.migrateLegacyBuild(in: root) {
                    try fm.createDirectory(at: dist, withIntermediateDirectories: true)
                    try Data("partial".utf8).write(to: dist.appendingPathComponent("patcher.js"))
                    if throwsError { throw HelperError.downloadFailed }
                }
                XCTFail("Incomplete install accepted")
            } catch { }
            XCTAssertEqual(try String(contentsOf: dist.appendingPathComponent("patcher.js")), "old")
            XCTAssertTrue(fm.fileExists(atPath: marker.path))
        }

        try await VencordInstallService.migrateLegacyBuild(in: root) {
            try fm.createDirectory(at: dist, withIntermediateDirectories: true)
            for file in ["patcher.js", "preload.js", "renderer.js", "renderer.css", "package.json"] {
                try Data("official".utf8).write(to: dist.appendingPathComponent(file))
            }
        }
        XCTAssertTrue(VencordInstallService.isInstalled(in: root))
        XCTAssertFalse(fm.fileExists(atPath: marker.path))
        XCTAssertEqual(try fm.contentsOfDirectory(atPath: root.path), ["dist"])
        try await VencordInstallService.migrateLegacyBuild(in: root) { XCTFail("Official build must not migrate again") }
    }

    func testUpdateTargetForTranslocatedApp() {
        let home = URL(fileURLWithPath: "/Users/example")
        let bundle = URL(fileURLWithPath: "/Applications/Discord 4K Helper.app")
        XCTAssertEqual(AppUpdateService.target(for: bundle, parentWritable: true, home: home), bundle)
        XCTAssertEqual(AppUpdateService.target(for: bundle, parentWritable: false, home: home), home.appendingPathComponent("Applications/Discord 4K Helper.app"))
        XCTAssertEqual(AppUpdateService.target(for: URL(fileURLWithPath: "/private/AppTranslocation/random/Discord 4K Helper.app"), parentWritable: true, home: home), home.appendingPathComponent("Applications/Discord 4K Helper.app"))
    }

    func testUpdaterRestoresOriginalWhenCopyOrLaunchFails() async throws {
        let fm = FileManager.default
        let root = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try fm.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: root) }
        for failure in ["none", "copy", "launch"] {
            let target = root.appendingPathComponent("Helper.app")
            let stage = root.appendingPathComponent("stage")
            let replacement = stage.appendingPathComponent("Helper.app")
            let script = root.appendingPathComponent("updater.sh")
            try Data("original".utf8).write(to: target)
            try fm.createDirectory(at: stage, withIntermediateDirectories: true)
            try Data("new".utf8).write(to: replacement)
            var source = AppUpdateService.updaterScript
                .replacingOccurrences(of: "while kill -0", with: "while false")
                .replacingOccurrences(of: "/usr/bin/open", with: failure == "launch" ? "/usr/bin/false" : "/usr/bin/true")
            if failure == "copy" { source = source.replacingOccurrences(of: "/usr/bin/ditto", with: "/usr/bin/false") }
            try Data(source.utf8).write(to: script)
            _ = try await ProcessRunner.run(URL(fileURLWithPath: "/bin/sh"), arguments: [script.path, target.path, replacement.path, stage.path, "0"])
            XCTAssertEqual(try String(contentsOf: target), failure == "none" ? "new" : "original", failure)
            XCTAssertEqual(try fm.contentsOfDirectory(atPath: root.path), ["Helper.app"])
        }
    }
}
