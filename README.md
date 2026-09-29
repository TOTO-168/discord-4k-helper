# Discord 4K Helper v2.2.0

原生 macOS（Apple Silicon、macOS 13+）與 Windows x64 小工具，用來安裝 Vencord 及切換 Discord 串流畫質選項。

## 使用方式

從[最新 GitHub Release](https://github.com/TOTO-168/discord-4k-helper/releases/latest) 下載 macOS ZIP 或 Windows EXE。Mac 解壓後將 App 移到「應用程式」。

先結束 Discord 通話或直播，再按「安裝並啟用 4K 畫質選項」。未安裝 Vencord 時會下載安裝程式；完成後重新啟動 Discord。已有完整 Vencord 檔案時只修改畫質設定。「關閉畫質繞過」保留其他外掛設定。

本工具只開啟客戶端畫質選項，實際解析度仍受來源與 Discord 控制，不能保證觀看端收到 4K。Discord 更新後若外掛不再載入，請以官方 Vencord Installer 修復；本工具的安裝狀態代表本機 Vencord 檔案完整，並非 Discord 已成功載入的即時證明。

## 從音效版升級

v2.2.0 已移除音效複製的介面、外掛、下載與更新流程。

v2.1.0 的舊更新器仍依賴音效封裝，因此已安裝音效版的使用者需手動下載新版 Helper。開啟新版後按「安裝並啟用 4K 畫質選項」，會偵測舊版標記，暫存原本的 `dist`，重新安裝官方 Vencord，成功後清除 SoundCloner 設定。下載或安裝失敗會還原原本的 `dist`，可以重試。僅開啟 Helper 不會自動關閉或修改 Discord。

最早的 `discord-4k-helper-backup/dist` 備份會保留。已經上傳到 Discord 伺服器的音效不受影響。

## 更新與本機資料

Helper 啟動時只檢查 Helper 新版本，按「下載並安裝更新」才安裝。macOS 從 App Translocation 或無法寫入的位置更新時，改安裝到 `~/Applications/Discord 4K Helper.app`。

Vencord 根目錄為 Mac 的 `~/Library/Application Support/Vencord` 或 Windows 的 `%APPDATA%\Vencord`。設定檔在 `settings/settings.json`；第一次修改前會保留 `settings.before-discord-4k-helper.json`，後續操作不覆蓋這份備份。格式錯誤時停止寫入。

## 建置與測試

Mac 需要 Xcode command-line tools；Windows 需要 .NET 8 SDK；版本檢查使用 Node.js。

```sh
node scripts/verify-versions.mjs
./scripts/build-app.sh
```

Mac 建置腳本先執行 Swift 測試，再產生 `dist/Discord-4K-Helper-macOS-arm64.zip`。

Windows：

```powershell
dotnet publish Windows/Discord4KHelper.Windows/Discord4KHelper.Windows.csproj -c Release -r win-x64 --self-contained true --output release-assets
$env:SELF_TEST_LOG = "$pwd/windows-self-test.log"
$p = Start-Process ./release-assets/Discord-4K-Helper-Windows-x64.exe -ArgumentList '--self-test' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Self-test failed' }
```

自動測試只使用暫存資料。實機驗收與目前驗證範圍見 [TESTING.md](TESTING.md)。

## 發布

同步更新 `Info.plist`、Windows `.csproj`、介面與程式中的版本字串及 release notes，執行版本檢查。推送同版本的 `vX.Y.Z` tag 後，Actions 建置並發布 Mac ZIP、Windows EXE 及 macOS Vencord 安裝程式；不再建置或發布音效外掛。

程式目前沒有 Apple Developer ID 公證或 Windows 程式碼簽章；第三方資訊見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
