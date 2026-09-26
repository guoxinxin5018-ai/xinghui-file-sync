using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        try { SetProcessDPIAware(); }
        catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal sealed class CopyJob
{
    public string Source;
    public string Target;
    public string Relative;
    public string Direction;
    public long Size;
}

internal sealed class SyncResult
{
    public int Copied;
    public int Total;
    public long Bytes;
}

[Serializable]
public sealed class SyncTaskConfig
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string FolderA { get; set; }
    public string FolderB { get; set; }
    public string Mode { get; set; }
    public decimal Interval { get; set; }
    public string Unit { get; set; }
    public bool Enabled { get; set; }
    public DateTime LastRun { get; set; }
    public string LastResult { get; set; }
    [XmlIgnore] public DateTime NextRun { get; set; }
    [XmlIgnore] public int ProgressPercent { get; set; }
    [XmlIgnore] public string RuntimeStatus { get; set; }

    public SyncTaskConfig()
    {
        Id = Guid.NewGuid().ToString("N");
        Name = "同步任务";
        FolderA = "";
        FolderB = "";
        Mode = "双向同步（A ↔ B）";
        Interval = 60;
        Unit = "秒";
        LastResult = "尚未运行";
        RuntimeStatus = "未启动";
    }

    public override string ToString() { return Name; }
}

[Serializable]
public sealed class SyncTaskStore
{
    public List<SyncTaskConfig> Tasks { get; set; }
    public SyncTaskStore() { Tasks = new List<SyncTaskConfig>(); }
}

internal sealed class TaskSettingsForm : Form
{
    private readonly TextBox nameBox = new TextBox();
    private readonly TextBox sourceBox = new TextBox();
    private readonly TextBox targetBox = new TextBox();
    private readonly ComboBox modeBox = new ComboBox();
    private readonly NumericUpDown intervalBox = new NumericUpDown();
    private readonly ComboBox unitBox = new ComboBox();
    private readonly Label hint = new Label();
    private readonly SyncTaskConfig original;

    public TaskSettingsForm(SyncTaskConfig task, bool isNew)
    {
        original = task;
        Text = isNew ? "新建同步任务" : "编辑同步任务";
        Width = 720;
        Height = 430;
        MinimumSize = new Size(650, 410);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var title = new Label { Text = Text, Font = new Font(Font.FontFamily, 16F, FontStyle.Bold), AutoSize = true, Left = 24, Top = 20 };
        Controls.Add(title);
        AddRow("任务名称", nameBox, 72, false);
        AddRow("源文件夹 A", sourceBox, 116, true);
        AddRow("目标文件夹 B", targetBox, 160, true);

        AddLabel("传输模式", 210);
        modeBox.SetBounds(130, 205, 300, 30);
        modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        modeBox.Items.AddRange(new object[] { "双向同步（A ↔ B）", "单向上传（A → B）", "单向下载（B → A）", "单向镜像备份（A ⇒ B）" });
        modeBox.SelectedIndexChanged += delegate { UpdateHint(); };
        Controls.Add(modeBox);

        AddLabel("同步间隔", 254);
        intervalBox.SetBounds(130, 249, 110, 30);
        intervalBox.Minimum = 1;
        intervalBox.Maximum = 999999;
        Controls.Add(intervalBox);
        unitBox.SetBounds(250, 249, 90, 30);
        unitBox.DropDownStyle = ComboBoxStyle.DropDownList;
        unitBox.Items.AddRange(new object[] { "秒", "分钟", "小时" });
        Controls.Add(unitBox);

        hint.SetBounds(130, 290, 520, 42);
        hint.ForeColor = Color.FromArgb(90, 100, 115);
        Controls.Add(hint);

        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 96, Height = 34, Left = 486, Top = 340 };
        var ok = new Button { Text = "保存设置", Width = 104, Height = 34, Left = 592, Top = 340 };
        ok.Click += delegate { SaveAndClose(); };
        Controls.Add(cancel);
        Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        nameBox.Text = task.Name;
        sourceBox.Text = task.FolderA;
        targetBox.Text = task.FolderB;
        modeBox.Text = task.Mode;
        if (modeBox.SelectedIndex < 0) modeBox.SelectedIndex = 0;
        intervalBox.Value = Math.Max(intervalBox.Minimum, Math.Min(intervalBox.Maximum, task.Interval));
        unitBox.Text = task.Unit;
        if (unitBox.SelectedIndex < 0) unitBox.SelectedIndex = 0;
        UpdateHint();
    }

    private void AddLabel(string text, int top)
    {
        Controls.Add(new Label { Text = text, AutoSize = true, Left = 24, Top = top + 5 });
    }

    private void AddRow(string label, TextBox box, int top, bool browse)
    {
        AddLabel(label, top);
        box.SetBounds(130, top, browse ? 470 : 566, 30);
        Controls.Add(box);
        if (!browse) return;
        var button = new Button { Text = "选择…", Left = 610, Top = top - 1, Width = 86, Height = 31 };
        button.Click += delegate
        {
            using (var dialog = new FolderBrowserDialog())
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
        };
        Controls.Add(button);
    }

    private void UpdateHint()
    {
        if (modeBox.Text.StartsWith("双向")) hint.Text = "双方新增和修改互相推送，同名文件以修改时间较新的版本为准。";
        else if (modeBox.Text.StartsWith("单向镜像")) hint.Text = "A 完整镜像到 B；B 中多出的内容会进入“星汇删除备份”。";
        else if (modeBox.Text.StartsWith("单向上传")) hint.Text = "只允许 A 向 B 复制新增和更新内容，不反向覆盖。";
        else hint.Text = "只允许 B 向 A 复制新增和更新内容，不反向覆盖。";
    }

    private void SaveAndClose()
    {
        string a, b;
        try
        {
            a = Path.GetFullPath(sourceBox.Text.Trim()).TrimEnd('\\');
            b = Path.GetFullPath(targetBox.Text.Trim()).TrimEnd('\\');
            if (!Directory.Exists(a) || !Directory.Exists(b) || String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) throw new Exception();
            if ((a + "\\").StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase) || (b + "\\").StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase)) throw new Exception();
        }
        catch
        {
            MessageBox.Show(this, "请选择两个存在且互不包含的文件夹。", "设置有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        original.Name = String.IsNullOrWhiteSpace(nameBox.Text) ? "未命名任务" : nameBox.Text.Trim();
        original.FolderA = a;
        original.FolderB = b;
        original.Mode = modeBox.Text;
        original.Interval = intervalBox.Value;
        original.Unit = unitBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class MainForm : Form
{
    private delegate CopyProgressResult CopyProgressRoutine(
        long totalFileSize, long totalBytesTransferred, long streamSize,
        long streamBytesTransferred, uint streamNumber, CopyProgressCallbackReason reason,
        IntPtr sourceFile, IntPtr destinationFile, IntPtr data);

    private enum CopyProgressResult : uint { Continue = 0, Cancel = 1, Stop = 2, Quiet = 3 }
    private enum CopyProgressCallbackReason : uint { ChunkFinished = 0, StreamSwitch = 1 }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CopyFileEx(
        string existingFileName, string newFileName, CopyProgressRoutine progressRoutine,
        IntPtr data, ref int cancel, uint copyFlags);

    private readonly DataGridView taskGrid = new DataGridView();
    private readonly Label totalSummary = new Label();
    private readonly Label activeSummary = new Label();
    private readonly Label stoppedSummary = new Label();
    private readonly TextBox searchBox = new TextBox();
    private readonly ComboBox statusFilter = new ComboBox();
    private readonly CheckBox startup = new CheckBox();
    private readonly Button start = new Button();
    private readonly Button stop = new Button();
    private readonly Button once = new Button();
    private readonly Button addTask = new Button();
    private readonly Button saveTask = new Button();
    private readonly Button deleteTask = new Button();
    private readonly Label status = new Label();
    private readonly TextBox log = new TextBox();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly NotifyIcon trayIcon = new NotifyIcon();
    private bool syncing;
    private bool exitRequested;
    private bool trayHintShown;
    private long lastProgressTick;
    private readonly object progressLock = new object();
    private readonly List<SyncTaskConfig> tasks = new List<SyncTaskConfig>();
    private SyncTaskConfig selectedTask;
    private bool changingSelection;
    private string currentTaskName = "";
    private string currentTaskId = "";

    private string ConfigPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "星汇");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "tasks.xml");
        }
    }

    private string LegacyConfigPath
    {
        get { return Path.Combine(Path.GetDirectoryName(ConfigPath), "settings.txt"); }
    }

    public MainForm()
    {
        Text = "星汇";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Width = 1220;
        Height = 760;
        MinimumSize = new Size(1040, 650);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(246, 248, 252);

        var title = new Label { Text = "同步任务管理", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Left = 24, Top = 18 };
        var subtitle = new Label { Text = "统一查看、运行和管理所有文件同步任务", ForeColor = Color.FromArgb(90, 100, 115), AutoSize = true, Left = 26, Top = 58 };
        Controls.Add(title);
        Controls.Add(subtitle);

        ConfigureSummary(totalSummary, 24, 86, Color.FromArgb(229, 240, 255));
        ConfigureSummary(activeSummary, 218, 86, Color.FromArgb(229, 247, 238));
        ConfigureSummary(stoppedSummary, 412, 86, Color.FromArgb(239, 241, 245));
        Controls.Add(totalSummary);
        Controls.Add(activeSummary);
        Controls.Add(stoppedSummary);

        statusFilter.SetBounds(624, 94, 120, 32);
        statusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        statusFilter.Items.AddRange(new object[] { "全部任务", "已启动", "未启动" });
        statusFilter.SelectedIndex = 0;
        statusFilter.SelectedIndexChanged += delegate { RefreshTaskLists(); };
        Controls.Add(statusFilter);
        searchBox.SetBounds(756, 94, 220, 32);
        searchBox.TextChanged += delegate { RefreshTaskLists(); };
        Controls.Add(searchBox);
        var searchHint = new Label { Text = "搜索任务名称或路径", ForeColor = Color.Gray, AutoSize = true, Left = 762, Top = 73 };
        Controls.Add(searchHint);

        addTask.Text = "＋ 新建任务";
        addTask.SetBounds(ClientSize.Width - 142, 22, 118, 38);
        addTask.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addTask.BackColor = Color.FromArgb(39, 111, 225);
        addTask.ForeColor = Color.White;
        addTask.FlatStyle = FlatStyle.Flat;
        addTask.Click += delegate { AddNewTask(); };
        Controls.Add(addTask);

        start.Text = "启动任务";
        stop.Text = "停止任务";
        once.Text = "立即同步";
        saveTask.Text = "编辑设置";
        deleteTask.Text = "删除任务";
        Button[] tools = { start, stop, once, saveTask, deleteTask };
        for (int i = 0; i < tools.Length; i++)
        {
            tools[i].SetBounds(24 + i * 108, 144, 98, 34);
            tools[i].FlatStyle = FlatStyle.Flat;
            tools[i].BackColor = Color.White;
            Controls.Add(tools[i]);
        }
        start.Click += delegate { StartAuto(); };
        stop.Click += delegate { StopAuto(); };
        once.Click += async delegate { await SyncNow(); };
        saveTask.Click += delegate { SaveSelectedTask(); };
        deleteTask.Click += delegate { DeleteSelectedTask(); };

        startup.Text = "登录 Windows 后自动运行";
        startup.AutoSize = true;
        startup.SetBounds(ClientSize.Width - 220, 151, 196, 26);
        startup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        startup.CheckedChanged += delegate { ChangeStartup(); };
        Controls.Add(startup);

        ConfigureTaskGrid();
        taskGrid.SetBounds(24, 190, ClientSize.Width - 48, ClientSize.Height - 390);
        taskGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(taskGrid);

        var logLabel = new Label { Text = "运行日志", Font = new Font(Font.FontFamily, 10F, FontStyle.Bold), AutoSize = true, Left = 24, Top = ClientSize.Height - 188, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        Controls.Add(logLabel);
        status.Text = "请选择一个任务";
        status.AutoSize = false;
        status.SetBounds(ClientSize.Width - 550, ClientSize.Height - 194, 526, 30);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        status.TextAlign = ContentAlignment.MiddleRight;
        status.AutoEllipsis = true;
        Controls.Add(status);
        log.SetBounds(24, ClientSize.Height - 158, ClientSize.Width - 48, 132);
        log.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.BackColor = Color.White;
        Controls.Add(log);

        timer.Interval = 1000;
        timer.Tick += async delegate { await SchedulerTick(); };
        ConfigureTrayIcon();
        FormClosing += MainFormClosing;
        Resize += delegate
        {
            if (WindowState == FormWindowState.Minimized) HideToTray();
        };
        LoadConfig();
        timer.Start();
    }

    private void ConfigureSummary(Label label, int left, int top, Color color)
    {
        label.SetBounds(left, top, 178, 48);
        label.BackColor = color;
        label.Padding = new Padding(14, 0, 0, 0);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
    }

    private void ConfigureTaskGrid()
    {
        taskGrid.AllowUserToAddRows = false;
        taskGrid.AllowUserToDeleteRows = false;
        taskGrid.AllowUserToResizeRows = false;
        taskGrid.AutoGenerateColumns = false;
        taskGrid.BackgroundColor = Color.White;
        taskGrid.BorderStyle = BorderStyle.FixedSingle;
        taskGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        taskGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        taskGrid.ColumnHeadersHeight = 40;
        taskGrid.EnableHeadersVisualStyles = false;
        taskGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 251);
        taskGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        taskGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 235, 255);
        taskGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 35, 50);
        taskGrid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        taskGrid.RowHeadersVisible = false;
        taskGrid.RowTemplate.Height = 38;
        taskGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        taskGrid.MultiSelect = false;
        taskGrid.ReadOnly = true;
        AddGridColumn("任务名称", 145);
        AddGridColumn("状态", 78);
        AddGridColumn("同步方向", 125);
        AddGridColumn("源文件夹", 190);
        AddGridColumn("目标文件夹", 190);
        AddGridColumn("进度", 70);
        AddGridColumn("同步间隔", 88);
        AddGridColumn("上次同步", 125);
        AddGridColumn("下次运行", 125);
        var resultColumn = AddGridColumn("上次结果", 130);
        resultColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        taskGrid.SelectionChanged += delegate
        {
            if (changingSelection || taskGrid.CurrentRow == null) return;
            selectedTask = taskGrid.CurrentRow.Tag as SyncTaskConfig;
            UpdateTaskButtons();
        };
        taskGrid.CellDoubleClick += delegate { SaveSelectedTask(); };
    }

    private DataGridViewTextBoxColumn AddGridColumn(string title, int width)
    {
        var column = new DataGridViewTextBoxColumn { HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
        taskGrid.Columns.Add(column);
        return column;
    }

    private void AddNewTask()
    {
        var task = new SyncTaskConfig { Name = "同步任务 " + (tasks.Count + 1) };
        using (var dialog = new TaskSettingsForm(task, true))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
        }
        tasks.Add(task); selectedTask = task;
        SaveConfig(); RefreshTaskLists(); UpdateTaskButtons();
    }

    private void SaveSelectedTask()
    {
        if (selectedTask == null) return;
        using (var dialog = new TaskSettingsForm(selectedTask, false))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
        }
        if (selectedTask.Enabled) selectedTask.NextRun = DateTime.Now.AddMilliseconds(TaskIntervalMilliseconds(selectedTask));
        SaveConfig();
        RefreshTaskLists();
        UpdateTaskButtons();
        status.Text = "任务已保存";
    }

    private void DeleteSelectedTask()
    {
        if (selectedTask == null) return;
        if (syncing && currentTaskId == selectedTask.Id)
        {
            MessageBox.Show(this, "该任务正在同步，请等待本轮完成后再删除。", "无法删除");
            return;
        }
        if (MessageBox.Show(this, "确定删除任务“" + selectedTask.Name + "”吗？", "删除任务", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        tasks.Remove(selectedTask);
        selectedTask = tasks.FirstOrDefault();
        RefreshTaskLists();
        UpdateTaskButtons();
        SaveConfig();
    }

    private void RefreshTaskLists()
    {
        string selectedId = selectedTask == null ? null : selectedTask.Id;
        changingSelection = true;
        taskGrid.Rows.Clear();
        string query = searchBox.Text.Trim();
        foreach (SyncTaskConfig task in tasks.OrderByDescending(t => t.Enabled).ThenBy(t => t.Name))
        {
            if (statusFilter.SelectedIndex == 1 && !task.Enabled) continue;
            if (statusFilter.SelectedIndex == 2 && task.Enabled) continue;
            if (query.Length > 0 && !(task.Name + " " + task.FolderA + " " + task.FolderB).Contains(query)) continue;
            int index = taskGrid.Rows.Add();
            DataGridViewRow row = taskGrid.Rows[index];
            row.Tag = task;
            UpdateTaskRow(row, task);
            if (task.Id == selectedId) row.Selected = true;
        }
        changingSelection = false;
        if (taskGrid.SelectedRows.Count == 0 && taskGrid.Rows.Count > 0) taskGrid.Rows[0].Selected = true;
        if (taskGrid.SelectedRows.Count > 0) selectedTask = taskGrid.SelectedRows[0].Tag as SyncTaskConfig;
        totalSummary.Text = "全部任务    " + tasks.Count;
        activeSummary.Text = "已启动       " + tasks.Count(t => t.Enabled);
        stoppedSummary.Text = "未启动       " + tasks.Count(t => !t.Enabled);
    }

    private void UpdateTaskRows()
    {
        foreach (DataGridViewRow row in taskGrid.Rows)
        {
            SyncTaskConfig task = row.Tag as SyncTaskConfig;
            if (task != null) UpdateTaskRow(row, task);
        }
        totalSummary.Text = "全部任务    " + tasks.Count;
        activeSummary.Text = "已启动       " + tasks.Count(t => t.Enabled);
        stoppedSummary.Text = "未启动       " + tasks.Count(t => !t.Enabled);
    }

    private void UpdateTaskRow(DataGridViewRow row, SyncTaskConfig task)
    {
        bool running = syncing && task.Id == currentTaskId;
        string state = running ? "同步中" : task.Enabled ? "已启动" : "未启动";
        string progress = running ? task.ProgressPercent + "%" : (task.LastRun == DateTime.MinValue ? "—" : task.ProgressPercent + "%");
        string next = running ? "本轮同步中" : task.Enabled ? (task.NextRun <= DateTime.Now ? "即将运行" : FormatDuration(task.NextRun - DateTime.Now) + "后") : "—";
        row.SetValues(task.Name, state, ModeDisplay(task.Mode), task.FolderA, task.FolderB, progress,
            task.Interval.ToString("0") + task.Unit, task.LastRun == DateTime.MinValue ? "—" : task.LastRun.ToString("MM-dd HH:mm:ss"), next, task.LastResult);
        row.Cells[3].ToolTipText = task.FolderA; row.Cells[4].ToolTipText = task.FolderB;
        row.Cells[1].Style.ForeColor = running ? Color.FromArgb(27, 105, 210) : task.Enabled ? Color.FromArgb(24, 139, 82) : Color.FromArgb(115, 122, 132);
    }

    private static string ModeDisplay(string value)
    {
        if (value == null) return "双向同步";
        if (value.StartsWith("单向镜像")) return "镜像备份 A→B";
        if (value.StartsWith("单向上传") || value == "A→B") return "单向上传 A→B";
        if (value.StartsWith("单向下载") || value == "B→A") return "单向下载 B→A";
        return "双向同步";
    }

    private void UpdateTaskButtons()
    {
        bool exists = selectedTask != null;
        saveTask.Enabled = deleteTask.Enabled = start.Enabled = once.Enabled = exists;
        stop.Enabled = exists && selectedTask.Enabled;
        start.Enabled = exists && !selectedTask.Enabled;
        if (syncing) once.Enabled = false;
    }

    private void ConfigureTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, delegate { RestoreFromTray(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("开始自动同步", null, delegate { RestoreFromTray(); StartAuto(); });
        menu.Items.Add("立即同步一次", null, async delegate { await SyncNow(); });
        menu.Items.Add("停止同步", null, delegate { StopAuto(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("完全退出", null, delegate { ExitApplication(); });

        trayIcon.Text = "星汇 - 文件夹同步";
        trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        trayIcon.Visible = true;
    }

    private void MainFormClosing(object sender, FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        timer.Stop();
        SaveConfig();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (!trayHintShown)
        {
            trayIcon.BalloonTipTitle = "星汇正在后台运行";
            trayIcon.BalloonTipText = "同步任务会继续运行。双击托盘图标可恢复窗口，右键可完全退出。";
            trayIcon.ShowBalloonTip(3000);
            trayHintShown = true;
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        exitRequested = true;
        Close();
    }

    private void StartAuto()
    {
        if (selectedTask == null || !ValidateTask(selectedTask, true)) return;
        selectedTask.Enabled = true;
        selectedTask.RuntimeStatus = "等待运行";
        selectedTask.NextRun = DateTime.Now;
        SaveConfig();
        RefreshTaskLists();
        UpdateTaskButtons();
        status.Text = "任务已启动，准备同步";
        SyncNow();
    }

    private void StopAuto()
    {
        if (selectedTask == null) return;
        selectedTask.Enabled = false;
        selectedTask.RuntimeStatus = "未启动";
        SaveConfig();
        RefreshTaskLists();
        UpdateTaskButtons();
        status.Text = syncing && currentTaskId == selectedTask.Id ? "本轮完成后停止" : "任务已停止";
    }

    private async Task SyncNow()
    {
        if (selectedTask == null || syncing || !ValidateTask(selectedTask, true)) return;
        SaveConfig();
        await RunTask(selectedTask);
    }

    private async Task SchedulerTick()
    {
        if (syncing) { UpdateTaskRows(); return; }
        SyncTaskConfig due = tasks.Where(t => t.Enabled && t.NextRun <= DateTime.Now).OrderBy(t => t.NextRun).FirstOrDefault();
        if (due != null)
        {
            if (!ValidateTask(due, false))
            {
                due.Enabled = false;
                AppendLog("[" + due.Name + "] 路径无效，任务已自动移入未启动任务");
                SaveConfig();
                RefreshTaskLists();
                UpdateTaskButtons();
                return;
            }
            await RunTask(due);
            return;
        }
        if (selectedTask != null && selectedTask.Enabled)
        {
            TimeSpan remaining = selectedTask.NextRun - DateTime.Now;
            status.Text = selectedTask.Name + "｜" + FormatDuration(remaining) + "后再检查";
        }
        else if (selectedTask != null) status.Text = selectedTask.Name + "｜未启动";
        UpdateTaskRows();
    }

    private async Task RunTask(SyncTaskConfig task)
    {
        if (syncing || !ValidateTask(task, true)) return;
        syncing = true;
        currentTaskName = task.Name;
        currentTaskId = task.Id;
        task.RuntimeStatus = "正在扫描";
        task.ProgressPercent = 0;
        once.Enabled = false;
        status.Text = "正在扫描文件…";
        AppendLog("[" + task.Name + "] 开始扫描并同步");
        UpdateTaskRows();
        try
        {
            SyncResult result = await Task.Run(() => SyncFolders(task.FolderA, task.FolderB, task.Mode));
            AppendLog("[" + task.Name + "] 完成：需同步 " + result.Total + " 个，已复制 " + result.Copied + " 个，共 " + FormatBytes(result.Bytes));
            status.Text = task.Name + "｜同步完成";
            task.ProgressPercent = 100;
            task.LastResult = "成功，复制 " + result.Copied + " 个";
        }
        catch (Exception ex)
        {
            AppendLog("[" + task.Name + "] 同步失败：" + ex.Message);
            status.Text = task.Name + "｜同步失败";
            task.LastResult = "失败：" + ex.Message;
        }
        finally
        {
            task.LastRun = DateTime.Now;
            syncing = false;
            currentTaskName = "";
            currentTaskId = "";
            once.Enabled = true;
            if (task.Enabled)
            {
                int wait = TaskIntervalMilliseconds(task);
                task.NextRun = DateTime.Now.AddMilliseconds(wait);
                task.RuntimeStatus = "等待运行";
                status.Text = task.Name + "｜本轮完成，" + FormatDuration(TimeSpan.FromMilliseconds(wait)) + "后再检查";
            }
            else task.RuntimeStatus = "未启动";
            SaveConfig();
            RefreshTaskLists();
            UpdateTaskButtons();
        }
    }

    private static int TaskIntervalMilliseconds(SyncTaskConfig task)
    {
        decimal multiplier = task.Unit == "小时" ? 3600000M : task.Unit == "分钟" ? 60000M : 1000M;
        return (int)Math.Min(task.Interval * multiplier, Int32.MaxValue);
    }

    private bool ValidateTask(SyncTaskConfig task, bool showMessage)
    {
        try
        {
            string a = Path.GetFullPath(task.FolderA.Trim()).TrimEnd('\\');
            string b = Path.GetFullPath(task.FolderB.Trim()).TrimEnd('\\');
            if (!Directory.Exists(a) || !Directory.Exists(b) || String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) throw new Exception();
            if ((a + "\\").StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase) || (b + "\\").StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase)) throw new Exception();
            return true;
        }
        catch
        {
            if (showMessage) MessageBox.Show(this, "任务“" + task.Name + "”的两个文件夹无效或互相包含。", "设置有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private SyncResult SyncFolders(string a, string b, string selectedMode)
    {
        if (selectedMode == "A→B" || selectedMode.StartsWith("单向上传")) return SyncOneWay(a, b);
        if (selectedMode == "B→A" || selectedMode.StartsWith("单向下载")) return SyncOneWay(b, a);
        if (selectedMode.StartsWith("单向镜像")) return SyncMirror(a, b);

        var filesA = MapFiles(a);
        var filesB = MapFiles(b);
        EnsureTwoWayDirectories(a, b);
        var all = new HashSet<string>(filesA.Keys, StringComparer.OrdinalIgnoreCase);
        all.UnionWith(filesB.Keys);
        var jobs = new List<CopyJob>();
        foreach (string relative in all.OrderBy(x => x))
        {
            string pa, pb;
            bool hasA = filesA.TryGetValue(relative, out pa);
            bool hasB = filesB.TryGetValue(relative, out pb);
            if (!hasA) AddJob(jobs, pb, Path.Combine(a, relative), relative, "B → A");
            else if (!hasB) AddJob(jobs, pa, Path.Combine(b, relative), relative, "A → B");
            else
            {
                var ia = new FileInfo(pa);
                var ib = new FileInfo(pb);
                if (ia.Length == ib.Length && Math.Abs((ia.LastWriteTimeUtc - ib.LastWriteTimeUtc).TotalMilliseconds) <= 1) continue;
                if (ia.LastWriteTimeUtc >= ib.LastWriteTimeUtc) AddJob(jobs, pa, pb, relative, "A → B（A 较新）");
                else AddJob(jobs, pb, pa, relative, "B → A（B 较新）");
            }
        }
        return ExecuteJobs(jobs);
    }

    private SyncResult SyncMirror(string source, string target)
    {
        SyncResult result = SyncOneWay(source, target);
        var sourceFiles = MapFiles(source);
        var targetFiles = MapFiles(target);
        string backupRoot = Path.Combine(
            Directory.GetParent(target.TrimEnd('\\')).FullName,
            Path.GetFileName(target.TrimEnd('\\')) + "_星汇删除备份",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        foreach (var item in targetFiles)
        {
            if (sourceFiles.ContainsKey(item.Key)) continue;
            try
            {
                string backup = Path.Combine(backupRoot, item.Key);
                string backupDirectory = Path.GetDirectoryName(backup);
                Directory.CreateDirectory(backupDirectory);
                if (File.Exists(backup)) backup += "." + DateTime.Now.Ticks;
                File.Move(item.Value, backup);
                LogFromWorker("镜像移除（已备份）：" + item.Key);
            }
            catch (Exception ex) { LogFromWorker("镜像移除失败：" + item.Key + "（" + ex.Message + "）"); }
        }
        RemoveExtraDirectories(source, target);
        return result;
    }

    private static void RemoveExtraDirectories(string source, string target)
    {
        var sourceDirectories = new HashSet<string>(MapDirectories(source), StringComparer.OrdinalIgnoreCase);
        foreach (string relative in MapDirectories(target).OrderByDescending(x => x.Length))
        {
            try
            {
                if (sourceDirectories.Contains(relative)) continue;
                string directory = Path.Combine(target, relative);
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch { }
        }
    }

    private SyncResult SyncOneWay(string source, string target)
    {
        EnsureOneWayDirectories(source, target);
        var jobs = new List<CopyJob>();
        foreach (var item in MapFiles(source))
        {
            string destination = Path.Combine(target, item.Key);
            var sourceInfo = new FileInfo(item.Value);
            var targetInfo = new FileInfo(destination);
            if (!targetInfo.Exists || sourceInfo.Length != targetInfo.Length || sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc.AddMilliseconds(1))
            {
                AddJob(jobs, item.Value, destination, item.Key, "已复制");
            }
        }
        return ExecuteJobs(jobs);
    }

    private static void EnsureOneWayDirectories(string source, string target)
    {
        foreach (string relative in MapDirectories(source))
            Directory.CreateDirectory(Path.Combine(target, relative));
    }

    private static void EnsureTwoWayDirectories(string folderA, string folderB)
    {
        var all = new HashSet<string>(MapDirectories(folderA), StringComparer.OrdinalIgnoreCase);
        all.UnionWith(MapDirectories(folderB));
        foreach (string relative in all)
        {
            Directory.CreateDirectory(Path.Combine(folderA, relative));
            Directory.CreateDirectory(Path.Combine(folderB, relative));
        }
    }

    private static void AddJob(List<CopyJob> jobs, string source, string target, string relative, string direction)
    {
        jobs.Add(new CopyJob { Source = source, Target = target, Relative = relative, Direction = direction, Size = new FileInfo(source).Length });
    }

    private SyncResult ExecuteJobs(List<CopyJob> jobs)
    {
        var result = new SyncResult { Total = jobs.Count, Bytes = jobs.Sum(j => j.Size) };
        long completedBytes = 0;
        int completedFiles = 0;
        int copiedFiles = 0;
        var watch = Stopwatch.StartNew();
        lastProgressTick = 0;
        if (jobs.Count == 0) ReportProgress(0, 0, 0, 0, "没有需要同步的文件", watch, true);
        var options = new ParallelOptions { MaxDegreeOfParallelism = DetermineParallelism(jobs) };
        Parallel.ForEach(jobs, options, delegate(CopyJob job)
        {
            try
            {
                CopyFile(job.Source, job.Target, delegate(long delta)
                {
                    long done = Interlocked.Add(ref completedBytes, delta);
                    int currentCompleted = Interlocked.CompareExchange(ref completedFiles, 0, 0);
                    ReportProgress(currentCompleted, result.Total, done, result.Bytes, job.Relative, watch, false);
                });
                Interlocked.Increment(ref copiedFiles);
                int finishedCount = Interlocked.Increment(ref completedFiles);
                LogFromWorker(job.Direction + "：" + job.Relative);
                ReportProgress(finishedCount, result.Total, Interlocked.Read(ref completedBytes), result.Bytes, job.Relative, watch, true);
            }
            catch (Exception ex) { LogFromWorker("失败：" + job.Relative + "（" + ex.Message + "）"); }
        });
        result.Copied = copiedFiles;
        return result;
    }

    private static int DetermineParallelism(List<CopyJob> jobs)
    {
        if (jobs.Count <= 1) return 1;
        long averageSize = jobs.Sum(j => j.Size) / jobs.Count;
        if (averageSize >= 1024L * 1024L * 1024L) return 2;
        return Math.Max(2, Math.Min(4, Environment.ProcessorCount / 2));
    }

    private static Dictionary<string, string> MapFiles(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                result[file.Substring(root.TrimEnd('\\').Length + 1)] = file;
            }
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) == 0) pending.Push(directory);
            }
        }
        return result;
    }

    private static List<string> MapDirectories(string root)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                result.Add(directory.Substring(root.TrimEnd('\\').Length + 1));
                pending.Push(directory);
            }
        }
        return result;
    }

    private static void CopyFile(string source, string target, Action<long> progress)
    {
        string directory = Path.GetDirectoryName(target);
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        string temp = target + ".syncing-" + Process.GetCurrentProcess().Id;
        try
        {
            long lastTransferred = 0;
            var callback = new CopyProgressRoutine(delegate(
                long total, long transferred, long streamSize, long streamTransferred,
                uint streamNumber, CopyProgressCallbackReason reason, IntPtr src, IntPtr dst, IntPtr data)
            {
                long delta = transferred - lastTransferred;
                if (delta > 0)
                {
                    lastTransferred = transferred;
                    progress(delta);
                }
                return CopyProgressResult.Continue;
            });
            int cancel = 0;
            if (!CopyFileEx(source, temp, callback, IntPtr.Zero, ref cancel, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));
            if (File.Exists(target)) File.Replace(temp, target, null);
            else File.Move(temp, target);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void ReportProgress(int completed, int total, long doneBytes, long totalBytes, string current, Stopwatch watch, bool force)
    {
        long now = watch.ElapsedMilliseconds;
        lock (progressLock)
        {
            if (!force && now - lastProgressTick < 250) return;
            lastProgressTick = now;
        }
        double seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.1);
        double speed = doneBytes / seconds;
        TimeSpan remaining = speed > 0 ? TimeSpan.FromSeconds(Math.Max(0, totalBytes - doneBytes) / speed) : TimeSpan.Zero;
        string message = total == 0 ? "没有需要同步的文件" : "同步 " + completed + "/" + total + "｜" + FormatBytes((long)speed) + "/秒｜剩余 " + FormatDuration(remaining);
        Action update = delegate
        {
            SyncTaskConfig runningTask = tasks.FirstOrDefault(t => t.Id == currentTaskId);
            if (runningTask != null)
            {
                runningTask.ProgressPercent = total == 0 || totalBytes == 0 ? 100 : (int)Math.Min(100, doneBytes * 100L / totalBytes);
                runningTask.RuntimeStatus = message;
            }
            status.Text = (String.IsNullOrEmpty(currentTaskName) ? "" : currentTaskName + "｜") + message;
            trayIcon.Text = message.Length > 63 ? message.Substring(0, 63) : message;
            UpdateTaskRows();
        };
        if (InvokeRequired) BeginInvoke(update); else update();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1) { value /= 1024; unitIndex++; }
        return value.ToString(unitIndex == 0 ? "0" : "0.0") + " " + units[unitIndex];
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1) return ((int)duration.TotalHours) + "小时" + duration.Minutes + "分";
        if (duration.TotalMinutes >= 1) return ((int)duration.TotalMinutes) + "分" + duration.Seconds + "秒";
        return Math.Max(0, (int)Math.Ceiling(duration.TotalSeconds)) + "秒";
    }

    private void LogFromWorker(string text)
    {
        if (InvokeRequired) BeginInvoke(new Action<string>(AppendLog), text);
        else AppendLog(text);
    }

    private void AppendLog(string text)
    {
        log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
    }

    private void ChangeStartup()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (startup.Checked) key.SetValue("星汇", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("星汇", false);
            }
            AppendLog("开机自动运行" + (startup.Checked ? "已启用" : "已关闭"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法修改开机启动设置：" + ex.Message, "设置失败");
        }
    }

    private void SaveConfig()
    {
        try
        {
            string temp = ConfigPath + ".tmp";
            var store = new SyncTaskStore();
            store.Tasks.AddRange(tasks);
            var serializer = new XmlSerializer(typeof(SyncTaskStore));
            using (var stream = File.Create(temp)) serializer.Serialize(stream, store);
            if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, null);
            else File.Move(temp, ConfigPath);
        }
        catch (Exception ex) { AppendLog("保存任务配置失败：" + ex.Message); }
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var serializer = new XmlSerializer(typeof(SyncTaskStore));
                using (var stream = File.OpenRead(ConfigPath))
                {
                    var store = (SyncTaskStore)serializer.Deserialize(stream);
                    if (store != null && store.Tasks != null) tasks.AddRange(store.Tasks);
                }
            }
        }
        catch (Exception ex) { AppendLog("读取任务配置失败：" + ex.Message); }

        if (tasks.Count == 0 && File.Exists(LegacyConfigPath))
        {
            try
            {
                string[] lines = File.ReadAllLines(LegacyConfigPath);
                if (lines.Length >= 5)
                {
                    decimal oldInterval;
                    if (!Decimal.TryParse(lines[3], out oldInterval) || oldInterval < 1) oldInterval = 60;
                    tasks.Add(new SyncTaskConfig {
                        Name = "原同步任务", FolderA = lines[0], FolderB = lines[1],
                        Mode = lines[2] == "A→B" ? "单向上传（A → B）" : lines[2] == "B→A" ? "单向下载（B → A）" : lines[2],
                        Interval = oldInterval, Unit = lines[4], Enabled = false
                    });
                }
            }
            catch { }
        }
        if (tasks.Count == 0) tasks.Add(new SyncTaskConfig { Name = "同步任务 1" });
        foreach (SyncTaskConfig task in tasks)
        {
            if (String.IsNullOrEmpty(task.Id)) task.Id = Guid.NewGuid().ToString("N");
            if (task.Interval < 1) task.Interval = 60;
            if (String.IsNullOrEmpty(task.Unit)) task.Unit = "秒";
            task.NextRun = task.Enabled ? DateTime.Now : DateTime.MaxValue;
            task.RuntimeStatus = task.Enabled ? "等待运行" : "未启动";
        }
        selectedTask = tasks[0];
        RefreshTaskLists();
        UpdateTaskButtons();
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            startup.Checked = key != null && key.GetValue("星汇") != null;
    }
}
