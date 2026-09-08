using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    public static string[] StartFiles = new string[0];
    const string MutexName = "Local\\EncodeKitSingleton";
    const string PipeName = "EncodeKitSingletonPipe";

    [STAThread]
    static void Main(string[] args)
    {
        if (args == null) args = new string[0];
        bool created;
        Mutex mutex = new Mutex(true, MutexName, out created);
        if (!created) { SendToRunning(args); return; }
        StartFiles = args;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }

    static void SendToRunning(string[] files)
    {
        try
        {
            NamedPipeClientStream pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(1500);
            StreamWriter w = new StreamWriter(pipe, Encoding.UTF8);
            int i;
            for (i = 0; i < files.Length; i++) w.WriteLine(files[i]);
            w.Flush();
            w.Dispose();
            pipe.Dispose();
        }
        catch { }
    }

    public static void Listen(MainForm form)
    {
        Thread t = new Thread(new ThreadStart(delegate
        {
            while (true)
            {
                try
                {
                    NamedPipeServerStream pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    pipe.WaitForConnection();
                    StreamReader r = new StreamReader(pipe, Encoding.UTF8);
                    List<string> list = new List<string>();
                    string line;
                    while ((line = r.ReadLine()) != null)
                        if (line.Length > 0) list.Add(line);
                    r.Dispose();
                    pipe.Dispose();
                    if (list.Count > 0)
                    {
                        string[] arr = list.ToArray();
                        form.BeginInvoke(new Action(delegate { form.AddIncoming(arr); }));
                    }
                }
                catch { Thread.Sleep(200); }
            }
        }));
        t.IsBackground = true;
        t.Start();
    }
}

sealed class Preset
{
    public string Name;
    public string Pre = "";
    public string Args = "";
    public string Suffix = ".out";
    public string Ext = "mp4";
    public string Note = "";
    public override string ToString() { return Name; }
    public bool NeedsCuda()
    {
        string s = ((Pre ?? "") + " " + (Args ?? "")).ToLowerInvariant();
        return s.IndexOf("cuda") >= 0 || s.IndexOf("nvenc") >= 0;
    }
}

sealed class Job
{
    public string Path;
    public string Status;
    public string OutputPath = "";
    public string SizeText = "";
    public string DurationText = "";
    public string ConvertedText = "";
    public bool OutputSmaller = true;
    public long InputBytes = 0;
}

sealed class AppSettings
{
    public int CpuPercent = 75;
    public ProcessPriorityClass Priority = ProcessPriorityClass.BelowNormal;
}

sealed class BarPanel : Panel
{
    int pct;
    string caption = "";
    Color fill;
    static readonly Color Track = Color.FromArgb(20, 22, 26);
    static readonly Color Fg = Color.FromArgb(232, 234, 237);

    public BarPanel(Color fillColor)
    {
        fill = fillColor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
        BackColor = Track;
    }

    public void Set(int percent, string text)
    {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        if (pct == percent && caption == text) return;
        pct = percent;
        caption = text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Track);
        int w = Width * pct / 100;
        if (w > 0)
        {
            using (SolidBrush b = new SolidBrush(fill))
                g.FillRectangle(b, 0, 0, w, Height);
        }
        if (caption != null && caption.Length > 0)
        {
            using (SolidBrush b = new SolidBrush(Fg))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(caption, Font, b, ClientRectangle, sf);
            }
        }
    }
}

sealed class DarkButton : Button
{
    public Color EnabledFore = Color.FromArgb(232, 234, 237);
    public Color DisabledFore = Color.FromArgb(140, 146, 154);
    public DarkButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Color text = Enabled ? EnabledFore : DisabledFore;
        e.Graphics.Clear(BackColor);
        using (Pen p = new Pen(FlatAppearance.BorderColor))
            e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

sealed class MainForm : Form
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    static readonly Color Bg = Color.FromArgb(27, 29, 33);
    static readonly Color Card = Color.FromArgb(34, 37, 43);
    static readonly Color Border = Color.FromArgb(42, 46, 54);
    static readonly Color Fg = Color.FromArgb(232, 234, 237);
    static readonly Color Muted = Color.FromArgb(154, 160, 166);
    static readonly Color Accent = Color.FromArgb(45, 212, 191);
    static readonly Color Danger = Color.FromArgb(248, 113, 113);
    static readonly Color DisabledFg = Color.FromArgb(140, 146, 154);
    static readonly Color HeaderBack = Color.White;
    static readonly Color HeaderText = Color.Black;

    ComboBox cboPreset = new ComboBox();
    ComboBox cboDest = new ComboBox();
    ListView lst = new ListView();
    Label lblFile = new Label();
    Label lblOut = new Label();
    Label lblPreset = new Label();
    Label lblDur = new Label();
    Label lblElapsed = new Label();
    Label lblEta = new Label();
    Label lblSpeed = new Label();
    Label lblFfmpeg = new Label();
    Label lblDestTitle = new Label();
    Label lblNote = new Label();
    LinkLabel lnkDest = new LinkLabel();
    ToolTip tip = new ToolTip();
    BarPanel bar = new BarPanel(Color.FromArgb(17, 94, 89));
    BarPanel cpuBar = new BarPanel(Color.FromArgb(59, 91, 140));
    BarPanel ramBar = new BarPanel(Color.FromArgb(90, 70, 120));
    TextBox log = new TextBox();
    System.Windows.Forms.Timer cpuTimer = new System.Windows.Forms.Timer();
    PerformanceCounter cpuCounter;
    Font btnFont;

    DarkButton btnAdd = new DarkButton();
    DarkButton btnRemove = new DarkButton();
    DarkButton btnClear = new DarkButton();
    DarkButton btnStart = new DarkButton();
    DarkButton btnRestart = new DarkButton();
    DarkButton btnStop = new DarkButton();
    DarkButton btnReload = new DarkButton();
    DarkButton btnPickDir = new DarkButton();

    List<Preset> presets = new List<Preset>();
    List<Job> jobs = new List<Job>();
    AppSettings settings = new AppSettings();
    CancellationTokenSource cts;
    Process currentProc;
    string appDir;
    string assetsDir;
    string customDir = "";
    bool loadingPresets;
    bool loadingDest;
    bool cudaOk;
    bool busy;
    int lastPct = -1;
    string lastBarText = "";

    public MainForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
        appDir = AppDomain.CurrentDomain.BaseDirectory;
        assetsDir = Path.Combine(appDir, "assets");
        btnFont = new Font("Segoe UI", 8f);

        this.Text = "EncodeKit";
        Width = 1180;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9.75f);
        MinimumSize = new Size(1020, 640);
        AllowDrop = true;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += OnFormKeyDown;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        string ico = Path.Combine(assetsDir, "app.ico");
        if (File.Exists(ico))
        {
            try { this.Icon = new Icon(ico); }
            catch { }
        }

        Label title = MakeLabel("EncodeKit", 16, 12, 200, 24, true);
        title.Font = new Font("Segoe UI Semibold", 14f);
        title.AutoSize = true;

        lblFfmpeg.ForeColor = Muted;
        lblFfmpeg.TextAlign = ContentAlignment.MiddleRight;
        lblFfmpeg.Cursor = Cursors.Hand;
        lblFfmpeg.Click += OnFfmpegClick;
        Controls.Add(lblFfmpeg);

        MakeLabel("Preset", 16, 48, 80, 20, true);
        lblDestTitle.Text = "Output Destination";
        lblDestTitle.AutoSize = true;
        lblDestTitle.ForeColor = Fg;
        Controls.Add(lblDestTitle);

        cboPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        cboPreset.FlatStyle = FlatStyle.Flat;
        cboPreset.BackColor = Card;
        cboPreset.ForeColor = Fg;
        cboPreset.SetBounds(16, 70, 360, 32);
        cboPreset.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        cboPreset.SelectedIndexChanged += OnPresetChanged;
        Controls.Add(cboPreset);

        StyleBtn(btnReload, "Reload preset", false);
        btnReload.SetBounds(384, 68, 120, 32);
        btnReload.Click += OnReload;
        Controls.Add(btnReload);

        lblNote.SetBounds(16, 106, 760, 36);
        lblNote.ForeColor = Muted;
        lblNote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(lblNote);

        cboDest.DropDownStyle = ComboBoxStyle.DropDownList;
        cboDest.FlatStyle = FlatStyle.Flat;
        cboDest.BackColor = Card;
        cboDest.ForeColor = Fg;
        cboDest.Items.Add("Same Source");
        cboDest.Items.Add("Custom Directory");
        cboDest.SelectedIndex = 0;
        cboDest.SelectedIndexChanged += OnDestChanged;
        Controls.Add(cboDest);

        StyleBtn(btnPickDir, "Select Directory", false);
        btnPickDir.Click += OnPickDir;
        btnPickDir.Enabled = false;
        Controls.Add(btnPickDir);

        lnkDest.AutoSize = false;
        lnkDest.LinkColor = Accent;
        lnkDest.ActiveLinkColor = Accent;
        lnkDest.VisitedLinkColor = Accent;
        lnkDest.ForeColor = Muted;
        lnkDest.LinkClicked += OnDestLink;
        Controls.Add(lnkDest);

        Panel queueBox = CardPanel(16, 150, 760, 258);
        queueBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        queueBox.Controls.Add(MakeLabel("Queue", 12, 8, 200, 22, false));

        lst.SetBounds(12, 34, 736, 212);
        lst.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lst.View = View.Details;
        lst.FullRowSelect = true;
        lst.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lst.BorderStyle = BorderStyle.None;
        lst.BackColor = Card;
        lst.ForeColor = Fg;
        lst.AllowDrop = true;
        lst.MultiSelect = true;
        lst.OwnerDraw = true;
        lst.Columns.Add("File", 220);
        lst.Columns.Add("Size", 70);
        lst.Columns.Add("Duration", 80);
        lst.Columns.Add("Status", 80);
        lst.Columns.Add("Source", 90);
        lst.Columns.Add("Converted", 150);
        // Dummy image list sets row height (~20 default). 40px ≈ 10px padding top and bottom.
        ImageList rowSize = new ImageList();
        rowSize.ImageSize = new Size(1, 40);
        lst.SmallImageList = rowSize;
        lst.DrawColumnHeader += OnDrawHeader;
        lst.DrawItem += OnDrawItem;
        lst.DrawSubItem += OnDrawSubItem;
        lst.ItemDrag += OnListItemDrag;
        lst.DragEnter += OnListDragEnter;
        lst.DragOver += OnListDragOver;
        lst.DragDrop += OnListDragDrop;
        lst.MouseClick += OnListClick;
        lst.MouseMove += OnListMouseMove;
        lst.KeyDown += OnListKeyDown;
        queueBox.Controls.Add(lst);
        Controls.Add(queueBox);

        Panel jobBox = CardPanel(792, 150, 360, 258);
        jobBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        jobBox.Controls.Add(MakeLabel("Current job", 12, 8, 200, 22, false));
        int y = 40;
        AddJobRow(jobBox, "Filename", lblFile, ref y);
        AddJobRow(jobBox, "Output", lblOut, ref y);
        AddJobRow(jobBox, "Preset", lblPreset, ref y);
        AddJobRow(jobBox, "Duration", lblDur, ref y);
        AddJobRow(jobBox, "Elapsed", lblElapsed, ref y);
        AddJobRow(jobBox, "ETA", lblEta, ref y);
        AddJobRow(jobBox, "Speed", lblSpeed, ref y);
        Controls.Add(jobBox);

        bar.SetBounds(16, 424, 760, 28);
        bar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        bar.Font = Font;
        Controls.Add(bar);

        cpuBar.SetBounds(792, 424, 176, 28);
        cpuBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        cpuBar.Font = Font;
        cpuBar.Set(0, "CPU 0%");
        Controls.Add(cpuBar);

        ramBar.SetBounds(976, 424, 176, 28);
        ramBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        ramBar.Font = Font;
        ramBar.Set(0, "RAM 0%");
        Controls.Add(ramBar);

        StyleBtn(btnAdd, "Add files", false);
        StyleBtn(btnRemove, "Remove", false);
        StyleBtn(btnClear, "Clear", false);
        StyleBtn(btnStart, "Start", true);
        StyleBtn(btnRestart, "Restart all", false);
        StyleBtn(btnStop, "Stop", false);
        btnStop.ForeColor = Danger;
        btnStop.EnabledFore = Danger;

        btnAdd.SetBounds(16, 464, 100, 36);
        btnRemove.SetBounds(122, 464, 90, 36);
        btnClear.SetBounds(218, 464, 80, 36);
        btnStart.SetBounds(304, 464, 90, 36);
        btnRestart.SetBounds(400, 464, 110, 36);
        btnStop.SetBounds(516, 464, 80, 36);
        btnAdd.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRemove.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnClear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnStart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRestart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnStop.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(btnAdd);
        Controls.Add(btnRemove);
        Controls.Add(btnClear);
        Controls.Add(btnStart);
        Controls.Add(btnRestart);
        Controls.Add(btnStop);

        btnAdd.Click += OnAdd;
        btnRemove.Click += OnRemove;
        btnClear.Click += OnClear;
        btnStart.Click += OnStart;
        btnRestart.Click += OnRestartAll;
        btnStop.Click += OnStop;

        Panel logBox = CardPanel(16, 516, 1136, 190);
        logBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logBox.Controls.Add(MakeLabel("Log", 12, 6, 80, 20, false));
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.BorderStyle = BorderStyle.None;
        log.BackColor = Color.FromArgb(18, 19, 22);
        log.ForeColor = Color.FromArgb(180, 186, 192);
        log.Font = new Font("Consolas", 9f);
        log.SetBounds(12, 28, 1112, 148);
        log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logBox.Controls.Add(log);
        Controls.Add(logBox);

        cpuTimer.Interval = 500;
        cpuTimer.Tick += OnCpuTick;
        Load += OnLoadForm;
        Resize += OnResizeForm;
        Shown += OnShown;
    }

    string SettingsPath() { return Path.Combine(assetsDir, "settings.ini"); }

    void EnsureSettings()
    {
        string path = SettingsPath();
        if (!File.Exists(path))
        {
            try
            {
                if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);
                File.WriteAllText(path,
                    "; CPU thread cap as a percent of logical processors." + Environment.NewLine +
                    "; Possible values: 1 to 100" + Environment.NewLine +
                    "; Used only for CPU presets (not CUDA / NVENC)." + Environment.NewLine +
                    "cpu_percent=75" + Environment.NewLine +
                    Environment.NewLine +
                    "; Windows priority for the ffmpeg.exe process." + Environment.NewLine +
                    "; Possible values: Idle, BelowNormal, Normal, AboveNormal, High" + Environment.NewLine +
                    "priority=BelowNormal" + Environment.NewLine);
            }
            catch (Exception ex) { LogLine("Could not create settings.ini: " + ex.Message); }
        }
        LoadSettings();
    }

    void LoadSettings()
    {
        settings = new AppSettings();
        string path = SettingsPath();
        if (!File.Exists(path)) return;
        try
        {
            string[] lines = File.ReadAllLines(path);
            int i;
            for (i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (key == "cpu_percent")
                {
                    int n;
                    if (int.TryParse(val, out n))
                    {
                        if (n < 1) n = 1;
                        if (n > 100) n = 100;
                        settings.CpuPercent = n;
                    }
                }
                else if (key == "priority")
                    settings.Priority = ParsePriority(val);
            }
        }
        catch (Exception ex) { LogLine("settings.ini: " + ex.Message); }
    }

    static ProcessPriorityClass ParsePriority(string val)
    {
        if (val == null) return ProcessPriorityClass.BelowNormal;
        string s = val.Trim().ToLowerInvariant().Replace(" ", "");
        if (s == "idle") return ProcessPriorityClass.Idle;
        if (s == "belownormal") return ProcessPriorityClass.BelowNormal;
        if (s == "normal") return ProcessPriorityClass.Normal;
        if (s == "abovenormal") return ProcessPriorityClass.AboveNormal;
        if (s == "high") return ProcessPriorityClass.High;
        return ProcessPriorityClass.BelowNormal;
    }

    int CpuThreadCount()
    {
        int cores = Environment.ProcessorCount;
        if (cores < 1) cores = 1;
        int n = cores * settings.CpuPercent / 100;
        if (n < 1) n = 1;
        if (n > cores) n = cores;
        return n;
    }

    string BuildCpuArgs(string args, int threads)
    {
        if (args == null) args = "";
        string low = args.ToLowerInvariant();
        if (low.IndexOf("-threads ") < 0)
            args = "-threads " + threads.ToString() + " " + args;
        low = args.ToLowerInvariant();
        if (low.IndexOf("libsvtav1") < 0) return args;
        int p = low.IndexOf("-svtav1-params");
        if (p < 0) return args + " -svtav1-params lp=" + threads.ToString();
        int i = p + 14;
        while (i < args.Length && args[i] == ' ') i++;
        int start = i;
        while (i < args.Length && args[i] != ' ') i++;
        string block = args.Substring(start, i - start);
        if (block.ToLowerInvariant().IndexOf("lp=") >= 0) return args;
        if (block.Length == 0)
            return args.Substring(0, start) + "lp=" + threads.ToString() + args.Substring(i);
        return args.Substring(0, start) + block + ":lp=" + threads.ToString() + args.Substring(i);
    }

    void LayoutTopRight()
    {
        lblFfmpeg.SetBounds(ClientSize.Width - 16 - 420, 12, 420, 22);
        lblFfmpeg.Anchor = AnchorStyles.Top | AnchorStyles.Right;
    }

    void LayoutDest()
    {
        int right = ClientSize.Width - 16;
        btnPickDir.SetBounds(right - 140, 68, 140, 32);
        btnPickDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cboDest.SetBounds(btnPickDir.Left - 228, 70, 220, 32);
        cboDest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblDestTitle.Left = cboDest.Left;
        lblDestTitle.Top = 48;
        lblDestTitle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lnkDest.SetBounds(cboDest.Left, 106, right - cboDest.Left, 36);
        lnkDest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
    }

    void OnShown(object sender, EventArgs e)
    {
        Program.Listen(this);
        LayoutTopRight();
        LayoutDest();
        UpdateDestHint();
        UpdatePresetNote();
        cpuTimer.Start();
        BringToFront();
        Activate();
    }

    public void AddIncoming(string[] files)
    {
        if (busy) { LogLine("Queue is locked while encoding. Stop first to add more files."); return; }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        AddFiles(files);
        BringToFront();
        Activate();
        LogLine("Added item(s) from Send To / command line.");
    }

    void OnFfmpegClick(object sender, EventArgs e)
    {
        try { Process.Start("https://github.com/BtbN/FFmpeg-Builds/releases/tag/latest"); }
        catch (Exception ex) { LogLine(ex.Message); }
    }

    void OnFormKeyDown(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (e.KeyCode == Keys.Delete && !log.Focused) { OnRemove(sender, e); e.Handled = true; }
    }

    void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (e.KeyCode == Keys.Delete) { OnRemove(sender, e); e.Handled = true; }
    }

    void OnDragEnter(object sender, DragEventArgs e)
    {
        if (busy) { e.Effect = DragDropEffects.None; return; }
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    void OnDragDrop(object sender, DragEventArgs e)
    {
        if (busy) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
    }

    void OnReload(object sender, EventArgs e)
    {
        if (busy) return;
        LoadSettings();
        cudaOk = DetectCuda();
        LoadPresets();
        UpdateDestHint();
        UpdatePresetNote();
        LogLine("Settings: cpu_percent=" + settings.CpuPercent.ToString() + ", priority=" + settings.Priority.ToString());
    }

    void OnAdd(object sender, EventArgs e) { if (!busy) PickFiles(); }

    void OnRemove(object sender, EventArgs e)
    {
        if (busy) return;
        if (lst.SelectedIndices.Count == 0) return;
        List<int> idx = new List<int>();
        int i;
        for (i = 0; i < lst.SelectedIndices.Count; i++) idx.Add(lst.SelectedIndices[i]);
        idx.Sort();
        for (i = idx.Count - 1; i >= 0; i--)
        {
            int n = idx[i];
            if (n >= 0 && n < jobs.Count) jobs.RemoveAt(n);
        }
        RefreshList();
    }

    void OnClear(object sender, EventArgs e)
    {
        if (busy) return;
        jobs.Clear();
        RefreshList();
    }

    void OnStart(object sender, EventArgs e) { if (!busy) RunQueue(false); }

    void OnRestartAll(object sender, EventArgs e)
    {
        if (busy) return;
        int i;
        for (i = 0; i < jobs.Count; i++)
        {
            jobs[i].Status = "Waiting";
            jobs[i].OutputPath = "";
        }
        RefreshList();
        RunQueue(true);
    }

    void OnStop(object sender, EventArgs e)
    {
        if (!busy) return;
        if (cts != null) cts.Cancel();
        try { if (currentProc != null && !currentProc.HasExited) currentProc.Kill(); }
        catch { }
    }

    void OnPresetChanged(object sender, EventArgs e)
    {
        if (loadingPresets) return;
        SaveLastPreset();
        UpdateDestHint();
        UpdatePresetNote();
    }

    void UpdatePresetNote()
    {
        if (cboPreset.SelectedItem == null) { lblNote.Text = ""; tip.SetToolTip(lblNote, ""); return; }
        string n = ((Preset)cboPreset.SelectedItem).Note ?? "";
        n = n.Trim();
        lblNote.Text = n;
        tip.SetToolTip(lblNote, n);
    }

    void OnDestChanged(object sender, EventArgs e)
    {
        if (busy || loadingDest) return;
        bool custom = cboDest.SelectedIndex == 1;
        btnPickDir.Enabled = custom;
        if (custom && (customDir == null || customDir.Length == 0)) OnPickDir(sender, e);
        SaveDest();
        UpdateDestHint();
    }

    void OnPickDir(object sender, EventArgs e)
    {
        if (busy) return;
        FolderBrowserDialog d = new FolderBrowserDialog();
        d.Description = "Select output directory";
        if (customDir != null && Directory.Exists(customDir)) d.SelectedPath = customDir;
        if (d.ShowDialog(this) == DialogResult.OK)
        {
            customDir = d.SelectedPath;
            SaveDest();
            UpdateDestHint();
        }
        else if (customDir == null || customDir.Length == 0)
        {
            loadingDest = true;
            cboDest.SelectedIndex = 0;
            loadingDest = false;
            btnPickDir.Enabled = false;
            UpdateDestHint();
        }
        d.Dispose();
    }

    void OnDestLink(object sender, LinkLabelLinkClickedEventArgs e)
    {
        if (cboDest.SelectedIndex != 1 || customDir == null || !Directory.Exists(customDir)) return;
        try { Process.Start("explorer.exe", customDir); }
        catch (Exception ex) { LogLine(ex.Message); }
    }

    void UpdateDestHint()
    {
        lnkDest.Links.Clear();
        if (cboDest.SelectedIndex == 1)
        {
            if (customDir != null && customDir.Length > 0)
            {
                string shown = TrimPath(customDir, 54);
                lnkDest.Text = shown;
                lnkDest.LinkArea = new LinkArea(0, shown.Length);
                lnkDest.LinkBehavior = LinkBehavior.HoverUnderline;
                tip.SetToolTip(lnkDest, customDir);
            }
            else
            {
                lnkDest.Text = "No directory selected";
                lnkDest.LinkArea = new LinkArea(0, 0);
                tip.SetToolTip(lnkDest, "");
            }
        }
        else
        {
            string suffix = ".out";
            string ext = "mp4";
            if (cboPreset.SelectedItem != null)
            {
                Preset p = (Preset)cboPreset.SelectedItem;
                suffix = p.Suffix;
                ext = p.Ext;
            }
            lnkDest.Text = "Same Source Name suffix " + suffix + "." + ext + " at the end";
            lnkDest.LinkArea = new LinkArea(0, 0);
            tip.SetToolTip(lnkDest, "");
        }
    }

    static string TrimPath(string path, int max)
    {
        if (path == null) return "";
        if (path.Length <= max) return path;
        return path.Substring(0, 18) + "..." + path.Substring(path.Length - (max - 21));
    }

    void OnListItemDrag(object sender, ItemDragEventArgs e)
    {
        if (!busy) lst.DoDragDrop(e.Item, DragDropEffects.Move);
    }

    void OnListDragEnter(object sender, DragEventArgs e)
    {
        if (busy) { e.Effect = DragDropEffects.None; return; }
        if (e.Data.GetDataPresent(typeof(ListViewItem))) e.Effect = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    void OnListDragOver(object sender, DragEventArgs e)
    {
        if (busy) { e.Effect = DragDropEffects.None; return; }
        if (e.Data.GetDataPresent(typeof(ListViewItem))) e.Effect = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    void OnListDragDrop(object sender, DragEventArgs e)
    {
        if (busy) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
            return;
        }
        if (!e.Data.GetDataPresent(typeof(ListViewItem))) return;
        Point pt = lst.PointToClient(new Point(e.X, e.Y));
        ListViewItem hover = lst.GetItemAt(pt.X, pt.Y);
        ListViewItem drag = (ListViewItem)e.Data.GetData(typeof(ListViewItem));
        if (drag == null) return;
        int from = drag.Index;
        int to = hover != null ? hover.Index : jobs.Count - 1;
        if (from < 0 || from >= jobs.Count) return;
        if (to < 0) to = 0;
        if (to >= jobs.Count) to = jobs.Count - 1;
        if (from == to) return;
        Job job = jobs[from];
        jobs.RemoveAt(from);
        jobs.Insert(to, job);
        RefreshList();
        if (to < lst.Items.Count) lst.Items[to].Selected = true;
    }

    void OnListClick(object sender, MouseEventArgs e)
    {
        ListViewHitTestInfo hit = lst.HitTest(e.Location);
        if (hit.Item == null || hit.SubItem == null) return;
        int i = hit.Item.Index;
        if (i < 0 || i >= jobs.Count) return;
        Job j = jobs[i];
        int col = SubItemColumn(hit);
        if (col == 4 && j.Path != null && File.Exists(j.Path)) RevealFile(j.Path);
        else if (col == 5 && j.Status == "Done" && j.OutputPath != null && j.OutputPath.Length > 0)
            RevealOutput(j.OutputPath);
    }

    static int SubItemColumn(ListViewHitTestInfo hit)
    {
        int s;
        for (s = 0; s < hit.Item.SubItems.Count; s++)
            if (hit.Item.SubItems[s] == hit.SubItem) return s;
        return -1;
    }

    void OnListMouseMove(object sender, MouseEventArgs e)
    {
        ListViewHitTestInfo hit = lst.HitTest(e.Location);
        bool hand = false;
        if (hit.Item != null && hit.SubItem != null)
        {
            int col = SubItemColumn(hit);
            if ((col == 4 && hit.SubItem.Text == "Open") ||
                (col == 5 && hit.SubItem.Text != null && hit.SubItem.Text.Length > 0))
                hand = true;
        }
        lst.Cursor = hand ? Cursors.Hand : Cursors.Default;
    }

    void OnDrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
    {
        Rectangle r = e.Bounds;
        // Last column paints the leftover header strip so no black/white gap remains.
        if (e.ColumnIndex == lst.Columns.Count - 1)
            r.Width = Math.Max(r.Width, lst.ClientSize.Width - r.X);
        using (SolidBrush b = new SolidBrush(HeaderBack))
            e.Graphics.FillRectangle(b, r);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, e.Bounds, HeaderText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    void OnDrawItem(object sender, DrawListViewItemEventArgs e) { }

    void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
        Color back = e.Item.Selected ? Color.FromArgb(42, 58, 62) : Card;
        using (SolidBrush b = new SolidBrush(back))
            e.Graphics.FillRectangle(b, e.Bounds);

        string text = e.SubItem.Text;
        bool isOpen = e.ColumnIndex == 4 && text == "Open";
        bool isConv = e.ColumnIndex == 5 && text != null && text.Length > 0;
        if (isOpen || isConv)
        {
            Size sz = TextRenderer.MeasureText(text, btnFont, new Size(1000, 22),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int w = Math.Min(e.Bounds.Width - 12, Math.Max(isOpen ? 56 : 88, sz.Width + 16));
            int h = 22;
            int x = e.Bounds.X + 6;
            int y = e.Bounds.Y + (e.Bounds.Height - h) / 2;
            Rectangle r = new Rectangle(x, y, w, h);
            Color fill = Color.FromArgb(16, 78, 72);
            Color ring = Accent;
            if (isConv && e.Item.Index >= 0 && e.Item.Index < jobs.Count)
            {
                if (jobs[e.Item.Index].OutputSmaller)
                {
                    fill = Color.FromArgb(20, 83, 45);
                    ring = Color.FromArgb(74, 222, 128);
                }
                else
                {
                    fill = Color.FromArgb(127, 29, 29);
                    ring = Color.FromArgb(248, 113, 113);
                }
            }
            using (SolidBrush b = new SolidBrush(fill))
                e.Graphics.FillRectangle(b, r);
            using (Pen p = new Pen(ring))
                e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            TextRenderer.DrawText(e.Graphics, text, btnFont, r, Fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return;
        }

        TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds, e.Item.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void RevealFile(string path)
    {
        try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
        catch (Exception ex) { LogLine(ex.Message); }
    }

    // Sequence outputs (%04d.jpeg) are a folder, not a single file.
    void RevealOutput(string path)
    {
        try
        {
            if (path.IndexOf('%') >= 0)
            {
                string dir = Path.GetDirectoryName(path);
                if (dir != null && Directory.Exists(dir))
                {
                    Process.Start("explorer.exe", dir);
                    return;
                }
            }
            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
                return;
            }
            if (File.Exists(path)) RevealFile(path);
            else
            {
                string dir = Path.GetDirectoryName(path);
                if (dir != null && Directory.Exists(dir))
                    Process.Start("explorer.exe", dir);
            }
        }
        catch (Exception ex) { LogLine(ex.Message); }
    }

    static long MeasureOutputBytes(string output)
    {
        if (string.IsNullOrEmpty(output)) return 0;
        if (output.IndexOf('%') >= 0)
        {
            string dir = Path.GetDirectoryName(output);
            if (dir == null || !Directory.Exists(dir)) return 0;
            long sum = 0;
            string[] files = Directory.GetFiles(dir);
            int i;
            for (i = 0; i < files.Length; i++)
            {
                try { sum += new FileInfo(files[i]).Length; }
                catch { }
            }
            return sum;
        }
        try
        {
            if (File.Exists(output)) return new FileInfo(output).Length;
        }
        catch { }
        return 0;
    }

    static string SizeDeltaText(long src, long dst)
    {
        string size = FormatSize(dst);
        if (src <= 0) return size;
        double pct = (dst - src) * 100.0 / src;
        string sign = pct > 0 ? "+" : "";
        return "Open " + size + " (" + sign + pct.ToString("0") + "%)";
    }

    void OnLoadForm(object sender, EventArgs e)
    {
        EnsureSendToShortcut();
        EnsureSettings();
        try
        {
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
        }
        catch { cpuCounter = null; }
        cudaOk = DetectCuda();
        LoadPresets();
        LoadDest();
        UpdateFfmpegStatus();
        LayoutTopRight();
        LayoutDest();
        UpdateDestHint();
        UpdatePresetNote();
        SetJob("-", "-", "-", "--:--:--", "--", "--", "-");
        SetBar(0, "Idle");
        SetBusy(false);
        LogLine("Settings: cpu_percent=" + settings.CpuPercent.ToString() + ", priority=" + settings.Priority.ToString());
        if (Program.StartFiles != null && Program.StartFiles.Length > 0)
        {
            AddFiles(Program.StartFiles);
            LogLine("Added " + jobs.Count.ToString() + " item(s) from Send To / command line.");
        }
    }

    void OnResizeForm(object sender, EventArgs e)
    {
        LayoutTopRight();
        LayoutDest();
        if (lst.Columns.Count >= 6)
        {
            int w = lst.ClientSize.Width;
            lst.Columns[0].Width = Math.Max(140, w - 450);
            lst.Columns[1].Width = 70;
            lst.Columns[2].Width = 80;
            lst.Columns[3].Width = 80;
            lst.Columns[4].Width = 90;
            lst.Columns[5].Width = 100;
        }
    }

    void OnCpuTick(object sender, EventArgs e)
    {
        int cpu = 0, ram = 0;
        try { if (cpuCounter != null) cpu = (int)Math.Max(0, Math.Min(100, cpuCounter.NextValue())); }
        catch { }
        try
        {
            MEMORYSTATUSEX ms = new MEMORYSTATUSEX();
            ms.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref ms)) ram = (int)ms.dwMemoryLoad;
        }
        catch { }
        cpuBar.Set(cpu, "CPU " + cpu.ToString() + "%");
        ramBar.Set(ram, "RAM " + ram.ToString() + "%");
    }

    void SetBusy(bool on)
    {
        busy = on;
        AllowDrop = !on;
        lst.AllowDrop = !on;
        cboPreset.Enabled = !on;
        cboDest.Enabled = !on;
        lnkDest.Enabled = !on;
        btnAdd.Enabled = !on;
        btnRemove.Enabled = !on;
        btnClear.Enabled = !on;
        btnStart.Enabled = !on;
        btnRestart.Enabled = !on;
        btnReload.Enabled = !on;
        btnPickDir.Enabled = (!on) && cboDest.SelectedIndex == 1;
        btnStop.Enabled = on;
        btnStop.Invalidate();
        btnAdd.Invalidate();
        btnRemove.Invalidate();
        btnClear.Invalidate();
        btnStart.Invalidate();
        btnRestart.Invalidate();
        btnReload.Invalidate();
        btnPickDir.Invalidate();
    }

    bool DetectCuda()
    {
        string ffmpeg = Path.Combine(assetsDir, "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) return false;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffmpeg;
            psi.Arguments = "-hide_banner -hwaccels";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            string all = (p.StandardOutput.ReadToEnd() + "\n" + p.StandardError.ReadToEnd()).ToLowerInvariant();
            p.WaitForExit(4000);
            string[] lines = all.Replace("\r", "").Split('\n');
            int i;
            for (i = 0; i < lines.Length; i++)
                if (lines[i].Trim() == "cuda") return true;
        }
        catch { }
        return false;
    }

    void EnsureSendToShortcut()
    {
        try
        {
            string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            if (sendTo == null || sendTo.Length == 0) return;
            string lnk = Path.Combine(sendTo, "EncodeKit.lnk");
            string target = Application.ExecutablePath;
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { appDir });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "EncodeKit" });
            string ico = Path.Combine(assetsDir, "app.ico");
            if (File.Exists(ico))
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { ico });
            else
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        catch (Exception ex) { LogLine("Send To shortcut: " + ex.Message); }
    }

    Label MakeLabel(string t, int x, int y, int w, int h, bool addToForm)
    {
        Label l = new Label();
        l.Text = t; l.Left = x; l.Top = y; l.Width = w; l.Height = h; l.ForeColor = Fg;
        if (addToForm) Controls.Add(l);
        return l;
    }

    void AddJobRow(Panel p, string key, Label val, ref int y)
    {
        Label k = new Label();
        k.Text = key; k.Left = 12; k.Top = y; k.Width = 90; k.Height = 22; k.ForeColor = Muted;
        val.SetBounds(110, y, 230, 22);
        val.ForeColor = Fg;
        val.TextAlign = ContentAlignment.MiddleRight;
        p.Controls.Add(k);
        p.Controls.Add(val);
        y += 28;
    }

    Panel CardPanel(int x, int y, int w, int h)
    {
        Panel p = new Panel();
        p.Left = x; p.Top = y; p.Width = w; p.Height = h; p.BackColor = Card;
        return p;
    }

    void StyleBtn(DarkButton b, string t, bool accent)
    {
        b.Text = t;
        b.FlatAppearance.BorderColor = accent ? Accent : Border;
        b.BackColor = accent ? Color.FromArgb(16, 78, 72) : Card;
        b.ForeColor = Fg;
        b.EnabledFore = Fg;
        b.DisabledFore = DisabledFg;
        b.Cursor = Cursors.Hand;
    }

    void LogLine(string s)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(LogLine), s); return; }
        log.AppendText(s + Environment.NewLine);
    }

    void UpdateFfmpegStatus()
    {
        string ffmpeg = Path.Combine(assetsDir, "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
        {
            lblFfmpeg.Text = "ffmpeg.exe missing";
            lblFfmpeg.ForeColor = Danger;
            tip.SetToolTip(lblFfmpeg, "Get Latest version of FFMpeg GPL Static from Github");
            return;
        }
        string ver = ReadFfmpegVersionLine(ffmpeg);
        if (ver.StartsWith("ffmpeg version ", StringComparison.OrdinalIgnoreCase))
            ver = "FFmpeg " + ver.Substring(15);
        int cut = ver.IndexOf(" Copyright");
        if (cut > 0) ver = ver.Substring(0, cut);
        lblFfmpeg.Text = ver.Trim().Length > 0 ? ver.Trim() : "FFmpeg found";
        lblFfmpeg.ForeColor = Accent;
        tip.SetToolTip(lblFfmpeg, "Get Latest version of FFMpeg GPL Static from Github");
    }

    static string ReadFfmpegVersionLine(string ffmpeg)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffmpeg;
            psi.Arguments = "-version";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            string s = p.StandardOutput.ReadLine();
            p.WaitForExit(2000);
            return s == null ? "" : s.Trim();
        }
        catch { return ""; }
    }

    string LastPresetPath() { return Path.Combine(assetsDir, "last_preset.txt"); }
    string DestPath() { return Path.Combine(assetsDir, "dest.txt"); }

    void SaveLastPreset()
    {
        try
        {
            if (cboPreset.SelectedItem == null) return;
            File.WriteAllText(LastPresetPath(), ((Preset)cboPreset.SelectedItem).Name);
        }
        catch { }
    }

    void SaveDest()
    {
        try { File.WriteAllText(DestPath(), cboDest.SelectedIndex.ToString() + Environment.NewLine + customDir); }
        catch { }
    }

    void LoadDest()
    {
        loadingDest = true;
        try
        {
            if (File.Exists(DestPath()))
            {
                string[] lines = File.ReadAllLines(DestPath());
                if (lines.Length > 0 && lines[0] == "1") cboDest.SelectedIndex = 1;
                else cboDest.SelectedIndex = 0;
                if (lines.Length > 1) customDir = lines[1].Trim();
            }
        }
        catch { }
        btnPickDir.Enabled = cboDest.SelectedIndex == 1;
        loadingDest = false;
    }

    void SelectLastPreset()
    {
        string want = "";
        try { if (File.Exists(LastPresetPath())) want = File.ReadAllText(LastPresetPath()).Trim(); }
        catch { }
        int i;
        if (want.Length > 0)
        {
            for (i = 0; i < cboPreset.Items.Count; i++)
            {
                if (string.Equals(((Preset)cboPreset.Items[i]).Name, want, StringComparison.OrdinalIgnoreCase))
                {
                    cboPreset.SelectedIndex = i;
                    return;
                }
            }
        }
        if (cboPreset.Items.Count > 0) cboPreset.SelectedIndex = 0;
    }

    void LoadPresets()
    {
        loadingPresets = true;
        presets.Clear();
        cboPreset.Items.Clear();
        string path = Path.Combine(assetsDir, "presets.ini");
        if (!File.Exists(path))
        {
            loadingPresets = false;
            LogLine("assets\\presets.ini not found.");
            return;
        }
        Preset cur = null;
        string[] lines = File.ReadAllLines(path);
        int i, hidden = 0;
        for (i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                cur = new Preset();
                cur.Name = line.Substring(1, line.Length - 2);
                presets.Add(cur);
                continue;
            }
            if (cur == null) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line.Substring(0, eq).Trim().ToLowerInvariant();
            string val = line.Substring(eq + 1).Trim();
            if (key == "pre") cur.Pre = val;
            else if (key == "args") cur.Args = val;
            else if (key == "suffix") cur.Suffix = val;
            else if (key == "ext") cur.Ext = val.Trim('.');
            else if (key == "note") cur.Note = val;
        }
        for (i = 0; i < presets.Count; i++)
        {
            if (!cudaOk && presets[i].NeedsCuda()) { hidden++; continue; }
            cboPreset.Items.Add(presets[i]);
        }
        SelectLastPreset();
        loadingPresets = false;
        if (cudaOk) LogLine("CUDA available. GPU presets shown.");
        else LogLine("CUDA not available. Hidden " + hidden.ToString() + " GPU preset(s).");
        LogLine("Loaded " + cboPreset.Items.Count.ToString() + " preset(s).");
        UpdatePresetNote();
    }

    void PickFiles()
    {
        OpenFileDialog d = new OpenFileDialog();
        d.Multiselect = true;
        d.Filter = "Media|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv;*.ts;*.mts;*.mp3;*.wav;*.aac|All|*.*";
        if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames);
        d.Dispose();
    }

    public void AddFiles(string[] files)
    {
        if (files == null) return;
        string ffprobe = Path.Combine(assetsDir, "ffprobe.exe");
        int i;
        for (i = 0; i < files.Length; i++)
        {
            string f = files[i];
            if (Directory.Exists(f)) { AddFiles(Directory.GetFiles(f)); continue; }
            if (!File.Exists(f) || AlreadyQueued(f)) continue;
            Job j = new Job();
            j.Path = f;
            j.Status = "Waiting";
            try
            {
                FileInfo fi = new FileInfo(f);
                j.SizeText = FormatSize(fi.Length);
                j.InputBytes = fi.Length;
            }
            catch { j.SizeText = "-"; }
            double dur = ProbeDuration(ffprobe, f);
            j.DurationText = dur > 0 ? FmtClock(dur) : "--:--:--";
            jobs.Add(j);
        }
        RefreshList();
    }

    bool AlreadyQueued(string path)
    {
        int i;
        for (i = 0; i < jobs.Count; i++)
            if (string.Equals(jobs[i].Path, path, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes.ToString() + " B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    string MakeOutputPath(Preset preset, string input)
    {
        string ext = preset.Ext;
        if (ext == null || ext == "*")
        {
            ext = Path.GetExtension(input);
            if (ext.StartsWith(".")) ext = ext.Substring(1);
            if (ext.Length == 0) ext = "mp4";
        }
        string file = Path.GetFileNameWithoutExtension(input) + preset.Suffix + "." + ext;
        if (cboDest.SelectedIndex == 1 && customDir != null && Directory.Exists(customDir))
            return Path.Combine(customDir, file);
        return Path.Combine(Path.GetDirectoryName(input), file);
    }

    void RefreshList()
    {
        lst.Items.Clear();
        int i;
        for (i = 0; i < jobs.Count; i++)
        {
            Job j = jobs[i];
            ListViewItem it = new ListViewItem(Path.GetFileName(j.Path));
            it.SubItems.Add(j.SizeText);
            it.SubItems.Add(j.DurationText);
            it.SubItems.Add(j.Status);
            it.SubItems.Add("Open");
            it.SubItems.Add((j.Status == "Done" && j.ConvertedText != null && j.ConvertedText.Length > 0) ? j.ConvertedText : "");
            if (j.Status == "Encoding" || j.Status == "Done") it.ForeColor = Accent;
            else if (j.Status == "Error" || j.Status == "Stopped") it.ForeColor = Danger;
            lst.Items.Add(it);
        }
    }

    delegate void SetJobHandler(string file, string output, string preset, string dur, string elapsed, string eta, string speed);

    void SetJob(string file, string output, string preset, string dur, string elapsed, string eta, string speed)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new SetJobHandler(SetJob), new object[] { file, output, preset, dur, elapsed, eta, speed });
            return;
        }
        lblFile.Text = file;
        lblOut.Text = output;
        lblPreset.Text = preset;
        lblDur.Text = dur;
        lblElapsed.Text = elapsed;
        lblEta.Text = eta;
        lblSpeed.Text = speed;
    }

    delegate void SetBarHandler(int pct, string text);

    void SetBar(int pct, string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new SetBarHandler(SetBar), new object[] { pct, text });
            return;
        }
        if (pct == lastPct && text == lastBarText) return;
        lastPct = pct;
        lastBarText = text;
        bar.Set(pct, text);
    }

    void RunQueue(bool restart)
    {
        if (cts != null || busy) return;
        LoadSettings();
        if (cboPreset.SelectedItem == null) { LogLine("No preset selected."); return; }
        if (cboDest.SelectedIndex == 1 && (customDir == null || !Directory.Exists(customDir)))
        {
            LogLine("Choose a custom output directory first.");
            return;
        }
        string ffmpeg = Path.Combine(assetsDir, "ffmpeg.exe");
        string ffprobe = Path.Combine(assetsDir, "ffprobe.exe");
        if (!File.Exists(ffmpeg)) { LogLine("ffmpeg.exe missing in assets"); return; }
        Preset preset = (Preset)cboPreset.SelectedItem;
        SaveLastPreset();
        cts = new CancellationTokenSource();
        SetBusy(true);
        CancellationToken token = cts.Token;
        Thread t = new Thread(new ThreadStart(delegate
        {
            try
            {
                int i;
                for (i = 0; i < jobs.Count; i++)
                {
                    if (token.IsCancellationRequested) break;
                    Job job = jobs[i];
                    if (!restart && job.Status == "Done") continue;
                    if (restart) { job.ConvertedText = ""; job.OutputPath = ""; }
                    job.Status = "Encoding";
                    Invoke(new MethodInvoker(RefreshList));
                    bool ok = EncodeOne(ffmpeg, ffprobe, preset, job, token);
                    if (token.IsCancellationRequested) job.Status = "Stopped";
                    else if (ok) job.Status = "Done";
                    else job.Status = "Error";
                    Invoke(new MethodInvoker(RefreshList));
                }
            }
            finally { Invoke(new MethodInvoker(FinishQueue)); }
        }));
        t.IsBackground = true;
        t.Start();
    }

    void FinishQueue()
    {
        if (cts != null) { cts.Dispose(); cts = null; }
        currentProc = null;
        SetBusy(false);
        SetBar(0, "Idle");
    }

    bool EncodeOne(string ffmpeg, string ffprobe, Preset preset, Job job, CancellationToken token)
    {
        string input = job.Path;
        string output = MakeOutputPath(preset, input);
        job.OutputPath = output;
        // Sequence outputs like Clip-frames\%04d.jpeg need the folder created first.
        string outDir = Path.GetDirectoryName(output);
        if (outDir != null && outDir.Length > 0 && !Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);
        double duration = ProbeDuration(ffprobe, input);
        SetJob(Path.GetFileName(input), Path.GetFileName(output), preset.Name,
            FmtClock(duration), "0 seconds", "--", "-");

        string extra = preset.Args;
        if (!preset.NeedsCuda()) extra = BuildCpuArgs(extra, CpuThreadCount());

        StringBuilder args = new StringBuilder();
        args.Append("-y -hide_banner -nostats -progress pipe:1 ");
        if (preset.Pre != null && preset.Pre.Trim().Length > 0) args.Append(preset.Pre).Append(' ');
        args.Append("-i ").Append(Q(input)).Append(' ');
        args.Append(extra).Append(' ');
        args.Append(Q(output));

        LogLine("> ffmpeg " + args.ToString());
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = ffmpeg;
        psi.Arguments = args.ToString();
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = assetsDir;

        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            currentProc = new Process();
            currentProc.StartInfo = psi;
            currentProc.ErrorDataReceived += OnProcError;
            currentProc.Start();
            try { currentProc.PriorityClass = settings.Priority; }
            catch { }
            currentProc.BeginErrorReadLine();

            double lastOut = 0;
            double lastSpeed = 1;
            string line;
            while ((line = currentProc.StandardOutput.ReadLine()) != null)
            {
                if (token.IsCancellationRequested) break;
                if (line.StartsWith("out_time_ms="))
                {
                    long ms;
                    if (long.TryParse(line.Substring(12), out ms) && ms > 0) lastOut = ms / 1000000.0;
                }
                else if (line.StartsWith("out_time_us="))
                {
                    long us;
                    if (long.TryParse(line.Substring(12), out us) && us > 0) lastOut = us / 1000000.0;
                }
                else if (line.StartsWith("out_time="))
                {
                    TimeSpan ts;
                    if (TimeSpan.TryParse(line.Substring(9), out ts)) lastOut = ts.TotalSeconds;
                }
                else if (line.StartsWith("speed="))
                {
                    string s = line.Substring(6).Trim().TrimEnd('x');
                    double sp;
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out sp) && sp > 0)
                        lastSpeed = sp;
                }
                else continue;

                int pct = 0;
                string eta = "--";
                if (duration > 0.5)
                {
                    pct = (int)Math.Max(0, Math.Min(100, lastOut * 100.0 / duration));
                    double remain = Math.Max(0, duration - lastOut);
                    if (lastSpeed > 0.01) eta = FmtHuman(remain / lastSpeed);
                }
                SetJob(Path.GetFileName(input), Path.GetFileName(output), preset.Name,
                    FmtClock(duration), FmtHuman(sw.Elapsed.TotalSeconds), eta,
                    lastSpeed.ToString("0.00", CultureInfo.InvariantCulture) + "x");
                SetBar(pct, pct.ToString() + "%  |  ETA " + eta);
            }

            currentProc.WaitForExit();
            if (token.IsCancellationRequested) return false;
            if (currentProc.ExitCode != 0)
            {
                LogLine("ffmpeg exit " + currentProc.ExitCode.ToString());
                return false;
            }
            SetBar(100, "100%  |  done");
            long outBytes = MeasureOutputBytes(output);
            job.ConvertedText = SizeDeltaText(job.InputBytes, outBytes);
            job.OutputSmaller = outBytes <= job.InputBytes || job.InputBytes == 0;
            return true;
        }
        catch (Exception ex)
        {
            LogLine(ex.Message);
            return false;
        }
    }

    void OnProcError(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null && e.Data.Length > 0) LogLine(e.Data);
    }

    static double ProbeDuration(string ffprobe, string file)
    {
        if (!File.Exists(ffprobe)) return 0;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffprobe;
            psi.Arguments = "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 " + Q(file);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            string s = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        }
        catch { }
        return 0;
    }

    static string Q(string path) { return "\"" + path + "\""; }

    static string Unit(int n, string one, string many)
    {
        return n.ToString() + " " + (n == 1 ? one : many);
    }

    static string FmtHuman(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--";
        int total = (int)Math.Round(seconds);
        if (total < 0) total = 0;
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        if (h > 0)
        {
            if (m > 0) return Unit(h, "hour", "hours") + " and " + Unit(m, "minute", "minutes");
            return Unit(h, "hour", "hours");
        }
        if (m > 0)
        {
            if (s > 0) return Unit(m, "minute", "minutes") + " and " + Unit(s, "second", "seconds");
            return Unit(m, "minute", "minutes");
        }
        return Unit(s, "second", "seconds");
    }

    static string FmtClock(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--:--:--";
        int total = (int)Math.Round(seconds);
        if (total < 0) total = 0;
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        return string.Format("{0:00}:{1:00}:{2:00}", h, m, s);
    }
}
