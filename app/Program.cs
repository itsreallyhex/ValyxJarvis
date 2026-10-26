// Valyx Jarvis helper: no window, no tray. The game plugin starts it with --game-pid and it quits when the game does.
// Everything the player sets is set in the game's own desktop menu and arrives through the link files.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ValyxJarvis
{
    static class Program
    {
        static void LogError(Exception e)
        {
            try
            {
                var dir = Settings.Dir; Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now.ToString("s") + "  " + e + "\r\n\r\n");
            }
            catch { }
        }

        static readonly object logGate = new object();

        /// <summary>A local log of what Jarvis heard and did (with confidences), for tuning; it never leaves this PC. Capped at 256 KB.</summary>
        static void WriteLog(string line)
        {
            try
            {
                lock (logGate)
                {
                    var path = Path.Combine(Settings.Dir, "helper.log");
                    Directory.CreateDirectory(Settings.Dir);
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 256 * 1024) { var old = path + ".1"; if (File.Exists(old)) File.Delete(old); File.Move(path, old); }
                    File.AppendAllText(path, (line.Length > 8 && line[2] == ':' ? "" : DateTime.Now.ToString("HH:mm:ss") + "  ") + line + "\r\n");
                }
            }
            catch { }
        }

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);
            string linkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValyxJarvis", "link");
            int gamePid = 0, exitAfter = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--game-pid") int.TryParse(args[i + 1], out gamePid);
                if (args[i] == "--link-dir") linkDir = args[i + 1];
                if (args[i] == "--exit-after") int.TryParse(args[i + 1], out exitAfter);   // for tests: close by itself
            }
            bool createdNew;
            using (new Mutex(true, "Local\\ValyxJarvisHelper", out createdNew))
            {
                if (!createdNew) return 0;   // one helper per Windows user is enough
                Assistant a;
                try { a = new Assistant(linkDir); a.Log += WriteLog; a.Start(); WriteLog("helper started, game " + gamePid); }
                catch (Exception e) { LogError(e); return 1; }
                var started = DateTime.UtcNow; int missed = 0;
                while (true)
                {
                    Thread.Sleep(1000);
                    if (exitAfter > 0 && (DateTime.UtcNow - started).TotalSeconds >= exitAfter) break;
                    if (a.QuitRequested) break;   // a new plugin version asked (it replaces this exe)
                    if (gamePid <= 0) continue;
                    bool alive;
                    try { alive = !Process.GetProcessById(gamePid).HasExited; } catch { alive = false; }
                    missed = alive ? 0 : missed + 1;
                    if (missed >= 5) break;   // the game closed (a few seconds of grace)
                }
                a.Dispose();
                return 0;
            }
        }
    }
}