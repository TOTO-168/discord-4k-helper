# 測試與驗收

## 自動測試

- `swift test`：設定保留、關閉繞過不啟用外掛、舊設定清除、錯誤 JSON、版本判讀、舊建置轉換成功與失敗還原、macOS 更新目標路徑、實際更新 shell 在複製／開啟失敗時回復原檔。
- Windows `--self-test`：設定原始備份、設定保留、錯誤 JSON 不覆寫、版本判讀、舊建置轉換與還原、PowerShell 更新失敗回復。
- `node scripts/verify-versions.mjs`：macOS、Windows 版本及發布 tag 一致。
- `./scripts/build-app.sh`：Swift 測試、arm64 Release 建置及 ad-hoc 簽署封裝。
- GitHub Actions：版本檢查、Mac 建置與測試、Windows publish 與原生 self-test。

測試使用暫存檔案，不修改真實 Discord，也不操作伺服器音效。

## 實機驗收

- [ ] 新安裝：主按鈕安裝 Vencord、啟用畫質選項，Discord 正常重啟。
- [ ] 已安裝：啟用／關閉畫質繞過，其他外掛設定保持不變。
- [ ] v2.1.0：手動升級 Helper 後按主按鈕，音效版轉回官方 Vencord，音效複製選單消失。
- [ ] 中斷轉換下載：錯誤有提示、原本 `dist` 還原，可重試。
- [ ] Discord 更新後：確認仍有載入 Vencord；若未載入，以官方 Installer 修復。
- [ ] Helper 新版本：正常下載、替換、重新開啟；不可寫路徑有正確處理。
- [ ] 實際串流：確認來源與觀看端解析度，不能以 Helper 狀態代替驗收。

Windows WPF 視窗、真實 Discord 安裝／重啟與跨版本自動更新仍需對應平台實測。建置通過不代表上述人工項目已完成。
