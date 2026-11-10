using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using GdAntiVirus.Core;

namespace GdAntiVirus
{
    public partial class App : Application
    {
        /// <summary>Set by "--selftest &lt;resultFile&gt;": opens every page, writes OK/FAIL and exits (used by CI).</summary>
        public static string SelfTestResultFile { get; private set; }
        public static bool IsSelfTest => SelfTestResultFile != null;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var args = e.Args.ToList();
            int i = args.FindIndex(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                SelfTestResultFile = i + 1 < args.Count ? args[i + 1] : Path.Combine(Path.GetTempPath(), "gdav-selftest.txt");

            DispatcherUnhandledException += OnDispatcherError;
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Fail(ex.ExceptionObject as Exception);

            Log.Info("Запуск GdAntiVirus " + AppInfo.Version);
            Theme.ApplyAccent(AppSettings.Accent);

            var w = new MainWindow();
            MainWindow = w;
            w.Show();
        }

        private void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Необработанная ошибка", e.Exception);
            if (IsSelfTest) { Fail(e.Exception); return; }
            e.Handled = true;
            (MainWindow as MainWindow)?.Toast("Ошибка: " + e.Exception.Message, ToastKind.Error);
        }

        public static void Fail(Exception ex)
        {
            if (!IsSelfTest) return;
            try { File.WriteAllText(SelfTestResultFile, "FAIL\n" + ex); } catch { }
            Environment.Exit(1);
        }

        public static void Pass()
        {
            try { File.WriteAllText(SelfTestResultFile, "OK"); } catch { }
            Current.Shutdown(0);
        }
    }
}
