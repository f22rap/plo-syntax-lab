using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PloNativeMcp {
    public sealed class SetupForm : Form {
        Configuration config;
        bool loading, dirty = true, busy;
        readonly TextBox configPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        readonly ListBox roots = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        readonly TextBox output = new TextBox { Dock = DockStyle.Fill }, history = new TextBox { Dock = DockStyle.Fill };
        readonly TextBox result = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        readonly Label state = new Label { AutoSize = true, Text = "CSVフォルダーを追加して保存してください。" };
        readonly Button save = Button("設定を保存"), check = Button("接続を診断"), copy = Button("AI用の設定をコピー");
        readonly TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(18) };
        public SetupForm(string file) {
            Text = "PLO Syntax Lab — Node.js不要の接続設定";
            Font = new Font("Yu Gothic UI", 10F); AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(960, 700); MinimumSize = new Size(920, 650); StartPosition = FormStartPosition.CenterScreen;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); Controls.Add(layout);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "PLOとAIの接続を準備\nCSV調査・ユーザーsyntax・EQ計算・全アクションのグラフ出力に対応。", Font = new Font(Font.FontFamily, 12F) });
            var files = Row("設定ファイル", configPath);
            var open = Button("既存設定を開く"); var other = Button("別の保存先");
            files.Controls.Add(open); files.Controls.Add(other); layout.Controls.Add(files);
            var rootBox = new GroupBox { Text = "AIが読み込めるCSVフォルダー（サブフォルダーを含む）", Dock = DockStyle.Fill, Padding = new Padding(8, 24, 8, 8) };
            rootBox.Controls.Add(roots); var rootButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 114, FlowDirection = FlowDirection.TopDown };
            var add = Button("追加"); var remove = Button("削除"); rootButtons.Controls.Add(add); rootButtons.Controls.Add(remove); rootBox.Controls.Add(rootButtons); layout.Controls.Add(rootBox);
            var outputRow = Row("結果の保存先", output); var chooseOutput = Button("選択"); outputRow.Controls.Add(chooseOutput); layout.Controls.Add(outputRow);
            var historyRow = Row("アプリの履歴先", history); var chooseHistory = Button("選択"); historyRow.Controls.Add(chooseHistory); layout.Controls.Add(historyRow);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; actions.Controls.Add(save); actions.Controls.Add(check); actions.Controls.Add(copy); layout.Controls.Add(actions);
            layout.Controls.Add(state); layout.Controls.Add(result);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "この診断はPC内の接続確認です。ブラウザ版ChatGPTとの接続には別途トンネル設定が必要です。", ForeColor = Color.DimGray });
            add.Click += delegate { var p = ChooseFolder("CSVが入った親フォルダーを選んでください", null); if (p != null && !roots.Items.Cast<string>().Contains(p, NativePaths.Comparer)) { roots.Items.Add(p); Changed(); } };
            remove.Click += delegate { if (roots.SelectedIndex >= 0) { roots.Items.RemoveAt(roots.SelectedIndex); Changed(); } };
            chooseOutput.Click += delegate { var p = ChooseFolder("結果の保存先", output.Text); if (p != null) output.Text = p; };
            chooseHistory.Click += delegate { var p = ChooseFolder("GUIと同じ履歴フォルダー", history.Text); if (p != null) history.Text = p; };
            output.TextChanged += delegate { Changed(); }; history.TextChanged += delegate { Changed(); };
            save.Click += delegate { Guard(Save); }; check.Click += async delegate { await Diagnose(); };
            copy.Click += delegate { Guard(delegate { config.EnsureUnchanged(); Clipboard.SetText(ClientConfig()); state.Text = "AI用の設定をコピーしました。ChatGPTへの登録はまだ行っていません。"; }); };
            open.Click += delegate { using (var dialog = new OpenFileDialog { Filter = "MCP設定 (*.json)|*.json", Title = "既存のmcp.local.jsonなどを開く" }) if (dialog.ShowDialog(this) == DialogResult.OK) Guard(delegate { LoadFile(dialog.FileName); }); };
            other.Click += delegate { using (var dialog = new SaveFileDialog { Filter = "MCP設定 (*.json)|*.json", FileName = "mcp-native.json", OverwritePrompt = true }) if (dialog.ShowDialog(this) == DialogResult.OK) Guard(delegate { if (File.Exists(dialog.FileName)) LoadFile(dialog.FileName); else { config.FilePath = Path.GetFullPath(dialog.FileName); config.Fingerprint = null; configPath.Text = config.FilePath; Changed(); } }); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; return; } if (dirty && roots.Items.Count > 0 && MessageBox.Show(this, "未保存の変更があります。保存せず閉じますか？", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true; };
            config = Configuration.New(file); Populate();
            Shown += delegate { if (File.Exists(file)) Guard(delegate { LoadFile(file); }); };
        }
        static Button Button(string text) { return new Button { Text = text, AutoSize = true, Height = 32, MinimumSize = new Size(100, 32), Margin = new Padding(3) }; }
        static FlowLayoutPanel Row(string caption, TextBox field) {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            panel.Controls.Add(new Label { Text = caption, Width = 112, Height = 32, TextAlign = ContentAlignment.MiddleLeft });
            field.Dock = DockStyle.None; field.Width = 390; panel.Controls.Add(field); return panel;
        }
        string ChooseFolder(string title, string current) {
            using (var dialog = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true }) {
                if (Directory.Exists(current)) dialog.SelectedPath = current;
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }
        void LoadFile(string file) { var loaded = Configuration.Load(file); config = loaded; Populate(); state.Text = "保存済み設定を読み込みました。接続を診断できます。"; }
        void Populate() {
            loading = true; configPath.Text = config.FilePath; roots.Items.Clear(); roots.Items.AddRange(config.InputRoots);
            output.Text = config.OutputRoot; history.Text = config.HistoryRoot; loading = false;
            dirty = config.Fingerprint == null; result.Clear(); RefreshButtons();
        }
        void Changed() { if (loading) return; dirty = true; result.Clear(); state.Text = "変更があります。設定を保存してから診断してください。"; RefreshButtons(); }
        void RefreshButtons() { save.Enabled = !busy && dirty; check.Enabled = !busy && !dirty; copy.Enabled = !busy && !dirty; }
        void Save() {
            config.InputRoots = roots.Items.Cast<string>().ToArray(); config.OutputRoot = output.Text; config.HistoryRoot = history.Text;
            config = config.Save(); Populate(); state.Text = "保存しました。次に「接続を診断」を押してください。";
        }
        async Task Diagnose() {
            busy = true; layout.Enabled = false; RefreshButtons(); state.Text = "接続EXEを起動し、状態取得を確認しています…";
            try {
                var data = await Task.Run(() => Program.Check(config.FilePath));
                var details = Json.Object(data["status"], "status");
                var paths = (object[])details["input_roots"];
                result.Text = "PC内の接続: 成功\r\nNode.js: 不要\r\n調査ツール: 17個\r\nCSV調査・syntax・EQ・全アクション頻度・グラフ出力に対応\r\nChatGPTからの接続: 未確認\r\n\r\nCSVフォルダー:\r\n" +
                    string.Join("\r\n", paths.Cast<string>()) + "\r\n\r\n結果の保存先:\r\n" + details["output_root"] + "\r\n\r\n履歴の保存先:\r\n" + details["history_root"];
                state.Text = "PC内の接続は成功しました。ChatGPTからの接続はまだ未確認です。";
            }
            catch (Exception e) { state.Text = "接続診断に失敗しました。"; result.Text = e.Message; }
            finally { busy = false; layout.Enabled = true; RefreshButtons(); }
        }
        string ClientConfig() { return Json.Write(Json.Obj("mcpServers", Json.Obj("plo-syntax-lab-native", Json.Obj("command", Assembly.GetExecutingAssembly().Location, "args", new[] { "--stdio", "--config", config.FilePath })))); }
        void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    }
}
