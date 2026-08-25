# Discord 4K Helper

一個原生小工具，用來安裝 Vencord、啟用 FakeNitro 的串流畫質繞過功能，並重新啟動 Discord。提供 Apple Silicon macOS 與 Windows x64 版本。

## 下載與使用

前往 [最新版本](https://github.com/TOTO-168/discord-4k-helper/releases/latest) 下載：

- macOS：`Discord-4K-Helper-macOS-arm64.zip`
- Windows：`Discord-4K-Helper-Windows-x64.exe`

開啟程式後按「安裝 Vencord 並啟用 4K」。如果電腦已經裝好 Vencord，程式只會啟用畫質選項，不會重複安裝。

分享 4K 螢幕時，在 Discord 選擇「自訂 → 來源 → 60 FPS」。`來源` 只有在分享 3840×2160 畫面時才代表 4K。

### 系統安全提示

目前程式未使用付費的 Apple Developer ID 或 Windows 程式碼簽章憑證：

- macOS 若阻擋 App，請在 Finder 對 App 按右鍵，選擇「打開」。
- Windows 若顯示 Microsoft Defender SmartScreen，請確認檔案是從本儲存庫下載，再選「其他資訊 → 仍要執行」。

## 自動更新

程式啟動時會檢查本儲存庫的最新 GitHub Release。看到新版本後按「下載並安裝更新」，程式會下載對應平台的檔案、自動替換目前版本並重新開啟。

發布新版本時：

1. 更新 `Info.plist` 與 Windows `.csproj` 的版本號。
2. 提交並推送到 `main`，等待 GitHub Actions 測試通過。
3. 建立並推送相同版本的 tag，例如 `git tag v2.1.0 && git push origin v2.1.0`。

GitHub Actions 會自動測試、建立 Mac/Windows 檔案並發布 Release；使用者之後會收到更新提示。

## 它會修改什麼

程式只會更新 Vencord `settings/settings.json` 中的兩個設定：

- `plugins.FakeNitro.enabled`
- `plugins.FakeNitro.enableStreamQualityBypass`

第一次修改前會在相同資料夾建立 `settings.before-discord-4k-helper.json` 備份。程式不會直接修改 Discord 應用程式；尚未安裝 Vencord 時，才會下載並執行官方 Vencord Installer。

## 從原始碼建置

macOS：

```bash
./scripts/build-app.sh
```

Windows：

```powershell
dotnet publish Windows/Discord4KHelper.Windows/Discord4KHelper.Windows.csproj -c Release -r win-x64 --self-contained true
```

第三方元件授權資訊請見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 限制與提醒

- 免費帳號的官方直播上限仍由 Discord 決定。
- 工具只解除客戶端畫質選項，Discord 伺服器仍可能降低解析度或位元率，無法保證觀看端收到 4K。
- Vencord 是第三方 Discord 客戶端修改，可能違反 Discord 使用條款。
