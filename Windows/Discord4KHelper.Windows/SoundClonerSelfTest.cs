using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Discord4KHelper.Windows;

internal static class SoundClonerSelfTest
{
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch { return; }
        throw new Exception(message);
    }

    internal static void Run(string directory)
    {
        const string input = "{\"plugins\":{\"Other\":{\"enabled\":true},\"FakeNitro\":{\"custom\":\"keep\"}}}";
        var enabled = VencordSettings.Edit(input, enabled: true, soundCloner: true);
        var disabled = JsonNode.Parse(VencordSettings.Edit(enabled, enabled: false));
        Check(disabled?["plugins"]?["SoundCloner"]?["enabled"]?.GetValue<bool>() == true, "Disabling 4K changed SoundCloner");
        var noSound = JsonNode.Parse(VencordSettings.Edit(enabled, soundCloner: false));
        Check(noSound?["plugins"]?["FakeNitro"]?["enableStreamQualityBypass"]?.GetValue<bool>() == true, "Disabling sound changed 4K");
        var restored = JsonNode.Parse(VencordSettings.Edit(enabled, removeSoundCloner: true));
        Check(restored?["plugins"]?["SoundCloner"] is null && restored?["plugins"]?["Other"]?["enabled"]?.GetValue<bool>() == true &&
            restored?["plugins"]?["FakeNitro"]?["custom"]?.GetValue<string>() == "keep", "Restore did not preserve settings");

        var stage = Path.Combine(directory, "stage");
        var package = Path.Combine(stage, "Vencord-SoundCloner");
        var dist = Path.Combine(package, "dist");
        var root = Path.Combine(directory, "Vencord");
        var settings = Path.Combine(root, "settings", "settings.json");
        Directory.CreateDirectory(dist);
        Directory.CreateDirectory(Path.Combine(root, "dist"));
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(Path.Combine(root, "dist", "patcher.js"), "original");
        File.WriteAllText(settings, input);
        var files = new[] { "patcher.js", "renderer.js", "renderer.css", "preload.js", "package.json" }.Select(name =>
        {
            var path = Path.Combine(dist, name);
            File.WriteAllText(path, "test-" + name);
            return new PluginFile("dist/" + name, PluginManifest.Hash(path));
        }).ToList();
        var manifest = new PluginManifest(1, "2.1.0", "1.0.0", new string('a', 40), files);
        File.WriteAllText(Path.Combine(package, "manifest.json"), JsonSerializer.Serialize(manifest, PluginManifest.JsonOptions));
        manifest.Verify(package);
        SoundClonerService.Install(stage, root, settings);
        Check(SoundClonerService.Installed(root)?.SameBuild(manifest) == true, "Installed manifest not detected");
        SoundClonerService.Install(stage, root, settings);
        Check(File.ReadAllText(Path.Combine(SoundClonerService.Backup(root), "patcher.js")) == "original", "Backup overwritten");
        var disabledSettings = VencordSettings.Edit(File.ReadAllText(settings), enabled: false, soundCloner: false);
        File.WriteAllText(settings, disabledSettings);
        SoundClonerService.Install(stage, root, settings, enableFeatures: false);
        Check(File.ReadAllText(settings) == disabledSettings, "Automatic update re-enabled disabled features");
        SoundClonerService.Install(stage, root, settings);
        SoundClonerService.Restore(root, settings);
        Check(File.ReadAllText(Path.Combine(root, "dist", "patcher.js")) == "original", "Restore failed");
        Check(JsonNode.Parse(File.ReadAllText(settings))?["plugins"]?["FakeNitro"]?["enableStreamQualityBypass"]?.GetValue<bool>() == true, "Restore changed 4K");

        var badSettings = Path.Combine(root, "directory.json");
        Directory.CreateDirectory(badSettings);
        Reject(() => SoundClonerService.Swap(dist, root, badSettings, "{}", null), "Settings write should fail");
        Check(File.ReadAllText(Path.Combine(root, "dist", "patcher.js")) == "original", "Rollback failed");
        File.AppendAllText(Path.Combine(dist, "renderer.js"), "tamper");
        Reject(() => SoundClonerService.Install(stage, root, settings), "Tampered package accepted");
        Check(File.ReadAllText(Path.Combine(root, "dist", "patcher.js")) == "original", "Tamper changed installation");

        foreach (var name in new[] { "../escape", "Vencord-SoundCloner/dist/../../escape", "Vencord-SoundCloner/dist/CON", "Vencord-SoundCloner/dist/link.js" })
        {
            var archive = Path.Combine(directory, Guid.NewGuid() + ".zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(name);
                if (name.EndsWith("link.js")) entry.ExternalAttributes = (0xa000 << 16);
            }
            Reject(() => SoundClonerService.ExtractVerified(archive, Path.Combine(directory, Guid.NewGuid().ToString())), "Unsafe ZIP accepted: " + name);
        }

        var releasePackage = Environment.GetEnvironmentVariable("SOUNDCLONER_PACKAGE");
        if (!string.IsNullOrEmpty(releasePackage))
        {
            var extraction = Path.Combine(directory, "release");
            SoundClonerService.ExtractVerified(releasePackage, extraction);
            var released = Path.Combine(extraction, "Vencord-SoundCloner");
            PluginManifest.Read(Path.Combine(released, "manifest.json")).Verify(released);
        }
    }
}
