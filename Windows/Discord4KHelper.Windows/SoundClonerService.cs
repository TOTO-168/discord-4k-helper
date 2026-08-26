using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Discord4KHelper.Windows;

internal sealed record PluginFile(string Path, string Sha256);
internal sealed record PluginManifest(int SchemaVersion, string HelperVersion, string PluginVersion,
    string VencordCommit, List<PluginFile> Files)
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal const string InvalidPackage = "音效外掛封裝驗證失敗，未變更現有安裝。請重新下載或更新 Helper。";
    internal static PluginManifest Read(string path)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException(InvalidPackage);
        manifest.Validate();
        return manifest;
    }
    internal void Validate()
    {
        if (SchemaVersion != 1 || !Regex.IsMatch(VencordCommit ?? "", "^[a-f0-9]{40}$") ||
            !Regex.IsMatch(HelperVersion ?? "", @"^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$") ||
            !Regex.IsMatch(PluginVersion ?? "", @"^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$") ||
            Files is null || Files.Count is < 1 or > 100 ||
            Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Files.Count ||
            Files.Any(f => !Regex.IsMatch(f.Path ?? "", "^dist/[A-Za-z0-9_-][A-Za-z0-9._-]*$") ||
                !Regex.IsMatch(f.Sha256 ?? "", "^[a-f0-9]{64}$")) ||
            new[] { "dist/patcher.js", "dist/preload.js", "dist/renderer.js", "dist/renderer.css", "dist/package.json" }
                .Except(Files.Select(f => f.Path)).Any()) throw new InvalidDataException(InvalidPackage);
    }
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    internal void Verify(string directory)
    {
        Validate();
        var paths = Directory.GetFileSystemEntries(System.IO.Path.Combine(directory, "dist"))
            .Select(p => "dist/" + System.IO.Path.GetFileName(p)).ToHashSet();
        if (!paths.SetEquals(Files.Select(f => f.Path))) throw new InvalidDataException(InvalidPackage);
        foreach (var file in Files)
        {
            var path = System.IO.Path.Combine(directory, file.Path);
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || Hash(path) != file.Sha256)
                throw new InvalidDataException(InvalidPackage);
        }
    }
    internal bool SameBuild(PluginManifest other) => SchemaVersion == other.SchemaVersion &&
        HelperVersion == other.HelperVersion && PluginVersion == other.PluginVersion && VencordCommit == other.VencordCommit &&
        Files.OrderBy(f => f.Path).SequenceEqual(other.Files.OrderBy(f => f.Path));
}

internal static class SoundClonerService
{
    internal const string ArchiveName = "Vencord-SoundCloner.zip";
    internal const string ManifestName = "Vencord-SoundCloner-manifest.json";
    internal const string MarkerName = ".soundcloner-manifest.json";
    internal static string Backup(string root) => Path.Combine(root, "discord-4k-helper-backup", "dist");
    internal static async Task<PluginManifest> GetManifestAsync(GitHubRelease release)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name == ManifestName)
            ?? throw new InvalidDataException("這個版本沒有音效外掛封裝，請更新 Helper。");
        var file = Path.Combine(Path.GetTempPath(), $"soundcloner-manifest-{Guid.NewGuid():N}.json");
        try
        {
            await Web.DownloadAsync(asset.DownloadUrl, file);
            var manifest = PluginManifest.Read(file);
            if (AppVersion.Parse(manifest.HelperVersion).CompareTo(release.Version) != 0)
                throw new InvalidDataException(PluginManifest.InvalidPackage);
            return manifest;
        }
        finally { File.Delete(file); }
    }

    internal static async Task<string> PrepareAsync(GitHubRelease release, string helperVersion)
    {
        var expected = await GetManifestAsync(release);
        if (AppVersion.Parse(helperVersion) < AppVersion.Parse(expected.HelperVersion))
            throw new InvalidOperationException("請先更新 Helper，再安裝音效外掛。");
        var asset = release.Assets.FirstOrDefault(a => a.Name == ArchiveName)
            ?? throw new InvalidDataException(PluginManifest.InvalidPackage);
        var stage = Path.Combine(Path.GetTempPath(), $"soundcloner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            var archive = Path.Combine(stage, "download.zip");
            await Web.DownloadAsync(asset.DownloadUrl, archive);
            ExtractVerified(archive, stage);
            File.Delete(archive);
            var package = Path.Combine(stage, "Vencord-SoundCloner");
            var manifest = PluginManifest.Read(Path.Combine(package, "manifest.json"));
            if (!manifest.SameBuild(expected)) throw new InvalidDataException(PluginManifest.InvalidPackage);
            manifest.Verify(package);
            return stage;
        }
        catch { Directory.Delete(stage, true); throw; }
    }

    internal static void ExtractVerified(string archive, string stage)
    {
        if (new FileInfo(archive).Length > 128 * 1024 * 1024) throw new InvalidDataException(PluginManifest.InvalidPackage);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is < 1 or > 110) throw new InvalidDataException(PluginManifest.InvalidPackage);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            var kind = (entry.ExternalAttributes >> 16) & 0xf000;
            var allowed = new[] { "Vencord-SoundCloner/", "Vencord-SoundCloner/dist/", "Vencord-SoundCloner/manifest.json", "Vencord-SoundCloner/Vencord-LICENSE.txt" };
            total += entry.Length;
            if ((!allowed.Contains(name) && !Regex.IsMatch(name, "^Vencord-SoundCloner/dist/[A-Za-z0-9_-][A-Za-z0-9._-]*$")) ||
                Regex.IsMatch(Path.GetFileName(name), @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase) || name.EndsWith('.') ||
                !seen.Add(name) || kind is not (0 or 0x4000 or 0x8000) ||
                (kind == 0x4000 && !name.EndsWith('/')) || (entry.ExternalAttributes & 0x400) != 0 || total > 128 * 1024 * 1024)
                throw new InvalidDataException(PluginManifest.InvalidPackage);
        }
        // Only exact allowlisted paths are used for extraction; never trust archive paths verbatim.
        foreach (var entry in zip.Entries)
        {
            var destination = Path.Combine(stage, entry.FullName);
            if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(destination);
            else { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); entry.ExtractToFile(destination); }
        }
    }

    internal static PluginManifest? Installed(string root)
    {
        try
        {
            var manifest = PluginManifest.Read(Path.Combine(root, "dist", MarkerName));
            return manifest.Files.All(f => PluginManifest.Hash(Path.Combine(root, f.Path)) == f.Sha256) ? manifest : null;
        }
        catch { return null; }
    }

    internal static bool NeedsCustomWarning(string root)
    {
        if (Installed(root) is not null || !File.Exists(Path.Combine(root, "dist", "patcher.js"))) return false;
        try
        {
            using var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dist", "renderer.js.map")));
            return map.RootElement.GetProperty("sources").EnumerateArray()
                .Any(s => s.GetString()!.Replace('\\', '/').Contains("/userplugins/"));
        }
        catch { return true; }
    }

    internal static void Install(string stage, string root, string settings, bool enableFeatures = true)
    {
        var package = Path.Combine(stage, "Vencord-SoundCloner");
        var marker = File.ReadAllText(Path.Combine(package, "manifest.json"));
        PluginManifest.Read(Path.Combine(package, "manifest.json")).Verify(package);
        var original = File.Exists(settings) ? File.ReadAllText(settings) : "{}";
        var updated = enableFeatures ? VencordSettings.Edit(original, enabled: true, soundCloner: true) : original;
        var backup = Backup(root);
        if (!Directory.Exists(backup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            var temp = Path.Combine(root, $"backup-{Guid.NewGuid():N}");
            try { CopyDirectory(Path.Combine(root, "dist"), temp); Directory.Move(temp, backup); }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
        Swap(Path.Combine(package, "dist"), root, settings, updated, marker);
    }

    internal static void Restore(string root, string settings)
    {
        var backup = Backup(root);
        if (!File.Exists(Path.Combine(backup, "patcher.js"))) throw new InvalidOperationException("找不到安裝前的 Vencord 備份，無法自動還原。");
        Swap(backup, root, settings, VencordSettings.Edit(File.Exists(settings) ? File.ReadAllText(settings) : "{}", removeSoundCloner: true), null);
    }

    internal static void Swap(string source, string root, string settings, string updated, string? marker)
    {
        var candidate = Path.Combine(root, $"dist-new-{Guid.NewGuid():N}");
        var previous = Path.Combine(root, $"dist-old-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "dist");
        try
        {
            CopyDirectory(source, candidate);
            if (marker is not null) File.WriteAllText(Path.Combine(candidate, MarkerName), marker);
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            Directory.Move(target, previous);
            try
            {
                Directory.Move(candidate, target);
                VencordSettings.WriteAtomic(settings, updated);
            }
            catch
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(previous, target);
                throw;
            }
            try { Directory.Delete(previous, true); } catch { /* A retained rollback copy is safe. */ }
        }
        finally { if (Directory.Exists(candidate)) Directory.Delete(candidate, true); }
    }

    private static void CopyDirectory(string source, string target)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Vencord 資料夾含符號連結，請先手動備份再安裝。");
        Directory.CreateDirectory(target);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Vencord 資料夾含符號連結，請先手動備份再安裝。");
            var destination = Path.Combine(target, Path.GetFileName(path));
            if (Directory.Exists(path)) CopyDirectory(path, destination);
            else File.Copy(path, destination);
        }
    }
}
