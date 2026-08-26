import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { runInNewContext } from "node:vm";
import test from "node:test";

const require = createRequire(resolve(".third-party/Vencord/package.json"));
const { transform } = require("esbuild");
const source = await readFile("VencordPlugin/SoundCloner/index.tsx", "utf8");
const { code } = await transform(source, { loader: "tsx", format: "cjs" });
const guilds = {
    source: { id: "source", name: "Source", ownerId: "me" },
    owned: { id: "owned", name: "Owned", ownerId: "me" },
    allowed: { id: "allowed", name: "Allowed", ownerId: "other" },
    denied: { id: "denied", name: "Denied", ownerId: "other" }
};
const sound = { soundId: "123", guildId: "source", name: "Sound" };
const common = {
    GuildStore: { getGuilds: () => guilds, getGuild: id => guilds[id] },
    UserStore: { getCurrentUser: () => ({ id: "me" }) },
    PermissionStore: { getGuildPermissions: ({ id }) => id === "allowed" ? 1n : 0n },
    PermissionsBits: { CREATE_GUILD_EXPRESSIONS: 1n },
    SoundboardStore: { getSoundById: id => id === "123" ? sound : { soundId: id, guildId: "" } },
    React: { createElement: (type, props, ...children) => ({ type, props, children }) },
    Menu: { MenuItem: "item" }
};
const module = { exports: {} };
runInNewContext(code, {
    module, exports: module.exports,
    require: id => {
        if (id === "@webpack/common") return common;
        if (id === "@utils/types") return value => value;
        if (id === "@webpack") return { findByCodeLazy: () => () => {} };
        return {};
    },
    fetch: async () => { throw new Error("network"); },
    AbortSignal
});
const plugin = module.exports;

test("only eligible targets, excluding source", () => {
    assert.deepEqual(Array.from(plugin.eligibleGuilds("source"), g => g.id), ["allowed", "owned"]);
});
test("sound context props resolve through store and exclude default sounds", () => {
    assert.equal(plugin.soundFromProps([{ sound }]), sound);
    assert.equal(plugin.soundFromProps([{ soundId: "123" }]), sound);
    assert.equal(plugin.soundFromProps([{ sound_id: "123" }]), sound);
    assert.equal(plugin.soundFromProps([{ soundId: "builtin" }]), undefined);
    assert.equal(plugin.soundFromProps([null, {}, { target: {} }]), undefined);
});
test("sound menu gains copy action only for a server sound", () => {
    const children = [];
    plugin.default.contextMenus["sound-button-context"](children, { sound });
    assert.equal(children.length, 1);
    assert.equal(children[0].props.label, "複製到其他伺服器…");
    const builtIn = [];
    plugin.default.contextMenus["sound-button-context"](builtIn, { soundId: "builtin" });
    assert.equal(builtIn.length, 0);
});
test("permission, capacity, rate limit and download errors are explicit", () => {
    assert.match(plugin.uploadError({ status: 403 }), /權限/);
    assert.match(plugin.uploadError({ body: { code: 30045 } }), /已滿/);
    assert.match(plugin.uploadError({ status: 429 }), /太頻繁/);
    assert.match(plugin.uploadError(new Error("download:404")), /下載/);
    assert.match(plugin.uploadError(new Error("network")), /以免重複/);
});
test("CDN failure rejects before any upload", async () => {
    await assert.rejects(plugin.soundDataUrl("123"), /network/);
});
