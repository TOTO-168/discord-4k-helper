# v2.1.0 驗收

## 自動化

- 外掛：目標伺服器權限、排除來源/內建音效、右鍵選單、下載失敗與 API 錯誤映射。
- Swift：設定獨立性、保留其他外掛、重複安裝/還原、第一次備份不覆蓋、hash 竄改拒絕、設定寫入失敗 rollback、ZIP 路徑/符號連結拒絕、Translocation 更新路徑、真實 Release 封裝。
- Windows self-test：同樣的設定/安裝/備份/還原/rollback/hash/ZIP 測試，包含 Windows 保留檔名與真實 Release 封裝。
- CI：固定 Vencord commit、TypeScript、外掛 lint、完整源碼封裝核對、Mac arm64 與 Windows x64 建置。

自動化不操作已登入 Discord，也不發送任何真正的音效上傳或播放請求。

## 需人工驗收（不視為自動化已驗證）

- [ ] macOS Apple Silicon 與 Windows x64 開啟 App，所有狀態、進度及錯誤訊息可讀。
- [ ] 全新官方 Discord：Helper 安裝官方 Vencord 後音效外掛可用。
- [ ] 已有官方 Vencord：第一次替換產生原始 dist 備份。
- [ ] 已有 userplugins：先顯示警告；取消時沒有替換；確認後可還原原始外掛。
- [ ] 免費帳號可在 B 服音效上按右鍵，看到「複製到其他伺服器…」。
- [ ] 無建立表情內容權限的 A 服不出現在選單。
- [ ] 有權限且有空位的 A 服成功新增音效，名稱/Unicode 表情正確；自訂表情回退 🔊。
- [ ] 第二個帳號在 A 服播放及收聽新增音效，不需要複製者擁有 Nitro。
- [ ] 目標已滿/權限撤銷/CDN 不可用/Discord API 拒絕時有明確訊息，不自動重試上傳。
- [ ] 下一個 Release 提示 App 更新，點擊後一併更新已安裝的音效功能並重啟 Discord；未安裝音效功能者不會被自動替換 Vencord。
- [ ] Mac 從 App Translocation/不可寫位置更新，結果位於使用者 Applications；舊 v2.0.x 手動升級一次。
- [ ] 還原後 SoundCloner 移除，但 FakeNitro/其他外掛設定與原始備份保留；伺服器已新增的音效仍存在。

## 相容性依據

Vencord 固定 `ef29bbeb6119cfb53d1273ed78147bcc97d91261`；音效右鍵 navId 為 `sound-button-context`，props 含 `sound` 或 `soundId`，以 SoundboardStore 再查證。使用 Discord 原生上傳函式，透過 `.GUILD_SOUNDBOARD_SOUNDS(` 與 `emoji_name:` 找到函式，不假設被壓縮的匯出名稱仍為 `uploadSound`。

這些介面不屬於 Discord 穩定公開客戶端 API。升級 Vencord 或 Discord 後須重做人工驗收；無法以 TypeScript 編譯成功替代實際音效播放測試。
