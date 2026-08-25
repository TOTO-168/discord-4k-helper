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
}
