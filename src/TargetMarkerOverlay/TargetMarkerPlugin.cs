using Advanced_Combat_Tracker;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace TargetMarkerOverlay
{
    public sealed class TargetMarkerPlugin : IActPluginV1
    {
        private readonly MarkerStateTracker tracker = new MarkerStateTracker();
        private PluginSettings settings;
        private SettingsControl control;
        private OverlayForm overlay;
        private Label status;
        private string settingsPath;
        private GitHubUpdateService updateService;
        private CancellationTokenSource updateCancellation;
        private ReleaseInfo availableRelease;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            status = pluginStatusText;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Advanced Combat Tracker", "Config", "TargetMarkerOverlay.xml");
            settings = LoadSettings();
            var initializedLanguage = string.IsNullOrWhiteSpace(settings.Language);
            if (initializedLanguage) settings.Language = Localization.FromUiCulture(CultureInfo.CurrentUICulture);
            settings.Language = Localization.Normalize(settings.Language);
            tracker.Language = settings.Language;
            control = new SettingsControl(settings, tracker);
            control.SettingsChanged += OnSettingsChanged;
            control.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
            control.InstallUpdateRequested += OnInstallUpdateRequested;
            control.PostponeUpdateRequested += OnPostponeUpdateRequested;
            pluginScreenSpace.Text = "Target Marker Overlay";
            pluginScreenSpace.Controls.Add(control);

            overlay = new OverlayForm(settings, tracker);
            overlay.BoundsChangedByUser += (s, e) => SaveSettings();
            ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;
            overlay.Show();
            overlay.ApplySettings(settings, true);
            status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "StatusReady");
            updateService = new GitHubUpdateService();
            updateCancellation = new CancellationTokenSource();
            if (initializedLanguage) SaveSettings();
            if (settings.CheckUpdatesOnStartup) CheckForUpdatesAsync(false);
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (logInfo == null) return;
            // ACT/FFXIVプラグインの版により、片方がnetwork形式、片方がparsed形式になる。
            tracker.ProcessLine(logInfo.originalLogLine);
            if (!string.Equals(logInfo.logLine, logInfo.originalLogLine, StringComparison.Ordinal))
                tracker.ProcessLine(logInfo.logLine);

            if (isImport || settings == null || !settings.EchoToggleEnabled ||
                string.IsNullOrWhiteSpace(settings.EchoToggleText)) return;
            string message;
            var matched = EchoCommandParser.TryGetMessage(logInfo.logLine, out message) ||
                          EchoCommandParser.TryGetMessage(logInfo.originalLogLine, out message);
            if (!matched || !string.Equals(message.Trim(), settings.EchoToggleText.Trim(), StringComparison.OrdinalIgnoreCase))
                return;
            if (control != null && control.InvokeRequired)
            {
                control.BeginInvoke(new Action(ToggleOverlayFromEcho));
                return;
            }
            ToggleOverlayFromEcho();
        }

        private void ToggleOverlayFromEcho()
        {
            if (settings == null) return;
            settings.OverlayEnabled = !settings.OverlayEnabled;
            control?.SetOverlayEnabled(settings.OverlayEnabled);
            overlay?.ApplySettings(settings);
            SaveSettings();
            if (status != null)
                status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "StatusEcho", settings.OverlayEnabled ? "ON" : "OFF");
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            settings.Normalize();
            tracker.Language = settings.Language;
            overlay?.ApplySettings(settings);
            SaveSettings();
        }

        private void OnCheckForUpdatesRequested(object sender, EventArgs e) => CheckForUpdatesAsync(true);

        private async void CheckForUpdatesAsync(bool userInitiated)
        {
            if (control == null || updateService == null) return;
            Ui(() => control.ShowUpdateChecking());
            var result = await updateService.CheckAsync(updateCancellation.Token);
            availableRelease = result.Release;
            settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            SaveSettings();
            Ui(() =>
            {
                control.ShowUpdateResult(result);
                if (result.UpdateAvailable && result.Release != null && !string.Equals(settings.SkippedUpdateVersion, result.Release.Version.ToString(), StringComparison.OrdinalIgnoreCase))
                    status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "UpdateAvailable", GitHubUpdateService.CurrentVersion, result.Release.Version);
                else if (userInitiated && !result.IsConfigured)
                    status.Text = "Target Marker Overlay: " + Localization.Get(settings.Language, "UpdateRepoMissing");
            });
        }

        private async void OnInstallUpdateRequested(object sender, EventArgs e)
        {
            if (availableRelease == null || updateService == null) return;
            Ui(() => control.ShowUpdateInstallState("UpdateDownloading"));
            try
            {
                var staged = await updateService.DownloadAndPrepareAsync(availableRelease, updateCancellation.Token);
                GitHubUpdateService.LaunchDeferredInstaller(staged);
                Ui(() => control.ShowUpdateInstallState("UpdatePrepared"));
            }
            catch (Exception ex)
            {
                Ui(() => control.ShowUpdateInstallState("UpdateCorrupt", ex.Message, true));
            }
        }

        private void OnPostponeUpdateRequested(object sender, EventArgs e)
        {
            if (availableRelease == null) return;
            settings.SkippedUpdateVersion = availableRelease.Version.ToString();
            SaveSettings();
            control.ShowUpdateInstallState("UpdateSkipped");
        }

        private void Ui(Action action)
        {
            if (control == null || control.IsDisposed) return;
            if (control.InvokeRequired) control.BeginInvoke(action); else action();
        }

        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.OnLogLineRead -= OnLogLineRead;
            updateCancellation?.Cancel();
            SaveSettings();
            if (control != null)
            {
                control.SettingsChanged -= OnSettingsChanged;
                control.CheckForUpdatesRequested -= OnCheckForUpdatesRequested;
                control.InstallUpdateRequested -= OnInstallUpdateRequested;
                control.PostponeUpdateRequested -= OnPostponeUpdateRequested;
            }
            overlay?.Close();
            overlay?.Dispose();
            control?.Dispose();
            updateService?.Dispose();
            updateCancellation?.Dispose();
            if (status != null) status.Text = "Target Marker Overlay: " + Localization.Get(settings?.Language, "StatusStopped");
        }

        private PluginSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return new PluginSettings();
                using (var stream = File.OpenRead(settingsPath))
                {
                    var value = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream);
                    value.Normalize();
                    return value;
                }
            }
            catch
            {
                return new PluginSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                var temp = settingsPath + ".tmp";
                using (var stream = File.Create(temp))
                    new XmlSerializer(typeof(PluginSettings)).Serialize(stream, settings);
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
                File.Move(temp, settingsPath);
            }
            catch (Exception ex)
            {
                if (status != null) status.Text = "Target Marker Overlay: " + Localization.Get(settings?.Language, "SaveError", ex.Message);
            }
        }
    }
}
