# Discord 4K Helper v2.1.0

原生 macOS（Apple Silicon、macOS 13+）與 Windows x64 小工具：安裝 Vencord、啟用串流畫質選項，並安裝 **SoundCloner 跨伺服器音效複製**外掛。

## 下載與安裝

從[最新 GitHub Release](https://github.com/TOTO-168/discord-4k-helper/releases/latest) 下載：

- Mac：`Discord-4K-Helper-macOS-arm64.zip`，解壓後把 App 移到「應用程式」。
- Windows：`Discord-4K-Helper-Windows-x64.exe`。

先結束 Discord 通話／直播，再按「安裝／更新 4K 與音效複製功能」。第一次安裝需要網路；未裝 Vencord 時會先執行官方安裝程式，接著部署包含 SoundCloner 的受管理建置，最後重新啟動 Discord。

如果已有其他自訂 Vencord 外掛，App 會先警告：新建置只包含官方外掛及 SoundCloner，原有自訂外掛會暫時不可用，但設定與原本的 `dist` 會保留備份。這個自訂建置由 Helper 更新，不使用 Vencord 自動更新器。

### 複製音效

1. 在 Discord 打開 B 伺服器的音效板。
2. 對一個伺服器音效按右鍵，選「複製到其他伺服器…」。
3. 選 A 伺服器，確認音效名稱，再按「複製音效」。
4. 完成後，音效會成為 A 服自己的音效；有播放權限的成員可以正常播放。

目標伺服器必須有音效空位，且你必須擁有「建立表情內容」權限；沒有權限的伺服器不會列出。Unicode 表情會沿用，自訂表情改用 🔊。不複製 Discord 內建音效，也不批次掃描或下載其他音效。還原 Helper 不會刪掉已新增到伺服器的音效。

這不是免費跨服播放 Nitro：它是一次正式的新增音效操作，不繞過任何權限、槽數或播放限制。只複製你有權使用的音效。[Discord 官方音效說明](https://support.discord.com/hc/en-us/articles/12612888127767-Discord-Soundboard-Guide-Using-Adding-and-Managing-Sounds)

### 串流畫質與系統提示

在 Discord 選「自訂 → 來源 → 60 FPS」；來源本身必須有 3840×2160 像素，才有機會傳送 4K。工具只解除客戶端選項，Discord 仍可能限制實際解析度、幀率與位元率，**不能保證觀看端收到 4K**。

程式目前沒有 Apple Developer ID 公證或 Windows 付費程式碼簽章。僅從本儲存庫 Release 下載；macOS 若阻擋，依「系統設定 → 隱私權與安全性」的提示允許開啟。Windows 可能顯示 SmartScreen。

## 更新與還原

Helper 啟動時檢查最新 Release 和音效外掛 manifest；不會未經點擊就關閉 Discord 或安裝。App 有新版時按「下載並安裝更新」，已安裝的受管理音效外掛會一併更新並重啟 Discord；只有功能建置需要更新時按「安裝功能更新」。首次從 v2.0.x 升級後，仍須按主按鈕安裝新的音效功能。

v2.1.0 起，Mac 從 App Translocation 或無法寫入的位置執行時，App 更新會安裝到 `~/Applications/Discord 4K Helper.app`。**v2.0.x 的舊更新器不含這項修正**，若顯示資料夾無法寫入，請手動下載 v2.1.0 並搬到「應用程式」一次。

「還原安裝前的 Vencord」會回復第一次替換前的 `dist`、移除 `plugins.SoundCloner`，保留 4K 及其他 Vencord 設定。原始備份不會在後續更新時被覆蓋。舊版 Vencord 可能不相容於未來 Discord；還原後可再使用官方 Installer 修復。

## 本機資料與驗證

Vencord 根目錄：Mac `~/Library/Application Support/Vencord`；Windows `%APPDATA%\Vencord`。

- `settings/settings.json`：僅更新 FakeNitro 畫質設定及 `plugins.SoundCloner.enabled`；還原時只移除 SoundCloner 設定。
- `discord-4k-helper-backup/dist`：第一次替換前的原始備份。
- `dist/.soundcloner-manifest.json`：目前管理建置的版本與雜湊。
- 暫存安裝檔完成或失敗後清理；替換或設定寫入失敗會回復先前 `dist`。

ZIP 解壓前檢查路徑、重複檔名、符號連結和大小限制；安裝前逐檔驗證 manifest 的 SHA-256，並核對 Release 版本與外部 manifest。下載信任 GitHub Release 與 HTTPS；雜湊是完整性檢查，不是獨立開發者簽章。Helper 與外掛均不要求、讀取或保存你的 Discord Token；上傳由 Discord 原本登入中的客戶端模組處理。

## 建置與測試

需要 Git、Node.js 22+、pnpm 11.9.0；Mac 另需 Xcode command-line tools，Windows 需 .NET 8 SDK。固定的 Vencord commit／Helper／外掛版本在 `VencordPlugin/build.json`。

```bash
git clone https://github.com/Vendicated/Vencord .third-party/Vencord
git -C .third-party/Vencord checkout ef29bbeb6119cfb53d1273ed78147bcc97d91261
mkdir -p .third-party/Vencord/src/userplugins
cp -R VencordPlugin/SoundCloner .third-party/Vencord/src/userplugins/
pnpm --dir .third-party/Vencord install --frozen-lockfile
pnpm --dir .third-party/Vencord testTsc
node --test scripts/soundcloner.test.mjs
SOURCE_DATE_EPOCH="$(git -C .third-party/Vencord show -s --format=%ct)000" pnpm --dir .third-party/Vencord build --standalone --disable-updater
node scripts/create-vencord-package.mjs .third-party/Vencord release-assets
node scripts/verify-vencord-package.mjs release-assets
SOUNDCLONER_PACKAGE="$PWD/release-assets/Vencord-SoundCloner.zip" ./scripts/build-app.sh
```

Windows：

```powershell
dotnet publish Windows/Discord4KHelper.Windows/Discord4KHelper.Windows.csproj -c Release -r win-x64 --self-contained true --output release-assets
$env:SOUNDCLONER_PACKAGE = "$pwd/release-assets/Vencord-SoundCloner.zip"
$p = Start-Process ./release-assets/Discord-4K-Helper-Windows-x64.exe -ArgumentList '--self-test' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Self-test failed' }
```

CI 驗證外掛邏輯與 TypeScript、封裝/原始碼/雜湊、Mac Swift 測試、Windows self-test。測試只操作暫存資料，不會修改真實 Discord 安裝或上傳音效。實際上傳、第二位使用者收聽、不同版本客戶端的右鍵選單仍需真人驗收，見 [TESTING.md](TESTING.md)。

### 發布新版本

1. 更新 `VencordPlugin/build.json`、`Info.plist`、Windows `.csproj` 及 release notes 的版本；變更外掛時提升 `pluginVersion`。
2. 推送 `main`，確認 Actions 通過，再推送同版本的 `vX.Y.Z` tag。
3. Actions 建置 Mac、Windows、自訂 Vencord，發布 ZIP、獨立 manifest、完整對應原始碼與 GPL 授權。

功能更新也應發布新 Helper 版本及相應資產；不要覆寫已發布版本的檔案。完整 Vencord 原始碼封裝含外掛、lockfile、原始建置腳本及 `BUILD-SOUNDCLONER.md`，後者提供取代 `.git` 資訊所需的環境變數與固定時間戳。請依該檔案重建。

## 授權與限制

SoundCloner 與 Vencord 自訂建置使用 GPL-3.0-or-later，完整授權在 `VencordPlugin/SoundCloner/LICENSE`，第三方資訊見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本專案非 Discord 或 Vencord 官方產品。使用自訂 Vencord 可能違反 Discord 使用條款；Discord 更新也可能使功能失效。
