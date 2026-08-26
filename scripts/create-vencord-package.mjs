#!/usr/bin/env node

import { createHash } from "node:crypto";
import { cp, mkdtemp, mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, join, relative, resolve } from "node:path";
import { spawnSync } from "node:child_process";

const config = JSON.parse(await readFile(new URL("../VencordPlugin/build.json", import.meta.url), "utf8"));
const [input, output, helperVersion = config.helperVersion, pluginVersion = config.pluginVersion] = process.argv.slice(2);
if (!input || !output) {
    throw new Error("usage: create-vencord-package.mjs <vencord-dir> <output-dir> [helper-version] [plugin-version]");
}
const vencordDir = resolve(input);
const outputDir = resolve(output);

const requiredFiles = ["patcher.js", "preload.js", "renderer.js", "renderer.css"];
for (const file of requiredFiles) await readFile(join(vencordDir, "dist", file));

const stageRoot = await mkdtemp(join(tmpdir(), "discord-4k-vencord-"));
const stage = join(stageRoot, "Vencord-SoundCloner");
await mkdir(join(stage, "dist"), { recursive: true });
await mkdir(outputDir, { recursive: true });

try {
    for (const file of await readdir(join(vencordDir, "dist"))) {
        if (/^(patcher|preload|renderer)\.(js|css)(\.map|\.LEGAL\.txt)?$/.test(file)) {
            await cp(join(vencordDir, "dist", file), join(stage, "dist", file));
        }
    }
    await writeFile(join(stage, "dist", "package.json"), "{}\n");
    await cp(join(vencordDir, "LICENSE"), join(stage, "Vencord-LICENSE.txt"));

    const files = [];
    async function collect(directory) {
        for (const entry of await readdir(directory, { withFileTypes: true })) {
            const path = join(directory, entry.name);
            if (entry.isDirectory()) await collect(path);
            else if (entry.isFile()) {
                const data = await readFile(path);
                files.push({
                    path: relative(stage, path).replaceAll("\\", "/"),
                    sha256: createHash("sha256").update(data).digest("hex")
                });
            }
        }
    }
    await collect(join(stage, "dist"));
    files.sort((a, b) => a.path.localeCompare(b.path));

    const git = spawnSync("git", ["-C", vencordDir, "rev-parse", "HEAD"], { encoding: "utf8" });
    if (git.status !== 0) throw new Error(git.stderr || "cannot read Vencord commit");
    if (git.stdout.trim() !== config.vencordCommit) throw new Error("Vencord checkout does not match the pinned commit");

    await writeFile(join(stage, "manifest.json"), JSON.stringify({
        schemaVersion: 1,
        helperVersion,
        pluginVersion,
        vencordCommit: git.stdout.trim(),
        files
    }, null, 2) + "\n");
    await cp(join(stage, "manifest.json"), join(outputDir, "Vencord-SoundCloner-manifest.json"));

    const archive = join(outputDir, "Vencord-SoundCloner.zip");
    await rm(archive, { force: true });
    const zip = spawnSync("zip", ["-qry", archive, basename(stage)], { cwd: stageRoot, encoding: "utf8" });
    if (zip.status !== 0) throw new Error(zip.stderr || "zip failed");

    const sourceArchive = join(outputDir, "Vencord-SoundCloner-Source.tar.gz");
    await rm(sourceArchive, { force: true });
    const epoch = spawnSync("git", ["-C", vencordDir, "show", "-s", "--format=%ct", "HEAD"], { encoding: "utf8" });
    if (epoch.status !== 0 || !/^\d+$/.test(epoch.stdout.trim())) throw new Error("cannot read source timestamp");
    const notes = join(stageRoot, "source-notes");
    await mkdir(notes);
    await writeFile(join(notes, "BUILD-SOUNDCLONER.md"), `# Rebuild this source archive\n\nVencord commit: ${config.vencordCommit}\nSoundCloner: ${pluginVersion}\nHelper: ${helperVersion}\n\nUse Node.js 24 and pnpm 11.9.0. From the extracted archive directory:\n\n\`\`\`sh\npnpm install --frozen-lockfile\nVENCORD_HASH=${config.vencordCommit.slice(0, 7)} VENCORD_REMOTE=Vendicated/Vencord SOURCE_DATE_EPOCH=${epoch.stdout.trim()}000 pnpm build --standalone --disable-updater\n\`\`\`\n\nThe environment variables replace Git metadata, since the source archive intentionally excludes .git. SOURCE_DATE_EPOCH is milliseconds as used by this pinned upstream build script.\n`);
    const tar = spawnSync("tar", [
        "--exclude=.git", "--exclude=node_modules", "--exclude=dist",
        "-czf", sourceArchive, "-C", vencordDir, ".", "-C", notes, "./BUILD-SOUNDCLONER.md"
    ], { encoding: "utf8" });
    if (tar.status !== 0) throw new Error(tar.stderr || "source archive failed");

    await cp(join(vencordDir, "LICENSE"), join(outputDir, "Vencord-SoundCloner-LICENSE.txt"));
    console.log(`Created ${archive}`);
} finally {
    await rm(stageRoot, { recursive: true, force: true });
}
