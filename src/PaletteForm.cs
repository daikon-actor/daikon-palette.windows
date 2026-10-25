using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DaikonPalette
{
    /// <summary>文章の入れ方</summary>
    internal enum InsertMode
    {
        Type,
        ShiftEnter,
        Paste,
    }

    /// <summary>
    /// 画面左端に常駐するパレット。
    /// クリックしても自分はアクティブにならない（WS_EX_NOACTIVATE）ので、
    /// 前面のアプリのカーソル位置にそのまま文字が入る。
    /// </summary>
    internal sealed class PaletteForm : Form
    {
        private static readonly Color Back = Color.FromArgb(0x1F, 0x1F, 0x1F);
        private static readonly Color Fore = Color.FromArgb(0xDD, 0xDD, 0xDD);
        private static readonly Color Muted = Color.FromArgb(0x9D, 0x9D, 0x9D);
        private static readonly Color ErrorFore = Color.FromArgb(0xF1, 0x8B, 0x8B);
        private static readonly Color ChipBack = Color.FromArgb(0x3A, 0x3D, 0x41);
        private static readonly Color ChipHover = Color.FromArgb(0x45, 0x49, 0x4E);
        private static readonly Color TemplateBack = Color.FromArgb(0x0E, 0x63, 0x9C);
        private static readonly Color TemplateHover = Color.FromArgb(0x11, 0x77, 0xBB);
        private static readonly Color JapaneseBack = Color.FromArgb(0x18, 0xB9, 0x57); // 日本語: #18B957
        private static readonly Color EnglishBack = Color.FromArgb(0xE5, 0x48, 0x48);  // 英語: #E54848
        private static readonly Color UnknownBack = Color.FromArgb(0x80, 0x80, 0x80);  // 不明: #808080
        private static readonly Color InfoFore = Color.FromArgb(0x18, 0xB9, 0x57);

        /// <summary>パレットの幅（96 DPI のときのピクセル数）</summary>
        private const int LogicalWidth = 240;

        /// <summary>隠したときの帯の幅（96 DPI のときのピクセル数）</summary>
        private const int LogicalCollapsedWidth = 32;

        /// <summary>表示しているときの内側の余白（96 DPI のときのピクセル数）</summary>
        private const int LogicalPadding = 8;

        private readonly AppBar _appBar;
        private readonly Label _ime;
        private readonly Label _imeMini;
        private readonly Panel _expandedPanel;
        private readonly FlowLayoutPanel _collapsedPanel;
        private readonly Label _target;
        private readonly FlowLayoutPanel _content;
        private readonly Label _status;
        private readonly PaletteButton _quit;
        private readonly PaletteButton _showButton;
        private readonly Panel _quitSpacer;
        private readonly Timer _timer;
        private readonly Timer _reloadTimer;
        private readonly Timer _infoTimer;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Font _smallFont = new Font("Yu Gothic UI", 8.5f);
        private readonly Font _groupFont = new Font("Yu Gothic UI", 8.5f, FontStyle.Bold);
        private readonly Font _imeFont = new Font("Yu Gothic UI", 11f, FontStyle.Bold);

        private PaletteContent _data;
        private IntPtr _lastTarget = IntPtr.Zero;
        private string _lastProcess = "";
        private uint _lastPid;
        private bool _busy;
        private bool _imeQueryRunning;
        private bool _collapsed;

        public PaletteForm()
        {
            _appBar = new AppBar(this);
            _appBar.FullscreenChanged += delegate(bool fullscreen)
            {
                // スライドショーや動画を全画面にしたときは隠れる
                Visible = !fullscreen;
            };
            Text = "Daikon Palette";
            Icon = LoadAppIcon();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            // 最前面は CreateParams の WS_EX_TOPMOST で指定する（TopMost プロパティはアクティブ化を伴うことがあるため使わない）
            BackColor = Back;
            ForeColor = Fore;
            Font = new Font("Yu Gothic UI", 10f);

            // ---- 上部: 入力モード・隠す（右上）・入力先・メモ ----
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Top;
            header.AutoSize = true;
            header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            header.ColumnCount = 2;
            header.RowCount = 3;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Padding = new Padding(0, 0, 0, 4);
            header.Margin = Padding.Empty;

            _ime = new Label();
            _ime.AutoSize = true;
            _ime.Padding = new Padding(8, 4, 8, 4);
            _ime.Font = _imeFont;
            _ime.Text = "?";
            _ime.BackColor = UnknownBack;
            _ime.ForeColor = Color.White;
            _ime.Margin = new Padding(0, 0, 8, 0);

            PaletteButton hide = new PaletteButton("«", ChipBack, ChipHover);
            hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hide.Margin = Padding.Empty;
            hide.Click += delegate { SetCollapsed(true); };

            _target = new Label();
            _target.AutoSize = true;
            _target.ForeColor = Muted;
            _target.Font = _smallFont;
            _target.Text = "入力先: ?";
            _target.Margin = new Padding(0, 6, 0, 0);

            FlowLayoutPanel memoRow = NewRow();
            memoRow.Margin = new Padding(0, 6, 0, 0);
            PaletteButton newMemo = new PaletteButton("＋ メモ", TemplateBack, TemplateHover);
            newMemo.Click += delegate { CreateMemo(); };
            PaletteButton memoFolder = new PaletteButton("メモフォルダ", ChipBack, ChipHover);
            memoFolder.Click += delegate { OpenMemoFolder(); };
            memoRow.Controls.Add(newMemo);
            memoRow.Controls.Add(memoFolder);

            header.Controls.Add(_ime, 0, 0);
            header.Controls.Add(hide, 1, 0);
            header.Controls.Add(_target, 0, 1);
            header.SetColumnSpan(_target, 2);
            header.Controls.Add(memoRow, 0, 2);
            header.SetColumnSpan(memoRow, 2);

            // ---- 中央: 単語のボタン ----
            _content = new FlowLayoutPanel();
            _content.Dock = DockStyle.Fill;
            _content.FlowDirection = FlowDirection.TopDown;
            _content.WrapContents = false;
            _content.AutoScroll = true;

            // ---- 下部: 編集ボタンとエラー表示 ----
            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Bottom;
            footer.FlowDirection = FlowDirection.TopDown;
            footer.WrapContents = false;
            footer.AutoSize = true;
            footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            footer.Padding = new Padding(0, 6, 0, 0);

            _status = new Label();
            _status.AutoSize = true;
            _status.ForeColor = ErrorFore;
            _status.Font = _smallFont;
            _status.Visible = false;
            _status.Margin = new Padding(0, 0, 0, 4);

            FlowLayoutPanel editRow = NewRow();
            PaletteButton editWords = new PaletteButton("単語を編集", ChipBack, ChipHover);
            editWords.Font = _smallFont;
            editWords.Click += delegate { OpenForEdit(PaletteData.PrepareWordsFile(_data == null ? null : _data.WordsPath)); };
            PaletteButton editTemplates = new PaletteButton("テンプレートを編集", ChipBack, ChipHover);
            editTemplates.Font = _smallFont;
            editTemplates.Click += delegate { OpenForEdit(PaletteData.PrepareTemplatesFile(_data == null ? null : _data.TemplatesPath)); };
            editRow.Controls.Add(editWords);
            editRow.Controls.Add(editTemplates);

            footer.Controls.Add(_status);
            footer.Controls.Add(editRow);

            // ---- 一番下: 終了（誤って押さないよう、上のボタンと間を空けて地味な色にする） ----
            PaletteButton quit = new PaletteButton("終了", Back, ChipHover);
            quit.AutoSize = false;
            quit.Dock = DockStyle.Bottom;
            quit.ForeColor = Muted;
            quit.Font = _smallFont;
            quit.FlatAppearance.BorderSize = 1;
            quit.FlatAppearance.BorderColor = ChipBack;
            quit.Click += delegate { Close(); };

            Panel quitSpacer = new Panel();
            quitSpacer.Dock = DockStyle.Bottom;

            // 表示しているときの画面（後から追加したものほど外側＝先にレイアウトされる）
            _expandedPanel = new Panel();
            _expandedPanel.Dock = DockStyle.Fill;
            _expandedPanel.Controls.Add(_content);
            _expandedPanel.Controls.Add(header);
            _expandedPanel.Controls.Add(footer);
            _expandedPanel.Controls.Add(quitSpacer);
            _expandedPanel.Controls.Add(quit);
            _content.BringToFront(); // Fill は最後にレイアウトさせる

            // ---- 隠しているときの細い帯: 表示ボタンと入力モード ----
            _collapsedPanel = new FlowLayoutPanel();
            _collapsedPanel.Dock = DockStyle.Fill;
            _collapsedPanel.FlowDirection = FlowDirection.TopDown;
            _collapsedPanel.WrapContents = false;
            _collapsedPanel.Visible = false;

            PaletteButton show = new PaletteButton("»", ChipBack, ChipHover);
            show.AutoSize = false;
            show.Padding = Padding.Empty;
            show.Margin = new Padding(0, 0, 0, 6);
            show.Click += delegate { SetCollapsed(false); };

            _imeMini = new Label();
            _imeMini.AutoSize = false;
            _imeMini.TextAlign = ContentAlignment.MiddleCenter;
            _imeMini.Font = _imeFont;
            _imeMini.Text = "?";
            _imeMini.BackColor = UnknownBack;
            _imeMini.ForeColor = Color.White;
            _imeMini.Margin = Padding.Empty;

            _collapsedPanel.Controls.Add(show);
            _collapsedPanel.Controls.Add(_imeMini);

            Controls.Add(_expandedPanel);
            Controls.Add(_collapsedPanel);

            // 大きさは DPI に合わせて OnHandleCreated で決める
            _quit = quit;
            _showButton = show;
            _quitSpacer = quitSpacer;

            _timer = new Timer();
            _timer.Interval = 300;
            _timer.Tick += delegate { OnTick(); };

            // JSON の保存を検知したら少し待ってから読み直す（保存中に何度も通知が来るため）
            _reloadTimer = new Timer();
            _reloadTimer.Interval = 400;
            _reloadTimer.Tick += delegate
            {
                _reloadTimer.Stop();
                LoadPalette();
            };

            // 「コピーしました」などのお知らせは少しで消す
            _infoTimer = new Timer();
            _infoTimer.Interval = 3000;
            _infoTimer.Tick += delegate
            {
                _infoTimer.Stop();
                ShowError(_data == null || _data.Errors.Count == 0 ? null : string.Join("\n", _data.Errors));
            };
        }

        /// <summary>exe に埋め込んだアプリのアイコン（読めなければ null で既定のアイコン）</summary>
        private static Icon LoadAppIcon()
        {
            try
            {
                using (Stream stream = typeof(PaletteForm).Assembly.GetManifestResourceStream("DaikonPalette.app.ico"))
                {
                    return stream == null ? null : new Icon(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static FlowLayoutPanel NewRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.WrapContents = false;
            row.Margin = Padding.Empty;
            return row;
        }

        /// <summary>96 DPI 基準の長さを今の画面の長さに直す</summary>
        private int Px(int logical)
        {
            using (Graphics g = CreateGraphics())
            {
                return (int)Math.Round(logical * g.DpiX / 96f);
            }
        }

        // ---- 自分をアクティブにしないための設定 ----

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(NativeMethods.MA_NOACTIVATE);
                return;
            }
            if (_appBar != null && _appBar.HandleMessage(ref m))
            {
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _expandedPanel.Padding = new Padding(Px(LogicalPadding));
            _collapsedPanel.Padding = new Padding(Px(4));
            _quit.Height = Px(28);
            _quitSpacer.Height = Px(10);
            _imeMini.Size = new Size(Px(LogicalCollapsedWidth - 8), Px(28));
            _showButton.Size = _imeMini.Size;
            _appBar.Register(Px(LogicalWidth));
            LoadPalette();
            WatchFolder(PaletteData.DataFolder);
            _timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            _appBar.Unregister();
            base.OnFormClosing(e);
        }

        /// <summary>表示しているときの、余白を除いた幅（隠している間に再読み込みしても崩れないよう固定値から計算）</summary>
        private int ExpandedInnerWidth()
        {
            return Px(LogicalWidth) - Px(LogicalPadding) * 2;
        }

        // ---- 隠す・表示する ----

        /// <summary>
        /// 隠す（左端の細い帯にする）・表示する を切り替える。
        /// アプリは起動したままで、帯の幅だけ画面を使う。
        /// </summary>
        private void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed)
            {
                return;
            }
            _collapsed = collapsed;
            SuspendLayout();
            _expandedPanel.Visible = !collapsed;
            _collapsedPanel.Visible = collapsed;
            ResumeLayout();
            _appBar.Resize(Px(collapsed ? LogicalCollapsedWidth : LogicalWidth));
        }

        // ---- パレットの表示 ----

        private void LoadPalette()
        {
            PaletteContent loaded = PaletteData.Load();
            if (loaded.Errors.Count > 0 && _data != null)
            {
                // 編集途中の誤りで表示が変わらないよう、前の内容のまま表示を続ける
                ShowError(string.Join("\n", loaded.Errors) + "\n（直すまで前の内容で表示します）");
                return;
            }
            _data = loaded;
            _content.SuspendLayout();
            _content.Controls.Clear();
            // レイアウト前でも正しい幅になるよう、フォームの幅から計算する
            int width = ExpandedInnerWidth() - SystemInformation.VerticalScrollBarWidth - 4;
            foreach (PaletteGroup group in _data.Groups)
            {
                Label title = new Label();
                title.Text = group.Name;
                title.AutoSize = true;
                title.ForeColor = Muted;
                title.Font = _groupFont;
                title.Margin = new Padding(0, 8, 0, 2);
                _content.Controls.Add(title);

                FlowLayoutPanel flow = new FlowLayoutPanel();
                flow.FlowDirection = FlowDirection.LeftToRight;
                flow.WrapContents = true;
                flow.AutoSize = true;
                flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                flow.MaximumSize = new Size(width, 0);
                flow.MinimumSize = new Size(width, 0);
                flow.Margin = Padding.Empty;
                foreach (PaletteItem item in group.Items)
                {
                    PaletteButton button = group.IsTemplate
                        ? new PaletteButton(item.Label, TemplateBack, TemplateHover)
                        : new PaletteButton(item.Label, ChipBack, ChipHover);
                    button.MaximumSize = new Size(width - 6, 0);
                    PaletteItem captured = item;
                    if (group.IsTemplate)
                    {
                        // テンプレートはクリップボードにコピーするだけ（貼り付けは利用者が Ctrl+V で行う）
                        button.Click += delegate { CopyToClipboard(captured); };
                    }
                    else
                    {
                        button.Click += delegate { OnItemClick(captured); };
                    }
                    flow.Controls.Add(button);
                }
                _content.Controls.Add(flow);
            }
            _content.ResumeLayout();
            ShowError(_data.Errors.Count == 0 ? null : string.Join("\n", _data.Errors));
        }

        /// <summary>エラーがあるときだけ下部に表示する（null で消す）</summary>
        private void ShowError(string text)
        {
            _infoTimer.Stop();
            _status.ForeColor = ErrorFore;
            _status.Text = text ?? "";
            _status.MaximumSize = new Size(ExpandedInnerWidth(), 0);
            _status.Visible = !string.IsNullOrEmpty(text);
        }

        /// <summary>お知らせを下部に少しの間だけ表示する</summary>
        private void ShowInfo(string text)
        {
            ShowError(text);
            _status.ForeColor = InfoFore;
            _infoTimer.Start();
        }

        // ---- テンプレートのコピー ----

        /// <summary>テンプレートを展開してクリップボードに入れる（{{cursor}} は取り除く）</summary>
        private void CopyToClipboard(PaletteItem item)
        {
            string text = InputSender.NormalizeNewlines(Placeholders.Expand(item.Value, DateTime.Now))
                .Replace(Placeholders.Cursor, "")
                .Replace("\n", "\r\n");
            if (text.Length == 0)
            {
                return;
            }
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    ShowInfo("「" + item.Label + "」をコピーしました\nCtrl+V で貼り付けられます");
                    return;
                }
                catch (ExternalException)
                {
                    // 他のアプリがクリップボードを使用中
                    System.Threading.Thread.Sleep(50);
                }
            }
            ShowError("クリップボードを使えません。もう一度押してください");
        }

        // ---- 編集・メモ ----

        private void OpenForEdit(string path)
        {
            if (Launch("notepad.exe", path))
            {
                WatchFolder(Path.GetDirectoryName(path));
            }
        }

        /// <summary>data\memo に新しいメモを作ってメモ帳で開く</summary>
        private void CreateMemo()
        {
            try
            {
                Launch("notepad.exe", PaletteData.CreateMemo(DateTime.Now));
            }
            catch (Exception e)
            {
                ShowError("メモを作れません: " + e.Message);
            }
        }

        private void OpenMemoFolder()
        {
            try
            {
                Directory.CreateDirectory(PaletteData.MemoFolder);
                Launch("explorer.exe", PaletteData.MemoFolder);
            }
            catch (Exception e)
            {
                ShowError("フォルダを開けません: " + e.Message);
            }
        }

        /// <summary>アプリを起動する（パレット自身はアクティブにならないので、起動したアプリが前面に出られるようにする）</summary>
        private bool Launch(string exe, string path)
        {
            try
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                using (Process.Start(exe, "\"" + path + "\""))
                {
                }
                ShowError(null);
                return true;
            }
            catch (Exception e)
            {
                ShowError("開けません: " + e.Message);
                return false;
            }
        }

        /// <summary>フォルダ内の JSON の変更を監視して、保存されたら読み直す</summary>
        private void WatchFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }
            foreach (FileSystemWatcher w in _watchers)
            {
                if (string.Equals(w.Path, folder, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            FileSystemWatcher watcher = new FileSystemWatcher(folder, "*.json");
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            FileSystemEventHandler changed = delegate { ScheduleReload(); };
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += delegate { ScheduleReload(); };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        private void ScheduleReload()
        {
            // FileSystemWatcher の通知は別スレッドから来るので画面のスレッドに移す
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(new Action(delegate
            {
                _reloadTimer.Stop();
                _reloadTimer.Start();
            }));
        }

        // ---- 入力先の追跡と入力モード表示 ----

        private void OnTick()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != Handle && fg != _lastTarget)
            {
                _lastTarget = fg;
                uint pid;
                NativeMethods.GetWindowThreadProcessId(fg, out pid);
                if (pid != _lastPid)
                {
                    _lastPid = pid;
                    _lastProcess = ProcessName(pid);
                    _target.Text = "入力先: " + (_lastProcess.Length == 0 ? "?" : _lastProcess);
                }
            }

            if (_imeQueryRunning)
            {
                return;
            }
            _imeQueryRunning = true;
            IntPtr target = _lastTarget;
            Task.Run(() => ImeReader.Query(target)).ContinueWith(t =>
            {
                _imeQueryRunning = false;
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    ShowIme(t.Result);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ShowIme(ImeState s)
        {
            string text = s.Kind == ImeKind.Unknown
                ? "?"
                : s.Short + (s.Kind == ImeKind.English ? " 英数" : " 日本語");
            if (_ime.Text == text)
            {
                return;
            }
            Color back;
            switch (s.Kind)
            {
                case ImeKind.Japanese:
                    back = JapaneseBack;
                    break;
                case ImeKind.English:
                    back = EnglishBack;
                    break;
                default:
                    back = UnknownBack;
                    break;
            }
            _ime.Text = text;
            _ime.BackColor = back;
            _imeMini.Text = s.Short;
            _imeMini.BackColor = back;
        }

        private static string ProcessName(uint pid)
        {
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName.ToLowerInvariant();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>アプリごとの入れ方。1 行の文字はどのアプリでも直接入力</summary>
        private static InsertMode Resolve(string process, bool multiline)
        {
            if (!multiline)
            {
                return InsertMode.Type;
            }
            switch (process)
            {
                case "notepad":
                    return InsertMode.Type;       // メモ帳: Enter で改行して問題ない
                case "winword":
                case "outlook":
                case "olk":
                    return InsertMode.Paste;      // Word/Outlook: オートフォーマットを避けて貼り付け
                default:
                    return InsertMode.ShiftEnter; // Teams ほか: Enter で送信されないよう Shift+Enter
            }
        }

        // ---- 文字の入力 ----

        private async void OnItemClick(PaletteItem item)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                // 念のため、前面が自分や何もない状態なら直前のアプリに戻す
                IntPtr fg = NativeMethods.GetForegroundWindow();
                if ((fg == IntPtr.Zero || fg == Handle) && _lastTarget != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(_lastTarget);
                    await Task.Delay(100);
                }

                string text = InputSender.NormalizeNewlines(Placeholders.Expand(item.Value, DateTime.Now));
                string before;
                string after;
                Placeholders.SplitCursor(text, out before, out after);
                string full = before + after;
                InsertMode mode = Resolve(_lastProcess, full.IndexOf('\n') >= 0);

                if (mode == InsertMode.Paste)
                {
                    await PasteAsync(full);
                }
                else
                {
                    InputSender.TypeText(full, mode == InsertMode.ShiftEnter);
                }
                if (after.Length > 0)
                {
                    await Task.Delay(mode == InsertMode.Paste ? 150 : 50);
                    InputSender.PressLeft(InputSender.CaretSteps(after));
                }
            }
            catch (Exception e)
            {
                ShowError("入力できません: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>クリップボード経由で貼り付け、少し待ってから元の内容に戻す</summary>
        private async Task PasteAsync(string text)
        {
            DataObject saved = SaveClipboard();
            if (!await RetryAsync(delegate { Clipboard.SetText(text); }))
            {
                throw new InvalidOperationException("クリップボードを使えません");
            }
            await Task.Delay(30);
            InputSender.PressPaste();
            await Task.Delay(500); // 貼り付けが終わるのを待つ
            await RetryAsync(delegate
            {
                if (saved == null)
                {
                    Clipboard.Clear();
                }
                else
                {
                    Clipboard.SetDataObject(saved, true);
                }
            });
        }

        /// <summary>今のクリップボードの内容を（取り出せる形式だけ）コピーしておく</summary>
        private static DataObject SaveClipboard()
        {
            try
            {
                IDataObject current = Clipboard.GetDataObject();
                if (current == null)
                {
                    return null;
                }
                DataObject copy = new DataObject();
                bool any = false;
                foreach (string format in current.GetFormats(false))
                {
                    try
                    {
                        object data = current.GetData(format, false);
                        if (data != null)
                        {
                            copy.SetData(format, data);
                            any = true;
                        }
                    }
                    catch (Exception)
                    {
                        // 取り出せない形式は諦める
                    }
                }
                return any ? copy : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Task<bool> RetryAsync(Action action)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (ExternalException)
                {
                    // 他のアプリがクリップボードを使用中
                }
                await Task.Delay(50);
            }
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (FileSystemWatcher w in _watchers)
                {
                    w.Dispose();
                }
                _timer.Dispose();
                _reloadTimer.Dispose();
                _infoTimer.Dispose();
                _smallFont.Dispose();
                _groupFont.Dispose();
                _imeFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>押してもフォーカスを取らないボタン</summary>
    internal sealed class PaletteButton : Button
    {
        public PaletteButton(string text, Color back, Color hover)
        {
            SetStyle(ControlStyles.Selectable, false);
            Text = text;
            UseMnemonic = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = hover;
            BackColor = back;
            ForeColor = Color.FromArgb(0xEE, 0xEE, 0xEE);
            Margin = new Padding(0, 0, 4, 4);
            Padding = new Padding(2, 0, 2, 0);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        // フォーカス枠を描かない
        protected override bool ShowFocusCues
        {
            get { return false; }
        }
    }
}
