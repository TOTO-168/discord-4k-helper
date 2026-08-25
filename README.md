# Discord 4K Helper for macOS

一個原生 SwiftUI 小工具，用來啟用 Vencord 內建 FakeNitro 的串流畫質繞過功能，並重新啟動 Discord。

## 需求

- Apple Silicon Mac
- macOS 13 或更新版本
- Discord Desktop
- 已安裝 [Vencord](https://vencord.dev/download/)

## 下載與使用

1. 從 [Releases](https://github.com/TOTO-168/discord-4k-helper-macos/releases/latest) 下載 `Discord-4K-Helper-macOS-arm64.zip`。
2. 解壓縮並開啟 `Discord 4K Helper.app`。
3. 按下「啟用 4K 選項並重啟 Discord」。
4. 分享 4K 螢幕時選擇「自訂 → 來源 → 60 FPS」。

如果 macOS 阻擋未公證的 App，請在 Finder 中按右鍵並選擇「打開」。

## 它會修改什麼

程式只會更新 Vencord `settings/settings.json` 中的兩個設定：

- `plugins.FakeNitro.enabled`
- `plugins.FakeNitro.enableStreamQualityBypass`

第一次修改前會在相同資料夾建立 `settings.before-discord-4k-helper.json` 備份。程式不會直接修改 `Discord.app`。

## 從原始碼建置

```bash
./scripts/build-app.sh
```

腳本會執行測試、建置 arm64 App、進行 ad-hoc 簽署，並輸出 zip 到 `dist/`。

## 限制與提醒

- 「來源」代表擷取來源的原始解析度；只有分享 3840×2160 螢幕時才是 4K。
- 工具只解除客戶端畫質選項，Discord 伺服器仍可能降低解析度或位元率。
- Vencord 是第三方 Discord 客戶端修改，可能違反 Discord 使用條款。
