using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

// ============================================================
// DeepSeek Harness Desktop 安装器
// 标准安装向导：欢迎 → 选择安装路径 → 安装进度 → 完成（勾选启动/桌面快捷方式）
// 所有文件（含内置 node 运行时）以嵌入资源打包成单个 setup.exe
// ============================================================
namespace DSHSetup
{
    public class SetupForm : Form
    {
        // 资源名 → 目标相对路径（{app} 下）
        private static readonly Dictionary<string, string> Payload = new Dictionary<string, string>
        {
            { "payload.app.exe",      "DeepSeek.Harness.Desktop.exe" },
            { "payload.node.pak",     "node\\node.exe" },   // 压缩的 node 运行时
            { "payload.core.dll",     "Microsoft.Web.WebView2.Core.dll" },
            { "payload.winforms.dll", "Microsoft.Web.WebView2.WinForms.dll" },
            { "payload.loader.dll",   "WebView2Loader.dll" },
            { "payload.store.html",   "store.html" },
            { "payload.harness.ico",  "harness.ico" },
            { "payload.manifest",     "app.manifest" },
            { "payload.MainForm.cs",  "MainForm.cs" }
        };

        private Panel pageHost;
        private Button btnPrev, btnNext, btnCancel;
        private int step = 0; // 0 欢迎 1 路径 2 安装 3 完成
        private string installDir;
        private TextBox txtPath;
        private ProgressBar progress;
        private Label lblStatus;
        private CheckBox chkDesktop, chkLaunch;
        private bool installed = false;
        private readonly string defaultDir;

        public SetupForm()
        {
            Text = "安装 DeepSeek Harness Desktop";
            ClientSize = new Size(620, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "DeepSeek Harness Desktop");

            // 底部按钮
            btnCancel = new Button(); btnCancel.Text = "取消"; btnCancel.Size = new Size(90, 30);
            btnCancel.Location = new Point(ClientSize.Width - 100, ClientSize.Height - 48);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Click += delegate { Close(); };

            btnNext = new Button(); btnNext.Text = "下一步"; btnNext.Size = new Size(90, 30);
            btnNext.Location = new Point(ClientSize.Width - 200, ClientSize.Height - 48);
            btnNext.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnNext.Click += delegate { NextStep(); };

            btnPrev = new Button(); btnPrev.Text = "上一步"; btnPrev.Size = new Size(90, 30);
            btnPrev.Location = new Point(ClientSize.Width - 300, ClientSize.Height - 48);
            btnPrev.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnPrev.Enabled = false;
            btnPrev.Click += delegate { GoStep(step - 1); };

            Controls.Add(btnCancel); Controls.Add(btnNext); Controls.Add(btnPrev);

            pageHost = new Panel();
            pageHost.Location = new Point(10, 10);
            pageHost.Size = new Size(ClientSize.Width - 20, ClientSize.Height - 75);
            pageHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(pageHost);

            GoStep(0);
        }

        private Control BuildPage(int s)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;

            if (s == 0) // 欢迎
            {
                Label title = new Label();
                title.Text = "DeepSeek Harness Desktop";
                title.Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
                title.AutoSize = true; title.Location = new Point(30, 60);

                Label icon = new Label();
                icon.Text = "🧊"; icon.Font = new Font("Segoe UI Emoji", 36f);
                icon.AutoSize = true; icon.Location = new Point(520, 40);

                Label desc = new Label();
                desc.Text = "基于 DeepSeek Harness 的桌面客户端。\r\n\r\n" +
                            "• 独立窗口聊天，界面与网页版完全一致\r\n" +
                            "• 内置 Node 运行时与扩展商店\r\n" +
                            "• 数据与本机网页版共用（~/.dsh）\r\n\r\n" +
                            "点击“下一步”继续安装。";
                desc.Font = new Font("Microsoft YaHei UI", 10f);
                desc.AutoSize = true; desc.Location = new Point(30, 110);

                p.Controls.Add(title); p.Controls.Add(icon); p.Controls.Add(desc);
            }
            else if (s == 1) // 安装路径
            {
                Label tip = new Label();
                tip.Text = "选择安装位置";
                tip.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
                tip.AutoSize = true; tip.Location = new Point(30, 40);
                p.Controls.Add(tip);

                Label lblPath = new Label();
                lblPath.Text = "将安装到：";
                lblPath.AutoSize = true; lblPath.Location = new Point(30, 90);
                p.Controls.Add(lblPath);

                txtPath = new TextBox();
                txtPath.Text = defaultDir;
                txtPath.Location = new Point(30, 115);
                txtPath.Size = new Size(430, 25);
                p.Controls.Add(txtPath);

                Button browse = new Button();
                browse.Text = "浏览...";
                browse.Size = new Size(90, 27);
                browse.Location = new Point(470, 114);
                browse.Click += delegate
                {
                    FolderBrowserDialog dlg = new FolderBrowserDialog();
                    dlg.Description = "选择安装文件夹";
                    dlg.SelectedPath = txtPath.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtPath.Text = dlg.SelectedPath;
                };
                p.Controls.Add(browse);

                Label note = new Label();
                note.Text = "提示：默认安装在当前用户目录（无需管理员权限）。\r\n如需安装到“Program Files”，请以管理员身份运行本安装程序。";
                note.ForeColor = Color.Gray; note.Font = new Font("Microsoft YaHei UI", 8.5f);
                note.AutoSize = true; note.Location = new Point(30, 160);
                p.Controls.Add(note);
            }
            else if (s == 2) // 安装进度
            {
                Label tip = new Label();
                tip.Text = "正在安装...";
                tip.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
                tip.AutoSize = true; tip.Location = new Point(30, 40);
                p.Controls.Add(tip);

                lblStatus = new Label();
                lblStatus.Text = "准备中...";
                lblStatus.AutoSize = true; lblStatus.Location = new Point(30, 90);
                p.Controls.Add(lblStatus);

                progress = new ProgressBar();
                progress.Location = new Point(30, 120);
                progress.Size = new Size(530, 22);
                progress.Minimum = 0; progress.Maximum = Payload.Count + 2;
                p.Controls.Add(progress);
            }
            else if (s == 3) // 完成
            {
                Label done = new Label();
                done.Text = "✓ 安装完成！";
                done.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
                done.AutoSize = true; done.Location = new Point(30, 50);
                p.Controls.Add(done);

                chkDesktop = new CheckBox();
                chkDesktop.Text = "创建桌面快捷方式";
                chkDesktop.Checked = true;
                chkDesktop.AutoSize = true; chkDesktop.Location = new Point(30, 110);
                p.Controls.Add(chkDesktop);

                chkLaunch = new CheckBox();
                chkLaunch.Text = "启动 DeepSeek Harness Desktop";
                chkLaunch.Checked = true;
                chkLaunch.AutoSize = true; chkLaunch.Location = new Point(30, 145);
                p.Controls.Add(chkLaunch);
            }
            return p;
        }

        private void GoStep(int s)
        {
            step = s;
            pageHost.Controls.Clear();
            pageHost.Controls.Add(BuildPage(s));
            btnPrev.Enabled = s > 0 && s < 3;
            btnNext.Enabled = s != 2;
            btnCancel.Enabled = s != 2;
            if (s == 0) btnNext.Text = "下一步";
            else if (s == 1) btnNext.Text = "安装";
            else if (s == 2) btnNext.Text = "下一步";
            else { btnNext.Text = "完成"; btnPrev.Enabled = false; }
        }

        private void NextStep()
        {
            if (step == 1)
            {
                installDir = txtPath.Text.Trim();
                if (string.IsNullOrEmpty(installDir))
                {
                    MessageBox.Show("请选择安装文件夹。", "提示");
                    return;
                }
                GoStep(2);
                BeginInstall();
            }
            else if (step == 2)
            {
                // 安装结束后不应到这里（按钮已禁用），兜底
            }
            else if (step == 3)
            {
                if (chkLaunch.Checked && installed)
                {
                    try
                    {
                        Process.Start(Path.Combine(installDir, "DeepSeek.Harness.Desktop.exe"));
                    }
                    catch { }
                }
                DialogResult = DialogResult.OK;
                Close();
            }
            else GoStep(1);
        }

        private void BeginInstall()
        {
            Application.DoEvents();
            try
            {
                Directory.CreateDirectory(installDir);
                if (lblStatus != null) lblStatus.Text = "正在解压文件...";
                progress.Value = 0;

                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();

                int i = 0;
                foreach (var kv in Payload)
                {
                    string resName = null;
                    foreach (string n in names)
                    {
                        if (n.EndsWith(kv.Key))
                        {
                            resName = n; break;
                        }
                    }
                    if (resName == null) continue;

                    string dest = Path.Combine(installDir, kv.Value);
                    string destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    if (lblStatus != null)
                        lblStatus.Text = "正在复制: " + kv.Value + " ...";

                    using (Stream s = asm.GetManifestResourceStream(resName))
                    using (FileStream fs = new FileStream(dest + ".tmp", FileMode.Create, FileAccess.Write))
                    {
                        if (kv.Value.EndsWith(".exe") && kv.Key == "payload.node.pak")
                        {
                            using (DeflateStream ds = new DeflateStream(s, CompressionMode.Decompress))
                                ds.CopyTo(fs);
                        }
                        else
                        {
                            s.CopyTo(fs);
                        }
                    }
                    File.Move(dest + ".tmp", dest);
                    i++;
                    progress.Value = Math.Min(progress.Maximum, i);
                    Application.DoEvents();
                }

                // 创建桌面快捷方式
                if (chkDesktop != null && chkDesktop.Checked) CreateShortcut();

                installed = true;
                progress.Value = progress.Maximum;
                GoStep(3);
            }
            catch (Exception ex)
            {
                MessageBox.Show("安装失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                GoStep(1);
            }
        }

        private void CreateShortcut()
        {
            string exe = Path.Combine(installDir, "DeepSeek.Harness.Desktop.exe");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, "DeepSeek Harness Desktop.lnk");
            try
            {
                Type shell = Type.GetTypeFromProgID("WScript.Shell");
                if (shell != null)
                {
                    dynamic wsh = Activator.CreateInstance(shell);
                    dynamic sc = wsh.CreateShortcut(lnk);
                    sc.TargetPath = exe;
                    sc.WorkingDirectory = installDir;
                    sc.Description = "DeepSeek Harness Desktop";
                    sc.Save();
                }
            }
            catch { }
        }
    }

    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        static void Main()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
