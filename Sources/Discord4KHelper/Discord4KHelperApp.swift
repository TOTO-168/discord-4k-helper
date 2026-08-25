import SwiftUI

@main
struct Discord4KHelperApp: App {
    @StateObject private var model = HelperModel()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(model)
                .frame(width: 560, height: 560)
        }
        .windowResizability(.contentSize)
    }
}

struct ContentView: View {
    @EnvironmentObject private var model: HelperModel

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            header

            Form {
                Section("目前狀態") {
                    StatusRow(
                        title: "Discord",
                        detail: model.discordInstalled ? "已安裝" : "找不到應用程式",
                        active: model.discordInstalled
                    )
                    StatusRow(
                        title: "Vencord",
                        detail: model.vencordInstalled ? "已安裝" : "尚未安裝",
                        active: model.vencordInstalled
                    )
                    StatusRow(
                        title: "4K 畫質選項",
                        detail: model.bypassEnabled ? "已啟用（仍受 Discord 伺服器控制）" : "未啟用",
                        active: model.bypassEnabled
                    )
                }

                if let notice = model.notice {
                    Section {
                        Label(notice, systemImage: model.noticeIsError ? "exclamationmark.triangle.fill" : "info.circle.fill")
                            .foregroundStyle(model.noticeIsError ? .red : .secondary)
                            .accessibilityLabel((model.noticeIsError ? "錯誤：" : "狀態：") + notice)
                    }
                }

                Section("操作") {
                    Button {
                        model.applyBypass(true)
                    } label: {
                        Label("啟用 4K 選項並重啟 Discord", systemImage: "display.2")
                            .frame(maxWidth: .infinity, minHeight: 32)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(model.isBusy || !model.vencordInstalled || !model.discordInstalled)

                    HStack {
                        Button("關閉畫質繞過") {
                            model.applyBypass(false)
                        }
                        .disabled(model.isBusy || !model.vencordInstalled)

                        Button("重新檢查") {
                            model.refresh()
                        }
                        .disabled(model.isBusy)

                        Spacer()

                        if model.discordInstalled {
                            Button("開啟 Discord") {
                                model.openDiscord()
                            }
                        }
                    }
                }

                if !model.vencordInstalled {
                    Section("需要 Vencord") {
                        Text("這個工具只切換 Vencord 內建的 FakeNitro 串流畫質功能，不會自行修改 Discord 程式檔。")
                            .foregroundStyle(.secondary)
                        Button("開啟 Vencord 官方下載頁") {
                            model.openVencordDownload()
                        }
                    }
                }
            }
            .formStyle(.grouped)

            footer
        }
        .padding(24)
    }

    private var header: some View {
        HStack(spacing: 14) {
            Image(systemName: "4k.tv.fill")
                .font(.system(size: 38))
                .foregroundStyle(.tint)
                .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 3) {
                Text("Discord 4K Helper")
                    .font(.title2.bold())
                Text("啟用 Vencord 的 Nitro 串流畫質選項")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var footer: some View {
        Text("提醒：免費帳號官方上限仍是 720p/30 FPS。此工具只能解除客戶端選項，無法保證觀看端收到 4K，且使用 Vencord 可能違反 Discord 使用條款。")
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}

private struct StatusRow: View {
    let title: String
    let detail: String
    let active: Bool

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: active ? "checkmark.circle.fill" : "xmark.circle.fill")
                .foregroundStyle(active ? .green : .secondary)
                .accessibilityHidden(true)
            Text(title)
            Spacer()
            Text(detail)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.trailing)
        }
        .accessibilityElement(children: .combine)
    }
}
