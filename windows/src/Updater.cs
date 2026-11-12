// Updates from GitHub Releases: looks for a release asset with "win" in its name
// (Easy-Uninstall-windows.zip holding "Easy Uninstall.exe").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;

namespace EasyUninstall
{
    enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Failed }

    static class Updater
    {
        public static UpdateState State = UpdateState.Idle;
        public static string Latest = "", Notes = "", Error = "";
        static string zipUrl;

        /// "1.2.0" > "1.1.9"
        public static bool IsNewer(string a, string b)
        {
            Func<string, int[]> parse = s => s.Split('.').Select(p => { int n; return int.TryParse(p, out n) ? n : 0; }).ToArray();
            int[] x = parse(a), y = parse(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var c = new WebClient();
            c.Headers[HttpRequestHeader.UserAgent] = "EasyUninstall";
            c.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return c;
        }

        public static void Check(bool silent)
        {
            if (State == UpdateState.Checking || State == UpdateState.Downloading) return;
            State = UpdateState.Checking; App.Invalidate();
            Bg.Run(() =>
            {
                try
                {
                    string json;
                    using (var c = Client()) json = c.DownloadString("https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest");
                    var o = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    string tag = (o["tag_name"] as string ?? "").Trim('v', 'V');
                    string zip = null;
                    var assets = o.ContainsKey("assets") ? o["assets"] as System.Collections.ArrayList : null;
                    if (assets != null)
                        foreach (Dictionary<string, object> a in assets)
                            if ((a["name"] as string ?? "").ToLowerInvariant().Contains("win")) { zip = a["browser_download_url"] as string; break; }
                    return new[] { tag, o.ContainsKey("body") ? o["body"] as string ?? "" : "", zip };
                }
                catch { return null; }
            }, r =>
            {
                if (r == null)
                {
                    Error = L.T("Internet বা GitHub এ সমস্যা", "Couldn't reach GitHub");
                    State = silent ? UpdateState.Idle : UpdateState.Failed;
                }
                else
                {
                    Latest = r[0]; Notes = r[1]; zipUrl = r[2];
                    State = IsNewer(Latest, AppInfo.Version) && zipUrl != null ? UpdateState.Available : UpdateState.UpToDate;
                }
                App.Invalidate();
            });
        }

        /// Downloads the new version, swaps it in place of this app and relaunches.
        public static void Install()
        {
            if (zipUrl == null || State == UpdateState.Downloading) return;
            State = UpdateState.Downloading; App.Invalidate();
            Bg.Run(() =>
            {
                try
                {
                    string work = Path.Combine(Path.GetTempPath(), "EasyUninstallUpdate-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(work);
                    string zip = Path.Combine(work, "update.zip");
                    using (var c = Client()) c.DownloadFile(zipUrl, zip);
                    ZipFile.ExtractToDirectory(zip, Path.Combine(work, "new"));
                    string fresh = Directory.GetFiles(Path.Combine(work, "new"), "*.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (fresh == null) return "zip";
                    string dest = AppInfo.ExePath;
                    try { using (new FileStream(Path.Combine(Path.GetDirectoryName(dest), ".eu-write-test"), FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { } }
                    catch { return L.T("এই ফোল্ডারে লেখার অনুমতি নেই — GitHub থেকে নতুন version নামিয়ে নিন।", "No permission to write to this folder — download the new version from GitHub."); }

                    int pid = Process.GetCurrentProcess().Id;
                    string script = Path.Combine(work, "swap.cmd");
                    File.WriteAllText(script,
                        "@echo off\r\nchcp 65001 >nul\r\n:wait\r\ntasklist /FI \"PID eq " + pid + "\" 2>nul | find \" " + pid + " \" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)\r\n" +
                        "copy /y \"" + fresh + "\" \"" + dest + "\" >nul\r\nstart \"\" \"" + dest + "\"\r\n(goto) 2>nul & rd /s /q \"" + work + "\"\r\n",
                        new System.Text.UTF8Encoding(false));
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    return null;
                }
                catch (Exception e) { return e.Message; }
            }, err =>
            {
                if (err == null) { System.Windows.Application.Current.Shutdown(); return; }
                Error = L.T("Update হয়নি: ", "Update failed: ") + err; State = UpdateState.Failed; App.Invalidate();
            });
        }
    }
}
