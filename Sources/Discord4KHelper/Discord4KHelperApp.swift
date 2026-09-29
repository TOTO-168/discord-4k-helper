import SwiftUI

@main
struct Discord4KHelperApp: App {
    @StateObject private var model = HelperModel()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(model)
                .frame(minWidth: 560, idealWidth: 620, minHeight: 740, idealHeight: 780)
        }
        .windowResizability(.contentMinSize)
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
                    StatusRow(
                        title: "版本",
                        detail: "v\(model.currentVersion)",
                        active: !model.updateAvailable
                    )
                }

                if let notice = model.notice {
                    Section {
                        HStack(spacing: 10) {
                            if model.isBusy {
                                ProgressView()
                                    .controlSize(.small)
                                    .accessibilityHidden(true)
                            }
                            Label(notice, systemImage: model.noticeIsError ? "exclamationmark.triangle.fill" : "info.circle.fill")
                                .foregroundStyle(model.noticeIsError ? .red : .secondary)
                        }
                        .accessibilityElement(children: .combine)
                        .accessibilityLabel((model.noticeIsError ? "錯誤：" : "狀態：") + notice)
                    }
                }

                Section("操作") {
                    Button {
                        model.installFeatures()
                    } label: {
                        Label("安裝並啟用 4K 畫質選項", systemImage: "arrow.down.app.fill")
                            .frame(maxWidth: .infinity, minHeight: 32)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(model.isBusy || model.isCheckingUpdate || !model.discordInstalled)
                    Text("操作會重新啟動 Discord，請先結束通話或直播。")
                        .font(.caption).foregroundStyle(.secondary)

                    HStack {
                        if model.vencordInstalled {
                            Button("關閉畫質繞過") {
                                model.applyBypass(false)
                            }
                            .disabled(model.isBusy || model.isCheckingUpdate || !model.bypassEnabled)
                        }

                        Button("重新檢查") {
                            model.refresh()
                        }
                        .disabled(model.isBusy)

                        Spacer()

                        if model.discordInstalled {
                            Button("開啟 Discord") {
                                model.openDiscord()
                            }
                            .disabled(model.isBusy)
                        }
                    }
                }

            }
            .formStyle(.grouped)

            // Keep updates reachable even while the status/operation form is scrolled.
            VStack(alignment: .leading, spacing: 8) {
                Text("軟體更新").font(.headline)
                HStack {
                    Text(model.updateAvailable
                         ? "可更新至 v\(model.latestVersion ?? "新版")"
                         : "目前版本 v\(model.currentVersion)")
                        .foregroundStyle(.secondary)
                    Spacer()
                    if model.isCheckingUpdate {
                        ProgressView().controlSize(.small).accessibilityLabel("正在檢查更新")
                    }
                    Button(model.updateAvailable ? "下載並安裝更新" : "檢查更新") {
                        model.installUpdate()
                    }
                    .disabled(model.isBusy || model.isCheckingUpdate)
                }
            }

            footer
        }
        .padding(24)
        .task {
            await model.checkForUpdates(silent: true)
        }
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
                Text("Discord 串流畫質設定工具")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var footer: some View {
        Text("提醒：4K 選項不保證觀看端收到 4K。使用自訂 Vencord 可能違反 Discord 使用條款。")
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
