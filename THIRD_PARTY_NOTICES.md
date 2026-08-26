# Third-party notices

Discord 4K Helper can download and run the official [Vencord Installer](https://github.com/Vencord/Installer).

The macOS command-line installer distributed in this project's GitHub Releases is built from Vencord Installer commit [`6f2ce070f5ea8c27eaa8177e2825dd39def23fa5`](https://github.com/Vencord/Installer/commit/6f2ce070f5ea8c27eaa8177e2825dd39def23fa5), plus the included macOS CLI compatibility stub in `third_party/vencord_darwin_cli_compat.go`. Vencord Installer is licensed under GNU GPL v3; each Release includes `VencordInstaller-LICENSE.txt`.

The Windows app downloads `VencordInstallerCli.exe` directly from Vencord's official latest Release.

## Managed Vencord and SoundCloner

The managed distribution is based on [Vencord](https://github.com/Vendicated/Vencord) commit `ef29bbeb6119cfb53d1273ed78147bcc97d91261`, with the original GPL-3.0-or-later SoundCloner plugin (copyright 2026 TOTO-168) added under `src/userplugins/SoundCloner`. It is built with `--standalone --disable-updater`; Vencord's upstream updater is intentionally disabled so Helper can retain the custom plugin.

Each release includes `Vencord-SoundCloner-LICENSE.txt` and `Vencord-SoundCloner-Source.tar.gz`, containing the complete corresponding Vencord source, SoundCloner, dependency lockfile and build scripts. Bundle-specific third-party notices are retained in `dist/*.LEGAL.txt`. The plugin license is also included at `VencordPlugin/SoundCloner/LICENSE`.

Integration references: [Discord Soundboard Guide](https://support.discord.com/hc/en-us/articles/12612888127767-Discord-Soundboard-Guide-Using-Adding-and-Managing-Sounds), [Discord API error codes](https://docs.discord.com/developers/topics/opcodes-and-status-codes), and the native sound context menu demonstrated by [ExitSounds](https://github.com/hauntii/vencord-ExitSounds) and [SoundboardHotkeys](https://github.com/GriffTanen/vc-soundboard-hotkeys). SoundCloner does not read or store account tokens; Discord's native signed-in upload module performs the request.
