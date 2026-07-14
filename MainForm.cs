using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LocalServiceManager
{
    internal sealed class MainForm : Form
    {
        private readonly ServiceManager _services;
        private readonly NotifyIcon _tray;
        private readonly DataGridView _grid;
        private readonly TabControl _tabs;
        private readonly TextBox _log;
        private readonly CheckBox _startupCheckBox;
        private readonly Timer _timer;
        private readonly Timer _autoStartWatchTimer;
        private Label _titleLabel;
        private IList<ManagedServiceStatus> _lastStatuses = new List<ManagedServiceStatus>();
        private bool _busy;
        private bool _refreshBusy;
        private bool _autoStartCheckBusy;
        private bool _exitRequested;
        private bool _syncingStartup;
        private bool _syncingServiceAutoStart;
        private bool _syncingTabs;

        public MainForm(ServiceManager services)
        {
            _services = services;
            Text = _services.Config.Title;
            MinimumSize = new Size(940, 500);
            Size = new Size(1040, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);

            _grid = BuildGrid();
            _tabs = BuildTabs();
            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 22, 22),
                ForeColor = Color.FromArgb(226, 232, 222),
                BorderStyle = BorderStyle.FixedSingle
            };
            _startupCheckBox = new CheckBox
            {
                Text = "开机启动",
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _startupCheckBox.CheckedChanged += delegate
            {
                if (_syncingStartup) return;
                if (_startupCheckBox.Checked)
                {
                    StartupManager.Install();
                    Log("已启用开机启动");
                }
                else
                {
                    StartupManager.Remove();
                    Log("已关闭开机启动");
                }
                UpdateStartupCheckBox();
            };

            Controls.Add(BuildLayout());
            _tray = BuildTrayIcon();
            _timer = new Timer { Interval = 7000 };
            _timer.Tick += delegate { RunFireAndForget(delegate { return RefreshStatusesAsync(false); }); };
            _timer.Start();

            _autoStartWatchTimer = new Timer { Interval = 30000 };
            _autoStartWatchTimer.Tick += delegate { RunFireAndForget(delegate { return EnsureAutoStartServicesRunningAsync("自动启动巡检"); }); };
            _autoStartWatchTimer.Start();

            Shown += delegate { RunFireAndForget(InitialRefreshAndAutoStartAsync); };
            FormClosing += OnFormClosing;
            UpdateStartupCheckBox();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _autoStartWatchTimer.Dispose();
                _tray.Dispose();
            }
            base.Dispose(disposing);
        }

        private Control BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));

            root.Controls.Add(BuildTitleBar(), 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            root.Controls.Add(BuildButtons(), 0, 2);
            root.Controls.Add(_log, 0, 3);
            return root;
        }

        private Control BuildTitleBar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _titleLabel = new Label
            {
                Text = _services.Config.Title,
                Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            bar.Controls.Add(_titleLabel, 0, 0);
            bar.Controls.Add(Button("刷新状态", delegate { return RefreshStatusesAsync(true, true); }), 1, 0);
            return bar;
        }

        private TabControl BuildTabs()
        {
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };
            var configuredTabs = _services.Config.tabs;
            if (configuredTabs != null)
            {
                foreach (var tab in configuredTabs)
                {
                    if (tab == null) continue;
                    var title = string.IsNullOrWhiteSpace(tab.label) ? "全部" : tab.label;
                    tabs.TabPages.Add(BuildTabPage(title, tab.tag ?? ""));
                }
            }
            if (tabs.TabPages.Count == 0) tabs.TabPages.Add(BuildTabPage("全部", ""));
            tabs.SelectedIndexChanged += delegate
            {
                if (_syncingTabs) return;
                MoveGridToSelectedTab();
                PopulateGrid(_lastStatuses);
            };
            tabs.SelectedIndex = 0;
            MoveGridToSelectedTab(tabs);
            return tabs;
        }
        private static TabPage BuildTabPage(string title, string tag)
        {
            return new TabPage(title) { Tag = tag, Padding = new Padding(6) };
        }

        private void MoveGridToSelectedTab()
        {
            MoveGridToSelectedTab(_tabs);
        }

        private void MoveGridToSelectedTab(TabControl tabs)
        {
            if (tabs.TabPages.Count == 0) return;
            if (_grid.Parent != null) _grid.Parent.Controls.Remove(_grid);
            var page = tabs.SelectedTab ?? tabs.TabPages[0];
            page.Controls.Add(_grid);
            _grid.Dock = DockStyle.Fill;
        }

        private void SyncConfigUi()
        {
            Text = _services.Config.Title;
            if (_titleLabel != null) _titleLabel.Text = _services.Config.Title;
            SyncTabsWithConfig();
        }

        private void SyncTabsWithConfig()
        {
            var selectedTag = CurrentTag;
            _syncingTabs = true;
            try
            {
                if (_grid.Parent != null) _grid.Parent.Controls.Remove(_grid);
                _tabs.TabPages.Clear();
                var configuredTabs = _services.Config.tabs;
                if (configuredTabs != null)
                {
                    foreach (var tab in configuredTabs)
                    {
                        if (tab == null) continue;
                        var title = string.IsNullOrWhiteSpace(tab.label) ? "全部" : tab.label;
                        _tabs.TabPages.Add(BuildTabPage(title, tab.tag ?? ""));
                    }
                }
                if (_tabs.TabPages.Count == 0) _tabs.TabPages.Add(BuildTabPage("全部", ""));
                var selectedIndex = 0;
                for (var i = 0; i < _tabs.TabPages.Count; i++)
                {
                    if (string.Equals(Convert.ToString(_tabs.TabPages[i].Tag) ?? "", selectedTag, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                _tabs.SelectedIndex = selectedIndex;
                MoveGridToSelectedTab();
            }
            finally
            {
                _syncingTabs = false;
            }
        }

        private string CurrentTag
        {
            get
            {
                if (_tabs == null || _tabs.SelectedTab == null) return "";
                return Convert.ToString(_tabs.SelectedTab.Tag) ?? "";
            }
        }

        private Control BuildButtons()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0, 8, 0, 8)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(Button("打开本地配置", delegate { _services.OpenLocalConfig(); }), 0, 0);
            panel.Controls.Add(Button("打开示例配置", delegate { _services.OpenInstanceConfig(); }), 1, 0);
            panel.Controls.Add(Button("打开日志目录", delegate { _services.OpenLogs(); }), 2, 0);
            panel.Controls.Add(_startupCheckBox, 3, 0);
            return panel;
        }

        private DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                RowTemplate = { Height = 38 },
                BackgroundColor = Color.FromArgb(245, 246, 245),
                DefaultCellStyle = { SelectionBackColor = Color.White, SelectionForeColor = Color.Black },
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "服务", ReadOnly = true, Width = 170 });
            grid.Columns.Add(new DataGridViewLinkColumn
            {
                Name = "endpoint",
                HeaderText = "地址",
                ReadOnly = true,
                Width = 270,
                TrackVisitedState = false,
                LinkBehavior = LinkBehavior.HoverUnderline,
                UseColumnTextForLinkValue = false
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "detail", HeaderText = "详情", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "state", HeaderText = "状态", ReadOnly = true, Width = 90 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "action", HeaderText = "操作", ReadOnly = true, Width = 82, UseColumnTextForButtonValue = false });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "autoStart", HeaderText = "自动启动", Width = 82 });
            grid.CellContentClick += OnGridCellContentClick;
            grid.SelectionChanged += delegate { grid.ClearSelection(); };
            grid.CellValueChanged += OnGridCellValueChanged;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            return grid;
        }

        private NotifyIcon BuildTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示面板", null, delegate { ShowWindow(); });
            menu.Items.Add("启动全部", null, delegate { RunFireAndForget(delegate { return RunActionAsync("启动全部", _services.StartAllAsync); }); });
            menu.Items.Add("停止全部", null, delegate { RunFireAndForget(delegate { return RunActionAsync("停止全部", _services.StopAllAsync); }); });
            menu.Items.Add("刷新状态", null, delegate { RunFireAndForget(delegate { return RefreshStatusesAsync(true, true); }); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开本地配置", null, delegate { _services.OpenLocalConfig(); });
            menu.Items.Add("打开示例配置", null, delegate { _services.OpenInstanceConfig(); });
            menu.Items.Add("打开日志目录", null, delegate { _services.OpenLogs(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { _exitRequested = true; Close(); });

            var tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = _services.Config.Title,
                Visible = true,
                ContextMenuStrip = menu
            };
            tray.DoubleClick += delegate { ShowWindow(); };
            return tray;
        }

        private Button Button(string text, Func<Task> action)
        {
            return Button(text, delegate { RunFireAndForget(action); });
        }

        private Button Button(string text, Action action)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                UseVisualStyleBackColor = true
            };
            button.Click += delegate { action(); };
            return button;
        }

        private async Task RunActionAsync(string label, Func<Task<string>> action)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                Log("> " + label + " ...");
                var output = await action();
                if (!string.IsNullOrWhiteSpace(output)) Log(output);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                MessageBox.Show(this, ex.Message, label, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
            }
            await RefreshStatusesAsync(true);
        }

        private async Task InitialRefreshAndAutoStartAsync()
        {
            await RefreshStatusesAsync(true);
            await EnsureAutoStartServicesRunningAsync("启动时自动启动");
        }

        private async Task EnsureAutoStartServicesRunningAsync(string label)
        {
            if (_busy || _autoStartCheckBusy) return;
            _busy = true;
            _autoStartCheckBusy = true;
            try
            {
                var configReloaded = _services.ReloadConfigIfChanged();
                if (configReloaded) SyncConfigUi();
                var statuses = await _services.GetStatusesAsync();
                var startedAny = false;
                foreach (var status in statuses)
                {
                    if (!ShouldAutoStart(status)) continue;
                    Log("> " + label + " " + status.Service.Name + " ...");
                    var output = await _services.StartServiceAsync(status.Service.Id);
                    if (!string.IsNullOrWhiteSpace(output)) Log(output);
                    startedAny = true;
                }
                if (startedAny || configReloaded)
                {
                    _lastStatuses = await _services.GetStatusesAsync();
                    PopulateGrid(_lastStatuses);
                    _tray.Text = AllRunning(_lastStatuses) ? "本地服务：全部运行中" : "本地服务：部分未运行";
                    if (startedAny) Log(label + "已完成");
                }
            }
            catch (Exception ex)
            {
                Log(label + "失败: " + ex.Message);
            }
            finally
            {
                _autoStartCheckBusy = false;
                _busy = false;
                UpdateStartupCheckBox();
            }
        }

        private async Task RefreshStatusesAsync(bool log, bool allowDuringAction)
        {
            if (_refreshBusy || _autoStartCheckBusy || (_busy && !allowDuringAction)) return;
            _refreshBusy = true;
            try
            {
                var configReloaded = _services.ReloadConfigIfChanged();
                if (configReloaded) SyncConfigUi();
                _lastStatuses = await _services.GetStatusesAsync();
                PopulateGrid(_lastStatuses);
                _tray.Text = AllRunning(_lastStatuses) ? "本地服务：全部运行中" : "本地服务：部分未运行";
                if (log) Log(configReloaded ? "状态已刷新，配置已重新读取" : "状态已刷新");
            }
            catch (Exception ex)
            {
                Log("刷新状态失败: " + ex.Message);
            }
            finally
            {
                _refreshBusy = false;
            }
            UpdateStartupCheckBox();
        }

        private Task RefreshStatusesAsync(bool log)
        {
            return RefreshStatusesAsync(log, false);
        }

        private void PopulateGrid(IList<ManagedServiceStatus> statuses)
        {
            _syncingServiceAutoStart = true;
            try
            {
                _grid.Rows.Clear();
                var tag = CurrentTag;
                foreach (var status in statuses)
                {
                    if (!status.Service.HasTag(tag)) continue;
                    var displayState = DisplayState(status);
                    var index = _grid.Rows.Add(
                        status.Service.Name,
                        status.Service.Endpoint,
                        status.Detail,
                        displayState,
                        status.Running ? "停止" : "启动",
                        ServiceAutoStartManager.IsEnabled(status.Service.Id));
                    var row = _grid.Rows[index];
                    row.Tag = status.Service.Id;
                    row.DefaultCellStyle.BackColor = Color.White;
                    row.DefaultCellStyle.SelectionBackColor = Color.White;
                    row.DefaultCellStyle.SelectionForeColor = Color.Black;
                    row.Cells["state"].Style.ForeColor = status.Running ? Color.FromArgb(16, 128, 56) : Color.FromArgb(192, 36, 36);
                    row.Cells["state"].Style.SelectionForeColor = row.Cells["state"].Style.ForeColor;
                    row.Cells["state"].Style.Font = new Font(Font, FontStyle.Bold);
                    row.Cells["endpoint"].ToolTipText = string.IsNullOrWhiteSpace(status.Service.Endpoint) ? "" : "点击打开浏览器";
                }
                _grid.ClearSelection();
            }
            finally
            {
                _syncingServiceAutoStart = false;
            }
        }

        private void OnGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var grid = (DataGridView)sender;
            var columnName = grid.Columns[e.ColumnIndex].Name;
            var row = grid.Rows[e.RowIndex];
            if (columnName == "endpoint")
            {
                OpenEndpoint(row);
                return;
            }
            if (columnName != "action") return;
            var serviceId = row.Tag as string;
            if (string.IsNullOrWhiteSpace(serviceId)) return;
            var serviceName = Convert.ToString(row.Cells["name"].Value);
            var shouldStop = string.Equals(Convert.ToString(row.Cells["action"].Value), "停止", StringComparison.OrdinalIgnoreCase);
            RunFireAndForget(delegate
            {
                return RunActionAsync((shouldStop ? "停止 " : "启动 ") + serviceName, delegate
                {
                    return shouldStop
                        ? _services.StopServiceAsync(serviceId)
                        : _services.StartServiceAsync(serviceId);
                });
            });
        }

        private void OpenEndpoint(DataGridViewRow row)
        {
            var endpoint = Convert.ToString(row.Cells["endpoint"].Value);
            if (string.IsNullOrWhiteSpace(endpoint)) return;
            try
            {
                _services.OpenEndpoint(endpoint);
            }
            catch (Exception ex)
            {
                Log("打开地址失败: " + ex.Message);
                MessageBox.Show(this, ex.Message, "打开地址", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_syncingServiceAutoStart || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var grid = (DataGridView)sender;
            if (grid.Columns[e.ColumnIndex].Name != "autoStart") return;
            var row = grid.Rows[e.RowIndex];
            var serviceId = row.Tag as string;
            if (string.IsNullOrWhiteSpace(serviceId)) return;
            var enabled = Convert.ToBoolean(row.Cells["autoStart"].Value);
            ServiceAutoStartManager.SetEnabled(serviceId, enabled);
            Log(Convert.ToString(row.Cells["name"].Value) + (enabled ? " 已开启自动启动" : " 已关闭自动启动"));
        }

        private static ManagedServiceStatus FindStatus(IList<ManagedServiceStatus> statuses, string id)
        {
            foreach (var status in statuses)
            {
                if (string.Equals(status.Service.Id, id, StringComparison.OrdinalIgnoreCase)) return status;
            }
            return null;
        }

        private static bool ShouldAutoStart(ManagedServiceStatus status)
        {
            return status != null
                && !status.Running
                && string.Equals(status.State, "未运行", StringComparison.OrdinalIgnoreCase)
                && ServiceAutoStartManager.IsEnabled(status.Service.Id);
        }

        private static bool AllRunning(IList<ManagedServiceStatus> statuses)
        {
            foreach (var status in statuses)
            {
                if (!status.Running) return false;
            }
            return true;
        }

        private static string DisplayState(ManagedServiceStatus status)
        {
            if (status.Running) return "运行中";
            return string.Equals(status.State, "异常", StringComparison.OrdinalIgnoreCase) ? "异常" : "已停止";
        }

        private void UpdateStartupCheckBox()
        {
            _syncingStartup = true;
            try
            {
                _startupCheckBox.Checked = StartupManager.IsInstalled();
            }
            finally
            {
                _syncingStartup = false;
            }
        }

        private void Log(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), text);
                return;
            }
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exitRequested) return;
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(1800, _services.Config.Title, "窗口已隐藏，右下角托盘可继续打开。", ToolTipIcon.Info);
        }

        private void RunFireAndForget(Func<Task> taskFactory)
        {
            var ignored = RunFireAndForgetAsync(taskFactory);
        }

        private async Task RunFireAndForgetAsync(Func<Task> taskFactory)
        {
            try
            {
                await taskFactory();
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
        }
    }
}

