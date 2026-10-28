using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace MusynxTool
{
    static class Json
    {
        public static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        public static object Parse(string s)
        {
            int i = 0;
            return Value(s, ref i);
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double d;
            double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            return d;
        }

        static string Str(string s, ref int i)
        {
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'u' && i + 4 <= s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                string k = Str(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[k] = Value(s, ref i);
            }
            return d;
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                l.Add(Value(s, ref i));
            }
            return l;
        }

        public static Dictionary<string, object> AsObj(object o) { return o as Dictionary<string, object>; }
        public static List<object> AsArr(object o) { return o as List<object>; }
        public static string S(object o) { return o as string; }
        public static int I(object o) { return o == null ? 0 : (int)Convert.ToDouble(o, CultureInfo.InvariantCulture); }
        public static bool B(object o) { return o is bool && (bool)o; }
    }

    sealed class Song
    {
        public string Id, Name, Bms, Hard, Scene;
        public int Keys, Level, Sync;
        public override string ToString() { return Name + " [" + Hard + "]"; }
    }

    static class Rpc
    {
        public const int Port = 38091;

        public static string Call(string request, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect("127.0.0.1", Port);
                    client.ReceiveTimeout = timeoutMs;
                    using (NetworkStream stream = client.GetStream())
                    {
                        var writer = new StreamWriter(stream, new UTF8Encoding(false));
                        writer.AutoFlush = true;
                        writer.WriteLine(request);
                        var reader = new StreamReader(stream, Encoding.UTF8);
                        string line = reader.ReadLine();
                        return line ?? "";
                    }
                }
            }
            catch (Exception e)
            {
                return "{\"ok\":false,\"error\":" + Json.Q(e.Message) + "}";
            }
        }
    }

    static class Installer
    {
        public static string Ensure(string gameDir)
        {
            if (!File.Exists(Path.Combine(gameDir, "MUSYNX.exe"))) return "不是有效的 MUSYNX 目录";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string payload = Path.Combine(baseDir, "payload");
            if (!Directory.Exists(payload)) return "缺少 payload 目录";

            File.Copy(Path.Combine(payload, "winhttp.dll"), Path.Combine(gameDir, "winhttp.dll"), true);
            File.Copy(Path.Combine(payload, "doorstop_config.ini"), Path.Combine(gameDir, "doorstop_config.ini"), true);
            File.Copy(Path.Combine(payload, ".doorstop_version"), Path.Combine(gameDir, ".doorstop_version"), true);

            string coreDst = Path.Combine(gameDir, "BepInEx", "core");
            Directory.CreateDirectory(coreDst);
            foreach (string f in Directory.GetFiles(Path.Combine(payload, "BepInEx", "core")))
                File.Copy(f, Path.Combine(coreDst, Path.GetFileName(f)), true);

            string pluginDst = Path.Combine(gameDir, "BepInEx", "plugins", "MusynxAutoplay");
            Directory.CreateDirectory(pluginDst);
            File.Copy(Path.Combine(baseDir, "BepInEx", "plugins", "MusynxAutoplay", "MusynxAutoplay.dll"),
                      Path.Combine(pluginDst, "MusynxAutoplay.dll"), true);

            string ggm = Path.Combine(gameDir, "MUSYNX_Data", "globalgamemanagers");
            byte[] bytes = File.ReadAllBytes(ggm);
            const int off = 4416;
            if (bytes.Length > off && bytes[off] == 1)
            {
                string backupDir = Path.Combine(gameDir, "BepInEx", "MusynxAutoplayBackup");
                Directory.CreateDirectory(backupDir);
                string backup = Path.Combine(backupDir, "globalgamemanagers.orig");
                if (!File.Exists(backup)) File.Copy(ggm, backup, true);
                bytes[off] = 0;
                File.WriteAllBytes(ggm, bytes);
                return "安装完成（已关闭启动对话框）";
            }
            return "安装完成";
        }
    }

    sealed class MainForm : Form
    {
        readonly TextBox txtGame = new TextBox();
        readonly Label lblConn = new Label();
        readonly ListView lv = new ListView();
        readonly ComboBox cbKeys = new ComboBox();
        readonly ComboBox cbDiff = new ComboBox();
        readonly TextBox txtSearch = new TextBox();
        readonly ProgressBar bar = new ProgressBar();
        readonly Label lblState = new Label();
        readonly TextBox txtLog = new TextBox();
        readonly Button btnPlay = new Button();
        readonly Timer timer = new Timer();
        readonly List<Song> songs = new List<Song>();
        string lastResult = "";
        bool autoLoaded;
        bool needRefresh;
        string lastScene = "";
        int sortCol = -1;
        bool sortAsc = true;

        public MainForm()
        {
            Text = "MUSYNX 自动打歌工具";
            Size = new Size(1080, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);

            var lblGame = new Label { Text = "游戏目录", Location = new Point(12, 16), AutoSize = true };
            txtGame.SetBounds(80, 12, 520, 26);
            txtGame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var btnBrowse = new Button { Text = "浏览…", Location = new Point(610, 11), Size = new Size(80, 28) };
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var btnInstall = new Button { Text = "安装/修复", Location = new Point(698, 11), Size = new Size(90, 28) };
            btnInstall.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var btnLaunch = new Button { Text = "启动游戏", Location = new Point(796, 11), Size = new Size(90, 28) };
            btnLaunch.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            lblConn.Text = "○ 未连接";
            lblConn.SetBounds(12, 46, 400, 20);
            lblConn.ForeColor = Color.Gray;

            var btnPrepare = new Button { Text = "进入选歌", Location = new Point(12, 72), Size = new Size(90, 30) };
            var btnRefresh = new Button { Text = "刷新列表", Location = new Point(108, 72), Size = new Size(90, 30) };
            btnPlay.Text = "开始打歌";
            btnPlay.SetBounds(204, 72, 100, 30);
            btnPlay.Enabled = false;
            btnPlay.Font = new Font(Font, FontStyle.Bold);

            txtSearch.SetBounds(312, 75, 170, 26);
            txtSearch.ForeColor = Color.Gray;
            txtSearch.Text = "搜索歌名";

            var lblKeys = new Label { Text = "键数", Location = new Point(492, 78), AutoSize = true };
            cbKeys.SetBounds(530, 74, 64, 26);
            cbKeys.DropDownStyle = ComboBoxStyle.DropDownList;
            cbKeys.Items.AddRange(new object[] { "全部", "4K", "6K" });
            cbKeys.SelectedIndex = 0;

            var lblDiff = new Label { Text = "难度", Location = new Point(602, 78), AutoSize = true };
            cbDiff.SetBounds(640, 74, 76, 26);
            cbDiff.DropDownStyle = ComboBoxStyle.DropDownList;
            cbDiff.Items.AddRange(new object[] { "全部", "简单", "困难", "IN" });
            cbDiff.SelectedIndex = 0;

            bar.SetBounds(726, 76, 180, 22);
            bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblState.SetBounds(912, 78, 150, 20);
            lblState.Text = "就绪";
            lblState.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            lv.SetBounds(12, 114, 1056, 424);
            lv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.MultiSelect = false;
            lv.HideSelection = false;
            lv.Columns.Add("歌曲", 340);
            lv.Columns.Add("难度", 90);
            lv.Columns.Add("键数", 80);
            lv.Columns.Add("等级", 80);
            lv.Columns.Add("同步率", 110);

            txtLog.SetBounds(12, 550, 1056, 92);
            txtLog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.White;

            Controls.AddRange(new Control[] { lblGame, txtGame, btnBrowse, btnInstall, btnLaunch, lblConn,
                btnPrepare, btnRefresh, btnPlay, lblKeys, cbKeys, lblDiff, cbDiff, txtSearch, bar, lblState, lv, txtLog });

            btnBrowse.Click += delegate { Browse(); };
            btnInstall.Click += delegate { Install(); };
            btnLaunch.Click += delegate { Launch(); };
            btnPrepare.Click += delegate { Prepare(); };
            btnRefresh.Click += delegate { RefreshSongs(); };
            btnPlay.Click += delegate { Play(); };
            cbKeys.SelectedIndexChanged += delegate { FillList(); };
            cbDiff.SelectedIndexChanged += delegate { FillList(); };
            txtSearch.TextChanged += delegate
            {
                if (txtSearch.Text != "搜索歌名") FillList();
            };
            txtSearch.Enter += delegate
            {
                if (txtSearch.Text == "搜索歌名") { txtSearch.Text = ""; txtSearch.ForeColor = Color.Black; }
            };
            txtSearch.Leave += delegate
            {
                if (txtSearch.Text.Trim().Length == 0) { txtSearch.Text = "搜索歌名"; txtSearch.ForeColor = Color.Gray; }
            };
            lv.ColumnClick += delegate(object sender, ColumnClickEventArgs e)
            {
                if (sortCol == e.Column) sortAsc = !sortAsc;
                else { sortCol = e.Column; sortAsc = true; }
                FillList();
            };
            lv.DoubleClick += delegate { if (btnPlay.Enabled) Play(); };
            Load += delegate { LoadSettings(); };
            FormClosing += delegate { SaveSettings(); };

            timer.Interval = 500;
            timer.Tick += delegate { Tick(); };
            timer.Start();
        }

        void Log(string s)
        {
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        string SettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
        }

        void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath()))
                {
                    string[] lines = File.ReadAllLines(SettingsPath());
                    foreach (string line in lines)
                        if (line.StartsWith("game=")) txtGame.Text = line.Substring(5);
                }
            }
            catch { }
            if (txtGame.Text.Length == 0)
                txtGame.Text = @"D:\Game\Steam\steamapps\common\MUSYNX";
        }

        void SaveSettings()
        {
            try { File.WriteAllText(SettingsPath(), "game=" + txtGame.Text); } catch { }
        }

        void Browse()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 MUSYNX 游戏目录（包含 MUSYNX.exe）";
                if (Directory.Exists(txtGame.Text)) dlg.SelectedPath = txtGame.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtGame.Text = dlg.SelectedPath;
            }
        }

        void Install()
        {
            try
            {
                string msg = Installer.Ensure(txtGame.Text.Trim());
                Log(msg);
                MessageBox.Show(this, msg, "安装", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                Log("安装失败：" + e.Message);
                MessageBox.Show(this, e.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void Launch()
        {
            string exe = Path.Combine(txtGame.Text.Trim(), "MUSYNX.exe");
            if (!File.Exists(exe)) { Log("找不到 MUSYNX.exe"); return; }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = txtGame.Text.Trim() });
                Log("已启动游戏，等待进入选歌…");
            }
            catch (Exception e) { Log("启动失败：" + e.Message); }
        }

        bool Connected()
        {
            string resp = Rpc.Call("{\"cmd\":\"ping\"}", 600);
            return resp.IndexOf("\"pong\"", StringComparison.Ordinal) >= 0;
        }

        void Prepare()
        {
            string resp = Rpc.Call("{\"cmd\":\"prepare\"}", 3000);
            var o = Json.AsObj(Json.Parse(resp));
            if (o != null && Json.B(o.ContainsKey("ok") ? o["ok"] : null))
            {
                Log("已请求进入选歌");
                for (int i = 0; i < 20; i++)
                {
                    System.Threading.Thread.Sleep(400);
                    Application.DoEvents();
                    if (SceneIsSelect()) break;
                }
                RefreshSongs();
            }
            else Log("进入选歌失败：" + ErrorOf(o));
        }

        string ErrorOf(Dictionary<string, object> o)
        {
            if (o == null) return "无响应";
            return o.ContainsKey("error") ? Json.S(o["error"]) : "未知错误";
        }

        bool SceneIsSelect()
        {
            string resp = Rpc.Call("{\"cmd\":\"state\"}", 800);
            var o = Json.AsObj(Json.Parse(resp));
            if (o == null || !o.ContainsKey("data")) return false;
            var d = Json.AsObj(o["data"]);
            string scene = d != null && d.ContainsKey("scene") ? Json.S(d["scene"]) : "";
            return scene != null && scene.IndexOf("SelectSong", StringComparison.Ordinal) >= 0;
        }

        void RefreshSongs()
        {
            string resp = Rpc.Call("{\"cmd\":\"songs\"}", 6000);
            var o = Json.AsObj(Json.Parse(resp));
            if (o == null || !Json.B(o.ContainsKey("ok") ? o["ok"] : null))
            {
                Log("刷新失败：" + ErrorOf(o));
                return;
            }
            songs.Clear();
            var arr = Json.AsArr(o["data"]);
            if (arr != null)
            {
                foreach (object it in arr)
                {
                    var d = Json.AsObj(it);
                    if (d == null) continue;
                    var s = new Song();
                    s.Id = Json.S(d["id"]);
                    s.Name = Json.S(d["name"]);
                    s.Bms = Json.S(d["bms"]);
                    s.Hard = Json.S(d["hard"]);
                    s.Scene = Json.S(d["scene"]);
                    s.Keys = Json.I(d["keys"]);
                    s.Level = Json.I(d["level"]);
                    s.Sync = Json.I(d["sync"]);
                    songs.Add(s);
                }
            }
            FillList();
            Log("已加载 " + songs.Count + " 张谱面");
        }

        static int HardRank(string hard)
        {
            if (hard == "easy") return 0;
            if (hard == "hard") return 1;
            if (hard == "in") return 2;
            return 3;
        }

        void FillList()
        {
            int keyFilter = cbKeys.SelectedIndex == 1 ? 4 : (cbKeys.SelectedIndex == 2 ? 6 : 0);
            int diffFilter = cbDiff.SelectedIndex;
            string q = txtSearch.Text == "搜索歌名" ? "" : txtSearch.Text.Trim();

            var view = new List<Song>();
            foreach (Song s in songs)
            {
                if (keyFilter != 0 && s.Keys != keyFilter) continue;
                if (diffFilter == 1 && s.Hard != "easy") continue;
                if (diffFilter == 2 && s.Hard != "hard") continue;
                if (diffFilter == 3 && s.Hard != "in") continue;
                if (q.Length > 0 && s.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                view.Add(s);
            }

            if (sortCol >= 0)
            {
                view.Sort(delegate (Song a, Song b)
                {
                    int cmp = 0;
                    switch (sortCol)
                    {
                        case 0: cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
                        case 1: cmp = HardRank(a.Hard).CompareTo(HardRank(b.Hard)); break;
                        case 2: cmp = a.Keys.CompareTo(b.Keys); break;
                        case 3: cmp = a.Level.CompareTo(b.Level); break;
                        case 4: cmp = a.Sync.CompareTo(b.Sync); break;
                    }
                    if (cmp == 0) cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    return sortAsc ? cmp : -cmp;
                });
            }

            string[] headers = { "歌曲", "难度", "键数", "等级", "同步率" };
            for (int i = 0; i < headers.Length; i++)
                lv.Columns[i].Text = (i == sortCol ? (sortAsc ? "▲ " : "▼ ") : "") + headers[i];

            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (Song s in view)
            {
                var item = new ListViewItem(s.Name) { Tag = s };
                item.SubItems.Add(HardName(s.Hard));
                item.SubItems.Add(s.Keys + "K");
                item.SubItems.Add(s.Level.ToString());
                item.SubItems.Add(s.Sync > 0 ? (s.Sync / 100.0).ToString("0.00") + "%" : "-");
                lv.Items.Add(item);
            }
            lv.EndUpdate();
            btnPlay.Enabled = lv.Items.Count > 0;
        }

        static string HardName(string hard)
        {
            if (hard == "hard") return "困难";
            if (hard == "in") return "IN";
            return "简单";
        }

        void Play()
        {
            if (lv.SelectedItems.Count == 0) { Log("先选一首歌"); return; }
            var song = lv.SelectedItems[0].Tag as Song;
            if (song == null) return;

            if (!SceneIsSelect())
            {
                Log("当前不在选歌界面，正在进入…");
                Rpc.Call("{\"cmd\":\"prepare\"}", 3000);
                bool ok = false;
                for (int i = 0; i < 20; i++)
                {
                    System.Threading.Thread.Sleep(400);
                    Application.DoEvents();
                    if (SceneIsSelect()) { ok = true; break; }
                }
                if (!ok) { Log("无法进入选歌界面"); return; }
            }

            string req = "{\"cmd\":\"play\",\"bms\":" + Json.Q(song.Bms) +
                         ",\"hard\":" + Json.Q(song.Hard) +
                         ",\"keys\":" + song.Keys + ",\"ratio\":1.0}";
            string resp = Rpc.Call(req, 8000);
            var o = Json.AsObj(Json.Parse(resp));
            if (o != null && Json.B(o.ContainsKey("ok") ? o["ok"] : null))
            {
                lastResult = "";
                btnPlay.Enabled = false;
                Log("开始：" + song.Name + "（" + song.Keys + "K " + HardName(song.Hard) + "）");
            }
            else Log("开始失败：" + ErrorOf(o));
        }

        void Tick()
        {
            string resp = Rpc.Call("{\"cmd\":\"state\"}", 900);
            var o = Json.AsObj(Json.Parse(resp));
            if (o == null || !Json.B(o.ContainsKey("ok") ? o["ok"] : null))
            {
                lblConn.Text = "○ 未连接（游戏未启动或插件未加载）";
                lblConn.ForeColor = Color.Gray;
                return;
            }
            lblConn.Text = "● 已连接";
            lblConn.ForeColor = Color.SeaGreen;

            var d = Json.AsObj(o["data"]);
            if (d == null) return;
            string scene = Json.S(d["scene"]);
            bool playing = Json.B(d["playing"]);
            double progress = Convert.ToDouble(d["progress"], CultureInfo.InvariantCulture);

            if (scene != lastScene)
            {
                if (scene != null && scene.IndexOf("SelectSong", StringComparison.Ordinal) >= 0 && needRefresh)
                {
                    needRefresh = false;
                    RefreshSongs();
                }
                lastScene = scene;
            }

            if (!autoLoaded && scene != null && scene.IndexOf("SelectSong", StringComparison.Ordinal) >= 0 && songs.Count == 0)
            {
                autoLoaded = true;
                RefreshSongs();
            }

            if (playing)
            {
                bar.Value = Math.Max(0, Math.Min(100, (int)(progress * 100)));
                lblState.Text = "打歌中 " + (progress * 100).ToString("0") + "%";
                btnPlay.Enabled = false;
            }
            else
            {
                lblState.Text = scene;
                if (scene != null && scene.IndexOf("Settlement", StringComparison.Ordinal) >= 0)
                    btnPlay.Enabled = lv.Items.Count > 0;
            }

            object resultObj = d.ContainsKey("result") ? d["result"] : null;
            var r = Json.AsObj(resultObj);
            if (r != null)
            {
                string key = Json.S(r["title"]) + "|" + Json.I(r["sync"]) + "|" + Json.I(r["miss"]) + "|" + Json.I(r["combo"]);
                if (key != lastResult)
                {
                    lastResult = key;
                    Log("结算：" + Json.S(r["title"]) + "  " + (Json.I(r["sync"]) / 100.0).ToString("0.00") +
                        "%  EX " + Json.I(r["ex"]) + " / MISS " + Json.I(r["miss"]) + " / COMBO " + Json.I(r["combo"]));
                    bar.Value = 0;
                    btnPlay.Enabled = true;
                    needRefresh = true;
                    if (scene != null && scene.IndexOf("SelectSong", StringComparison.Ordinal) >= 0)
                    {
                        needRefresh = false;
                        RefreshSongs();
                    }
                }
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object o, System.Threading.ThreadExceptionEventArgs e)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tool-error.log"), e.Exception.ToString());
                };
                AppDomain.CurrentDomain.UnhandledException += delegate (object o, UnhandledExceptionEventArgs e)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tool-error.log"), e.ExceptionObject.ToString());
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception e)
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tool-error.log"), e.ToString());
                }
                catch { }
            }
        }
    }
}
