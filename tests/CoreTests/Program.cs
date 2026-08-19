using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Serialization;
using TargetMarkerOverlay;

internal static class Program
{
    private static int count;
    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); count++; Console.WriteLine("PASS " + name); }

    private static void Main()
    {
        Assert(Localization.FromUiCulture(new CultureInfo("ja-JP")) == "ja", "OS Japanese mapping");
        Assert(Localization.FromUiCulture(new CultureInfo("zh-TW")) == "zh-CN", "OS Chinese mapping");
        Assert(Localization.FromUiCulture(new CultureInfo("ko-KR")) == "ko", "OS Korean mapping");
        Assert(Localization.FromUiCulture(new CultureInfo("fr-FR")) == "en", "OS fallback to English");
        Assert(Localization.Get("xx", "Support") == "Support", "translation fallback");
        Assert(Localization.Get("en", "Support") == "Support" && Localization.Get("ja", "Support") == "支援" && Localization.Get("zh-CN", "Support") == "支持" && Localization.Get("ko", "Support") == "후원", "four-language support tab names without symbols");
        var legacyXml = "<?xml version=\"1.0\"?><PluginSettings><OverlayEnabled>false</OverlayEnabled><OpacityPercent>75</OpacityPercent></PluginSettings>";
        PluginSettings legacy;
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacyXml))) legacy = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream);
        Assert(legacy.Language == null && legacy.CheckUpdatesOnStartup && !legacy.OverlayEnabled && legacy.OpacityPercent == 75, "legacy Config backward compatibility");

        SemVersion v1, v2, pre;
        Assert(SemVersion.TryParse("v1.2.3", out v1), "SemVer v-tag parsing");
        Assert(SemVersion.TryParse("1.3.0", out v2) && v2.CompareTo(v1) > 0, "SemVer comparison");
        Assert(SemVersion.TryParse("1.3.0-beta.1", out pre) && pre.CompareTo(v2) < 0, "SemVer prerelease ordering");

        var json = "[{\"tag_name\":\"v1.4.0\",\"draft\":false,\"prerelease\":false,\"name\":\"Stable\",\"body\":\"Notes\",\"html_url\":\"https://github.com/o/r/releases/tag/v1.4.0\",\"assets\":[{\"name\":\"TargetMarkerOverlay-v1.4.0.zip\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.4.0/a.zip\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.4.0/SHA256SUMS.txt\"}]},{\"tag_name\":\"v2.0.0-beta\",\"draft\":false,\"prerelease\":true}]";
        var release = ReleaseParser.ParseLatestStable(json);
        Assert(release != null && release.Version.ToString() == "1.4.0" && release.Package != null && release.HashManifest != null, "stable Release JSON parsing");
        Assert(GitHubUpdateService.EvaluateReleaseJson(json, "1.3.0").UpdateAvailable, "update available response");
        Assert(!GitHubUpdateService.EvaluateReleaseJson(json, "1.4.0").UpdateAvailable, "no update response");
        Assert(!string.IsNullOrWhiteSpace(GitHubUpdateService.EvaluateReleaseJson("{broken", "1.3.0").Error), "malformed Release response is contained");
        Assert(UpdateSecurity.IsAllowedGitHubUrl("https://github.com/o/r/releases/download/v1/a.zip") && !UpdateSecurity.IsAllowedGitHubUrl("http://github.com/a") && !UpdateSecurity.IsAllowedGitHubUrl("https://evil.example/a"), "asset URL allowlist");

        var root = Path.Combine(Path.GetTempPath(), "TMO-CoreTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var data = Path.Combine(root, "asset.zip"); File.WriteAllText(data, "test");
            var hash = UpdateSecurity.ComputeSha256(data);
            Assert(UpdateSecurity.FindExpectedHash(hash + "  asset.zip", "asset.zip") == hash, "SHA-256 manifest validation");
            Assert(!string.Equals(new string('0', 64), hash, StringComparison.OrdinalIgnoreCase), "corrupt asset hash mismatch");
            var safeZip = Path.Combine(root, "safe.zip");
            using (var z = ZipFile.Open(safeZip, ZipArchiveMode.Create)) { var e = z.CreateEntry("TargetMarkerOverlay.dll"); using (var w = new StreamWriter(e.Open())) w.Write("x"); }
            Assert(UpdateSecurity.ValidateZip(safeZip).Count == 1, "safe ZIP validation");
            var unsafeZip = Path.Combine(root, "unsafe.zip");
            using (var z = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) { var e = z.CreateEntry("../evil.dll"); using (var w = new StreamWriter(e.Open())) w.Write("x"); }
            var rejected = false; try { UpdateSecurity.ValidateZip(unsafeZip); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Zip Slip rejection");
            var script = GitHubUpdateService.BuildInstallerScript("C:\\stage\\TargetMarkerOverlay.dll", "C:\\plugins\\TargetMarkerOverlay.dll", 123);
            Assert(script.Contains(".bak") && script.Contains("catch") && script.Contains("Copy-Item -LiteralPath $backup"), "backup and rollback script");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine("ALL PASS: " + count);
    }
}
