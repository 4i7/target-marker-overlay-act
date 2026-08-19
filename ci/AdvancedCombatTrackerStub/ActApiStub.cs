using System;
using System.Windows.Forms;

namespace Advanced_Combat_Tracker
{
    public interface IActPluginV1
    {
        void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText);
        void DeInitPlugin();
    }

    public sealed class LogLineEventArgs : EventArgs
    {
        public string originalLogLine;
        public string logLine;
    }

    public delegate void LogLineEventDelegate(bool isImport, LogLineEventArgs logInfo);

    public sealed class ActMainForm
    {
        public event LogLineEventDelegate OnLogLineRead;
        public void RaiseLogLine(bool isImport, LogLineEventArgs args) => OnLogLineRead?.Invoke(isImport, args);
    }

    public static class ActGlobals
    {
        public static ActMainForm oFormActMain = new ActMainForm();
    }
}
