import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile, mkdtemp, rm, readdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { execFileSync } from "node:child_process";

const output = resolve(process.argv[2] ?? "release-assets");
const stage = await mkdtemp(join(tmpdir(), "soundcloner-verify-"));
try {
    execFileSync("unzip", ["-q", join(output, "Vencord-SoundCloner.zip"), "-d", stage]);
    const root = join(stage, "Vencord-SoundCloner");
    const manifest = JSON.parse(await readFile(join(root, "manifest.json"), "utf8"));
    assert.deepEqual(manifest, JSON.parse(await readFile(join(output, "Vencord-SoundCloner-manifest.json"), "utf8")));
    assert.deepEqual(new Set((await readdir(join(root, "dist"))).map(p => "dist/" + p)), new Set(manifest.files.map(f => f.path)));
    for (const file of manifest.files) {
        assert.match(file.path, /^dist\/[A-Za-z0-9_-][A-Za-z0-9._-]*$/);
        assert.equal(createHash("sha256").update(await readFile(join(root, file.path))).digest("hex"), file.sha256);
    }
    const config = JSON.parse(await readFile(new URL("../VencordPlugin/build.json", import.meta.url), "utf8"));
    for (const key of Object.keys(config)) assert.equal(manifest[key], config[key]);
    const listing = execFileSync("tar", ["-tzf", join(output, "Vencord-SoundCloner-Source.tar.gz")], { encoding: "utf8" });
    for (const file of ["LICENSE", "pnpm-lock.yaml", "package.json", "scripts/build/build.mjs", "src/userplugins/SoundCloner/index.tsx", "src/userplugins/SoundCloner/LICENSE"])
        assert.ok(listing.split("\n").includes("./" + file), "Source missing " + file);
    assert.ok(!listing.includes("node_modules/"));
    const plugin = execFileSync("tar", ["-xOzf", join(output, "Vencord-SoundCloner-Source.tar.gz"), "./src/userplugins/SoundCloner/index.tsx"], { encoding: "utf8" });
    assert.equal(plugin, await readFile(new URL("../VencordPlugin/SoundCloner/index.tsx", import.meta.url), "utf8"));
    console.log("Package manifest, hashes, pinned commit, and complete source archive verified.");
} finally { await rm(stage, { recursive: true, force: true }); }
