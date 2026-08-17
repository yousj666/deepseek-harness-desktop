using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// ============================================================
// DSH 桌面壳：双击 exe → 自动启动 DSH 服务 → WebView2 独立窗口聊天
// 顶部工具栏：扩展商店（浏览/安装 npm dsh-plugin 扩展）、返回聊天、刷新
// 数据目录 = 本机 ~/.dsh（与网页版共用），不依赖任何第三方桌面板
// ============================================================
namespace DSHDesktop
{
    public class MainForm : Form
    {
        private WebView2 webView;
        private Process dshProcess;
        private bool ownProcess = false;
        private System.Windows.Forms.Timer bootTimer;
        private Label statusLabel;
        private bool webViewReady = false;
        private ToolStrip toolStrip;
        private ToolStripButton btnStore;
        private ToolStripButton btnChat;
        private ToolStripButton btnReload;
        private StoreServer storeServer;
        private const string BaseUrl = "http://127.0.0.1:3080";

        public MainForm()
        {
            Text = "DeepSeek Harness Desktop";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 600);

            // 顶部工具栏
            toolStrip = new ToolStrip();
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip.Padding = new Padding(8, 3, 8, 3);
            toolStrip.Font = new Font("Microsoft YaHei UI", 9f);

            btnChat = new ToolStripButton("聊天");
            btnStore = new ToolStripButton("扩展商店");
            btnReload = new ToolStripButton("刷新");
            toolStrip.Items.Add(btnChat);
            toolStrip.Items.Add(btnStore);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnReload);
            Controls.Add(toolStrip);

            btnStore.Click += delegate { GoStore(); };
            btnChat.Click += delegate { GoChat(); };
            btnReload.Click += delegate { if (webViewReady) webView.Reload(); };
            btnChat.Enabled = false;

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            statusLabel.Font = new Font("Microsoft YaHei UI", 13f);
            statusLabel.ForeColor = Color.Gray;
            statusLabel.Text = "正在启动 DeepSeek Harness...";
            Controls.Add(statusLabel);

            FormClosing += OnFormClosing;
            Shown += async delegate { await Boot(); };
        }

        private bool ServerUp()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(BaseUrl);
                req.Timeout = 700;
                req.Method = "HEAD";
                HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
                resp.Close();
                return true;
            }
            catch { return false; }
        }

        private string FindDshRoot()
        {
            string env = Environment.GetEnvironmentVariable("DSH_DESKTOP_DSH_DIR");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "io.github.hairyf.deepseek-harness-desktop", "dependencies", "dsh");
            if (Directory.Exists(appData)) return appData;

            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dsh");
            if (Directory.Exists(local)) return local;

            return null;
        }

        public static string FindNode(string dshRoot)
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "node", "node.exe");
            if (File.Exists(local)) return local;

            string inDsh = Path.Combine(dshRoot, "node.exe");
            if (File.Exists(inDsh)) return inDsh;

            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = "where";
                    p.StartInfo.Arguments = "node";
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.CreateNoWindow = true;
                    p.Start();
                    string line = p.StandardOutput.ReadLine();
                    p.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(line))
                    {
                        string trimmed = line.Trim();
                        if (File.Exists(trimmed)) return trimmed;
                    }
                }
            }
            catch { }
            return null;
        }

        public static string DshHomeDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
        }

        private async Task Boot()
        {
            string dshRoot = FindDshRoot();
            if (dshRoot == null)
            {
                statusLabel.Text = "找不到 DSH 运行时。\n\n可设置环境变量 DSH_DESKTOP_DSH_DIR 指向 dsh 目录后重试。";
                return;
            }

            if (ServerUp())
            {
                ownProcess = false;
                await ShowUi(dshRoot);
                return;
            }

            string bin = Path.Combine(dshRoot, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            if (!File.Exists(bin))
            {
                statusLabel.Text = "找不到 dsh 入口：\n" + bin;
                return;
            }

            string nodeExe = FindNode(dshRoot);
            if (nodeExe == null)
            {
                statusLabel.Text = "找不到 node 运行时。";
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(nodeExe);
                psi.Arguments = "\"" + bin + "\" web --host 127.0.0.1 --port 3080";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = dshRoot;
                // 强制使用本机网页版的 ~/.dsh（不继承环境里可能的桌面版 DSH_HOME）
                psi.EnvironmentVariables["DSH_HOME"] = DshHomeDir();
                dshProcess = Process.Start(psi);
                ownProcess = true;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "启动 DSH 服务失败：\n" + ex.Message;
                return;
            }

            bootTimer = new System.Windows.Forms.Timer();
            bootTimer.Interval = 700;
            bootTimer.Tick += async delegate
            {
                if (ServerUp())
                {
                    bootTimer.Stop();
                    await ShowUi(dshRoot);
                }
                else if (ownProcess && dshProcess != null && dshProcess.HasExited)
                {
                    bootTimer.Stop();
                    statusLabel.Text = "DSH 服务启动失败（进程已退出）。";
                }
            };
            bootTimer.Start();
        }

        private async Task ShowUi(string dshRoot)
        {
            if (webViewReady) return;
            webViewReady = true;

            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            Controls.Add(webView);
            webView.BringToFront();

            try
            {
                // 禁用 Chromium 后台节流 + 允许 WebGL 软件渲染 + 强制不减少动画
                // （用户系统开了"减少动态效果"，aqua 流体/粒子动画会被静音成静态图）
                CoreWebView2EnvironmentOptions opts = new CoreWebView2EnvironmentOptions();
                opts.AdditionalBrowserArguments =
                    "--disable-background-timer-throttling " +
                    "--disable-renderer-backgrounding " +
                    "--disable-backgrounding-occluded-windows " +
                    "--ignore-gpu-blocklist --enable-webgl --enable-unsafe-swiftshader " +
                    "--force-prefers-no-reduced-motion " +
                    "--remote-debugging-port=9333";
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, null, opts);
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.Source = new Uri(BaseUrl);
                btnChat.Enabled = true;

                // 启动本地商店服务
                storeServer = new StoreServer(dshRoot);
                storeServer.Start();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "WebView2 初始化失败：\n" + ex.Message;
            }
        }

        private void GoStore()
        {
            if (!webViewReady || storeServer == null) return;
            webView.Source = new Uri("http://127.0.0.1:" + storeServer.Port + "/store");
        }

        private void GoChat()
        {
            if (!webViewReady) return;
            webView.Source = new Uri(BaseUrl);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (storeServer != null) storeServer.Stop();
            if (ownProcess && dshProcess != null && !dshProcess.HasExited)
            {
                try { dshProcess.Kill(); } catch { }
            }
        }
    }

    // 本地极简 HTTP 服务：托管商店页面 + 安装/已安装接口（免 urlacl 权限）
    public class StoreServer
    {
        private TcpListener listener;
        private Thread acceptThread;
        private volatile bool running = false;
        private string dshRoot;
        public int Port { get; private set; }

        public StoreServer(string dshRoot)
        {
            this.dshRoot = dshRoot;
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            listener = new TcpListener(IPAddress.Loopback, Port);
        }

        public void Start()
        {
            listener.Start();
            running = true;
            acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Start();
        }

        public void Stop()
        {
            running = false;
            try { listener.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Thread t = new Thread(delegate() { HandleClient(client); });
                    t.IsBackground = true;
                    t.Start();
                }
                catch { break; }
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    NetworkStream ns = client.GetStream();
                    byte[] buf = new byte[8192];
                    int n = ns.Read(buf, 0, buf.Length);
                    if (n <= 0) return;
                    string req = Encoding.UTF8.GetString(buf, 0, n);
                    string[] lines = req.Split(new char[] { '\r', '\n' });
                    string[] parts = lines[0].Split(' ');
                    if (parts.Length < 2) return;
                    string path = parts[1];
                    string body = "";
                    string contentType = "text/html; charset=utf-8";
                    int status = 200;

                    try
                    {
                        if (path == "/" || path.StartsWith("/store"))
                        {
                            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "store.html");
                            if (File.Exists(file))
                            {
                                body = File.ReadAllText(file, Encoding.UTF8);
                            }
                            else
                            {
                                body = "<h2>store.html 未找到</h2>";
                                status = 500;
                            }
                        }
                        else if (path.StartsWith("/install?"))
                        {
                            string name = ParseQuery(path, "name");
                            if (string.IsNullOrEmpty(name))
                            {
                                body = "{\"ok\":false,\"error\":\"missing name\"}";
                            }
                            else
                            {
                                string output = RunInstall(name);
                                body = "{\"ok\":true,\"output\":" + JsonEscape(output) + "}";
                            }
                            contentType = "application/json; charset=utf-8";
                        }
                        else if (path == "/installed")
                        {
                            body = "{\"ok\":true,\"packages\":" + ListInstalled() + "}";
                            contentType = "application/json; charset=utf-8";
                        }
                        else
                        {
                            body = "{\"ok\":false,\"error\":\"not found\"}";
                            contentType = "application/json; charset=utf-8";
                            status = 404;
                        }
                    }
                    catch (Exception ex)
                    {
                        body = "{\"ok\":false,\"error\":" + JsonEscape(ex.Message) + "}";
                        contentType = "application/json; charset=utf-8";
                        status = 500;
                    }

                    byte[] resp = Encoding.UTF8.GetBytes(body);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("HTTP/1.1 ").Append(status).Append(" OK\r\n");
                    sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
                    sb.Append("Access-Control-Allow-Origin: *\r\n");
                    sb.Append("Content-Length: ").Append(resp.Length).Append("\r\n");
                    sb.Append("Connection: close\r\n\r\n");
                    byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
                    ns.Write(head, 0, head.Length);
                    ns.Write(resp, 0, resp.Length);
                    ns.Flush();
                }
            }
            catch { }
        }

        private static string ParseQuery(string path, string key)
        {
            int q = path.IndexOf('?');
            if (q < 0) return null;
            string query = path.Substring(q + 1);
            string[] pairs = query.Split('&');
            foreach (string pair in pairs)
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && kv[0] == key)
                {
                    return Uri.UnescapeDataString(kv[1].Replace("+", " "));
                }
            }
            return null;
        }

        private string RunInstall(string name)
        {
            try
            {
                string bin = Path.Combine(dshRoot, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                string nodeExe = MainForm.FindNode(dshRoot);
                if (nodeExe == null) return "ERROR: node not found";
                if (!File.Exists(bin)) return "ERROR: dsh bin not found: " + bin;

                string profile = ProfileName();
                ProcessStartInfo psi = new ProcessStartInfo(nodeExe);
                psi.Arguments = "\"" + bin + "\" plugin --profile " + profile + " add " + name;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = dshRoot;
                // 强制使用本机网页版的 ~/.dsh（不继承环境里可能的桌面版 DSH_HOME）
                psi.EnvironmentVariables["DSH_HOME"] = MainForm.DshHomeDir();

                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(600000))
                    {
                        try { p.Kill(); } catch { }
                        return "安装超时（10 分钟）——pnpm 首次装包较慢，可稍后在商店重试";
                    }
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    return "exit=" + p.ExitCode + "\n" + stdout + "\n" + stderr;
                }
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        private string ProfileName()
        {
            try
            {
                string profilesDir = Path.Combine(MainForm.DshHomeDir(), "profiles");
                if (Directory.Exists(profilesDir))
                {
                    foreach (string d in Directory.GetDirectories(profilesDir))
                    {
                        string n = Path.GetFileName(d);
                        if (n != "node_modules") return n;
                    }
                }
            }
            catch { }
            return "web";
        }

        private string ListInstalled()
        {            List<string> names = new List<string>();
            string dshHome = MainForm.DshHomeDir();
            string profilesDir = Path.Combine(dshHome, "profiles");
            try
            {
                if (Directory.Exists(profilesDir))
                {
                    foreach (string profile in Directory.GetDirectories(profilesDir))
                    {
                        string nm = Path.Combine(profile, "node_modules");
                        if (!Directory.Exists(nm)) continue;
                        foreach (string d in Directory.GetDirectories(nm))
                        {
                            string n = Path.GetFileName(d);
                            if (!names.Contains(n)) names.Add(n);
                        }
                        string scoped = Path.Combine(nm, "@deepseek-ai");
                        if (Directory.Exists(scoped))
                        {
                            foreach (string d in Directory.GetDirectories(scoped))
                            {
                                string n = "@deepseek-ai/" + Path.GetFileName(d);
                                if (!names.Contains(n)) names.Add(n);
                            }
                        }
                    }
                }
            }
            catch { }
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(JsonEscape(names[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }

        public static string JsonEscape(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }
    }

    static class Program
    {
        // PerMonitorV2 高 DPI 感知：防止 Windows 位图缩放导致界面/文字模糊
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        static void Main()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
