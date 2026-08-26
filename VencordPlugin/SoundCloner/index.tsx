/*
 * SoundCloner for Vencord
 * Copyright (c) 2026 TOTO-168
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { BaseText } from "@components/BaseText";
import { Flex } from "@components/Flex";
import definePlugin from "@utils/types";
import type { Guild, SoundboardSound } from "@vencord/discord-types";
import { findByCodeLazy } from "@webpack";
import {
    Button,
    Forms,
    GuildStore,
    Menu,
    Modal,
    openModal,
    PermissionsBits,
    PermissionStore,
    React,
    SearchableSelect,
    SoundboardStore,
    TextInput,
    Toasts,
    UserStore } from "@webpack/common";

// Discord mangles export names; identify its native uploadSound implementation by endpoint.
const uploadSound = findByCodeLazy(".GUILD_SOUNDBOARD_SOUNDS(", "emoji_name:");

function showToast(message: string, type: string) {
    Toasts.show({ message, type, id: Toasts.genId() });
}

export function eligibleGuilds(sourceGuildId: string): Guild[] {
    const me = UserStore.getCurrentUser()?.id;

    return Object.values(GuildStore.getGuilds())
        .filter(guild => guild.id !== sourceGuildId && (
            guild.ownerId === me ||
            (PermissionStore.getGuildPermissions({ id: guild.id }) & PermissionsBits.CREATE_GUILD_EXPRESSIONS)
                === PermissionsBits.CREATE_GUILD_EXPRESSIONS
        ))
        .sort((left, right) => left.name.localeCompare(right.name));
}

export async function soundDataUrl(soundId: string): Promise<string> {
    const response = await fetch(`https://cdn.discordapp.com/soundboard-sounds/${soundId}`, { signal: AbortSignal.timeout(30_000) });
    if (!response.ok) throw new Error(`download:${response.status}`);

    const bytes = await response.arrayBuffer();
    if (bytes.byteLength > 512_000) throw new Error("download:size");
    const blob = new Blob([bytes], { type: "audio/ogg" });
    return await new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onerror = () => reject(reader.error ?? new Error("read"));
        reader.onload = () => resolve(reader.result as string);
        reader.readAsDataURL(blob);
    });
}

export function uploadError(error: any): string {
    const status = error?.status ?? error?.response?.status;
    const code = error?.body?.code ?? error?.code;
    if (code === 30045) return "複製失敗：目標伺服器的音效空位已滿，請先移除不需要的音效。";
    if (status === 403) return "複製失敗：你沒有在這個伺服器新增音效的權限。";
    if (status === 429) return "操作太頻繁，請稍後再試；不會自動重複上傳。";
    if (status === 400) return "複製失敗：請確認音效名稱有效，而且伺服器仍有音效空位。";
    if (String(error?.message).startsWith("download:")) return "無法下載來源音效，請稍後再試。";
    return "未能確認上傳結果，請先查看目標音效板再重試，以免重複新增。";
}

function CloneSoundModal({ sound, onClose }: { sound: SoundboardSound; onClose: () => void; }) {
    const guilds = React.useMemo(() => eligibleGuilds(sound.guildId), [sound.guildId]);
    const [guildId, setGuildId] = React.useState<string>();
    const [name, setName] = React.useState(sound.name);
    const [busy, setBusy] = React.useState(false);
    const selected = guilds.find(guild => guild.id === guildId);

    async function clone() {
        if (!selected || !name.trim() || busy) return;
        setBusy(true);

        try {
            if (!eligibleGuilds(sound.guildId).some(guild => guild.id === selected.id)) {
                showToast("複製失敗：你沒有在這個伺服器新增音效的權限。", Toasts.Type.FAILURE);
                return;
            }
            await uploadSound({
                guildId: selected.id,
                name: name.trim(),
                sound: await soundDataUrl(sound.soundId),
                emojiName: sound.emojiId ? "🔊" : sound.emojiName || "🔊",
                volume: sound.volume ?? 1
            });
            showToast(`已將「${name.trim()}」新增到 ${selected.name}`, Toasts.Type.SUCCESS);
            onClose();
        } catch (error) {
            showToast(uploadError(error), Toasts.Type.FAILURE);
        } finally {
            setBusy(false);
        }
    }

    const options = guilds.map(guild => ({ label: guild.name, value: guild.id }));

    return (
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
            <div>
                <Forms.FormTitle>音效名稱（必填）</Forms.FormTitle>
                <TextInput
                    value={name}
                    maxLength={32}
                    onChange={setName}
                    placeholder="輸入音效名稱"
                    disabled={busy}
                />
            </div>

            <div>
                <Forms.FormTitle>新增到伺服器（必填）</Forms.FormTitle>
                {guilds.length ? (
                    <SearchableSelect
                        options={options}
                        value={guildId}
                        placeholder="選擇伺服器"
                        onChange={setGuildId}
                        closeOnSelect
                        isDisabled={busy}
                    />
                ) : (
                    <Forms.FormText style={{ color: "var(--text-danger)" }}>
                        找不到你有權限新增音效的其他伺服器。
                    </Forms.FormText>
                )}
            </div>

            <Forms.FormText>
                這會把音效正式新增到目標伺服器，仍受伺服器權限與音效空位限制。
            </Forms.FormText>

            <Button
                onClick={clone}
                disabled={!selected || !name.trim() || busy}
                style={{ width: "100%" }}
            >
                {busy ? "正在複製…" : "複製音效"}
            </Button>
        </div>
    );
}

export function soundFromProps(args: unknown[]): SoundboardSound | undefined {
    for (const arg of args) {
        if (!arg || typeof arg !== "object") continue;
        const props = arg as { soundId?: string; sound_id?: string; sound?: SoundboardSound; };
        const id = props.soundId ?? props.sound_id ?? props.sound?.soundId;
        if (typeof id !== "string") continue;
        try {
            const sound = SoundboardStore.getSoundById(id);
            // Built-in sounds have no real guild. Resolve membership from the store, not menu input.
            if (sound?.guildId && GuildStore.getGuild(sound.guildId)) return sound;
        } catch { /* Discord may have removed the sound while the menu was opening. */ }
    }
}

export default definePlugin({
    name: "SoundCloner",
    description: "在 Discord 音效板中把伺服器音效複製到你有管理權限的其他伺服器",
    tags: ["Voice", "Servers"],
    authors: [{ name: "TOTO-168", id: 0n }],

    contextMenus: {
        "sound-button-context": (children, ...args) => {
            const sound = soundFromProps(args);
            if (!sound) return;

            children.push(
                <Menu.MenuItem
                    id="sound-cloner"
                    key="sound-cloner"
                    label="複製到其他伺服器…"
                    action={() => openModal(modalProps => (
                        <Modal
                            {...modalProps}
                            title={
                                <Flex gap="0.5em" alignItems="center">
                                    <BaseText tag="h3" size="md" weight="medium">
                                        複製「{sound.name}」
                                    </BaseText>
                                </Flex>
                            }
                        >
                            <CloneSoundModal sound={sound} onClose={modalProps.onClose} />
                        </Modal>
                    ))}
                />
            );
        }
    }
});
