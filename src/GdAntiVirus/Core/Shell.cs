using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GdAntiVirus.Core
{
    public sealed class ProcResult
    {
        public int ExitCode;
        public string Output;
        public bool Ok => ExitCode == 0;
    }

    public static class Shell
    {
        /// <summary>Runs a console program hidden and captures its output (OEM code page, as cmd tools use it).</summary>
        public static Task<ProcResult> RunAsync(string file, string args, CancellationToken ct = default, Action<string> onLine = null, Encoding encoding = null)
        {
            var tcs = new TaskCompletionSource<ProcResult>();
            var sb = new StringBuilder();
            Encoding enc = encoding;
            if (enc == null)
            {
                try { enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { enc = Encoding.UTF8; }
            }

            var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = enc,
                    StandardErrorEncoding = enc,
                },
                EnableRaisingEvents = true
            };
            DataReceivedEventHandler h = (s, e) =>
            {
                if (e.Data == null) return;
                lock (sb) sb.AppendLine(e.Data);
                onLine?.Invoke(e.Data);
            };
            p.OutputDataReceived += h;
            p.ErrorDataReceived += h;
            p.Exited += (s, e) =>
            {
                try { p.WaitForExit(); } catch { }
                string output;
                lock (sb) output = sb.ToString();
                int code = -1;
                try { code = p.ExitCode; } catch { }
                tcs.TrySetResult(new ProcResult { ExitCode = code, Output = output });
                p.Dispose();
            };

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new ProcResult { ExitCode = -1, Output = ex.Message });
                return tcs.Task;
            }

            if (ct.CanBeCanceled)
                ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } });

            Log.Info($"Запуск: {file} {args}");
            return tcs.Task;
        }

        public static Task<ProcResult> PowerShellAsync(string command, CancellationToken ct = default)
        {
            var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command));
            return RunAsync("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64, ct, null, Encoding.UTF8);
        }

        /// <summary>Opens a file / URL / .msc / ms-settings: link through the shell.</summary>
        public static bool Open(string target, string args = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { Arguments = args ?? "", UseShellExecute = true });
                Log.Info("Открыто: " + target + " " + args);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось открыть " + target, ex);
                return false;
            }
        }

        /// <summary>Runs a command in a visible console window that stays open.</summary>
        public static bool Console(string command) => Open("cmd.exe", "/k " + command);

        public static void ShowInExplorer(string path)
        {
            if (System.IO.File.Exists(path)) Open("explorer.exe", "/select,\"" + path + "\"");
            else if (System.IO.Directory.Exists(path)) Open("explorer.exe", "\"" + path + "\"");
            else
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (dir != null && System.IO.Directory.Exists(dir)) Open("explorer.exe", "\"" + dir + "\"");
            }
        }

        public static async Task RestartExplorerAsync()
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            await Task.Delay(800);
            if (Process.GetProcessesByName("explorer").Length == 0) Open("explorer.exe");
            Log.Info("Проводник перезапущен");
        }
    }
}
