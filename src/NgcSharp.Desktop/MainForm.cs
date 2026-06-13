using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NgcSharp.App;
using NgcSharp.Core;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace NgcSharp.Desktop;

public sealed class MainForm : Form
{
    private const int DefaultFrameWidth = 640;
    private const int DefaultFrameHeight = 480;
    private const int DesktopDefaultMaxInstructions = int.MaxValue;
    private const int VideoFrameLimit = 4_194_304;
    private const int MaxOutputCharacters = 256 * 1024;
    private const int OutputTrimTargetCharacters = 192 * 1024;
    private const int LiveFrameCapturePollInstructions = 4096;

    private readonly TableLayoutPanel _rootLayout = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 4,
    };

    private readonly MenuStrip _menuStrip = new()
    {
        Dock = DockStyle.Top,
    };

    private readonly ToolStripMenuItem _presetsMenu = new("Presets");
    private readonly ToolStripMenuItem _traceMenuItem = new("Instruction trace") { CheckOnClick = true };
    private readonly ToolStripMenuItem _verboseOutputMenuItem = new("Verbose runner output") { CheckOnClick = true };
    private readonly ToolStripMenuItem _liveFrameCaptureMenuItem = new("Live screen capture") { CheckOnClick = true, Checked = true };
    private readonly ToolStripMenuItem _dumpMmioMenuItem = new("Dump MMIO") { CheckOnClick = true };
    private readonly ToolStripMenuItem _dumpRegistersMenuItem = new("Dump registers") { CheckOnClick = true };
    private readonly ToolStripMenuItem _fastForwardIdleMenuItem = new("Fast-forward known idle loops") { CheckOnClick = true, Checked = true };
    private readonly ToolStripMenuItem _memoryCardAMenuItem = new("Memory card A") { CheckOnClick = true, Checked = true };
    private readonly ToolStripMenuItem _memoryCardBMenuItem = new("Memory card B") { CheckOnClick = true };
    private readonly ToolStripMenuItem _controllerMenu = new("Controller");
    private readonly ToolStripMenuItem _runMenuItem = new("Run");
    private readonly ToolStripMenuItem _cancelRunMenuItem = new("Cancel") { Enabled = false };
    private readonly ToolStripMenuItem _clearOutputMenuItem = new("Clear Output");

    private readonly TableLayoutPanel _commandBar = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 10,
        RowCount = 2,
        Padding = new Padding(8, 6, 8, 6),
        BackColor = Color.FromArgb(30, 32, 36),
    };

    private readonly ComboBox _commandSelector = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
    };

    private readonly ComboBox _presetSelector = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
    };

    private readonly TextBox _pathTextBox = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "Select a DOL or disc image",
    };

    private readonly Button _browseButton = new()
    {
        Text = "Browse",
        Dock = DockStyle.Right,
        Width = 88,
    };

    private readonly NumericUpDown _maxInstructionsInput = new()
    {
        Dock = DockStyle.Fill,
        Minimum = 1,
        Maximum = int.MaxValue,
        Value = DesktopDefaultMaxInstructions,
        Increment = 10_000_000,
        ThousandsSeparator = true,
    };

    private readonly NumericUpDown _frameCaptureStrideInput = new()
    {
        Dock = DockStyle.Fill,
        Minimum = 1,
        Maximum = 600,
        Value = 2,
        Increment = 1,
        ThousandsSeparator = true,
    };

    private readonly CheckBox _traceCheck = new()
    {
        Text = "Instruction trace",
        AutoSize = true,
        ForeColor = Color.Gainsboro,
    };

    private readonly TextBox _tracePathTextBox = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "Trace path (optional)",
        Enabled = false,
    };

    private readonly CheckBox _dumpMmio = new()
    {
        Text = "Dump MMIO",
        AutoSize = true,
        ForeColor = Color.Gainsboro,
    };

    private readonly CheckBox _dumpRegisters = new()
    {
        Text = "Dump registers",
        AutoSize = true,
        ForeColor = Color.Gainsboro,
    };

    private readonly CheckBox _fastForwardIdle = new()
    {
        Text = "Fast-forward known idle loops",
        AutoSize = true,
        Checked = true,
        ForeColor = Color.Gainsboro,
    };

    private readonly CheckBox _memoryCardA = new()
    {
        Text = "Memory card A",
        AutoSize = true,
        Checked = true,
        ForeColor = Color.Gainsboro,
    };

    private readonly CheckBox _memoryCardB = new()
    {
        Text = "Memory card B",
        AutoSize = true,
        ForeColor = Color.Gainsboro,
    };

    private readonly ComboBox _controllerButtons = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
    };

    private readonly Button _runButton = new()
    {
        Text = "Run",
        Dock = DockStyle.Fill,
        Height = 36,
        BackColor = Color.FromArgb(50, 112, 202),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };

    private readonly Button _clearButton = new()
    {
        Text = "Clear",
        Dock = DockStyle.Fill,
        Height = 36,
    };

    private readonly Button _cancelButton = new()
    {
        Text = "Cancel",
        Dock = DockStyle.Fill,
        Height = 36,
        Enabled = false,
    };

    private readonly StatusStrip _statusStrip = new()
    {
        Dock = DockStyle.Bottom,
    };

    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Text = "Ready",
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private readonly SplitContainer _rightSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        SplitterWidth = 6,
        SplitterDistance = 430,
    };

    private readonly Panel _screenPanel = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.Black,
        Padding = new Padding(10),
    };

    private readonly PictureBox _screen = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.Black,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private readonly TabControl _bottomTabs = new()
    {
        Dock = DockStyle.Fill,
    };

    private readonly RichTextBox _output = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        ScrollBars = RichTextBoxScrollBars.Both,
        BackColor = Color.FromArgb(18, 18, 18),
        ForeColor = Color.FromArgb(210, 220, 210),
        BorderStyle = BorderStyle.None,
    };

    private readonly RichTextBox _telemetryOutput = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        ScrollBars = RichTextBoxScrollBars.Both,
        BackColor = Color.FromArgb(18, 18, 18),
        ForeColor = Color.FromArgb(210, 220, 210),
        BorderStyle = BorderStyle.None,
    };

    private readonly TextBox _additionalArgs = new()
    {
        Multiline = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f),
        BackColor = Color.FromArgb(24, 24, 24),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = "Additional ngcsharp options, e.g. --dump-frame frame.png --dump-gx-frame gx-frame.png",
    };

    private readonly TextBox _commandPreview = new()
    {
        Multiline = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f),
        BackColor = Color.FromArgb(24, 24, 24),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.FixedSingle,
        ReadOnly = true,
    };

    private readonly WinFormsTimer _screenUpdateTimer = new() { Interval = 16 };
    private readonly object _screenFrameLock = new();
    private Bitmap? _pendingScreenFrame;
    private CancellationTokenSource? _activeRunCancellation;
    private bool _applyingPreset;

    private static readonly (uint High, uint Low)[] ViFramebufferRegisterPairs =
    [
        (0xCC00_2020, 0xCC00_2022),
        (0xCC00_201C, 0xCC00_201E),
    ];

    private static readonly uint[] ViFramebufferRegisters =
    [
        0xCC00_201C,
        0xCC00_2020,
        0xCC00_2024,
        0xCC00_2028,
    ];

    public MainForm()
    {
        Text = "ngcSharp";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(18, 18, 18);
        ForeColor = Color.Gainsboro;
        MinimumSize = new Size(1060, 720);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 780);

        _presetSelector.Items.AddRange(BuildPresets());
        _commandSelector.Items.AddRange(["Run DOL", "Run Disc", "Disc Info", "DOL Info"]);
        _commandSelector.SelectedIndex = 0;
        _controllerButtons.Items.AddRange(["None", "A", "Start", "B", "X", "Y", "A+Start"]);
        _controllerButtons.SelectedIndex = 0;

        BuildMenuBar();
        BuildCommandBar();
        BuildContentArea();

        Controls.Add(_rootLayout);
        MainMenuStrip = _menuStrip;
        _statusStrip.Items.Add(_statusLabel);
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.Controls.Add(_menuStrip, 0, 0);
        _rootLayout.Controls.Add(_commandBar, 0, 1);
        _rootLayout.Controls.Add(_rightSplit, 0, 2);
        _rootLayout.Controls.Add(_statusStrip, 0, 3);

        _traceCheck.CheckedChanged += (_, _) => _tracePathTextBox.Enabled = _traceCheck.Checked;
        _browseButton.Click += OnBrowseClicked;
        _runButton.Click += OnRunClicked;
        _cancelButton.Click += OnCancelClicked;
        _clearButton.Click += (_, _) => ClearLogs();
        _runMenuItem.Click += OnRunClicked;
        _cancelRunMenuItem.Click += OnCancelClicked;
        _clearOutputMenuItem.Click += (_, _) => ClearLogs();
        _commandSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!_applyingPreset)
            {
                MarkCustomPreset();
            }

            UpdateModeState();
        };
        _presetSelector.SelectedIndexChanged += OnPresetChanged;
        RegisterPreviewUpdateHandlers();
        _screenUpdateTimer.Tick += OnScreenFrameTick;

        _presetSelector.SelectedIndex = SelectInitialPresetIndex();
        UpdateModeState();
        UpdateCommandPreview();
    }

    private sealed record DesktopPreset(
        string Name,
        string CommandName,
        string? Path,
        int MaxInstructions,
        bool MemoryCardA,
        bool MemoryCardB,
        string ControllerButtons,
        string AdditionalArguments)
    {
        public override string ToString() => Name;
    }

    private sealed record RunCommandRequest(
        string CommandName,
        string Path,
        int MaxInstructions,
        bool TraceEnabled,
        string TracePath,
        bool VerboseRunnerOutput,
        bool LiveFrameCapture,
        int FrameCaptureStride,
        bool DumpMmio,
        bool DumpRegisters,
        bool FastForwardIdle,
        bool MemoryCardA,
        bool MemoryCardB,
        string ControllerButtons,
        string ArtifactDirectory,
        IReadOnlyList<string> AdditionalArguments);

    private void BuildMenuBar()
    {
        ToolStripMenuItem fileMenu = new("&File");
        ToolStripMenuItem openMenuItem = new("&Open...")
        {
            ShortcutKeys = Keys.Control | Keys.O,
        };
        openMenuItem.Click += OnBrowseClicked;
        ToolStripMenuItem discInfoMenuItem = new("Disc &Info");
        discInfoMenuItem.Click += (_, _) => SelectCommandAndRun("Disc Info");
        ToolStripMenuItem dolInfoMenuItem = new("DOL I&nfo");
        dolInfoMenuItem.Click += (_, _) => SelectCommandAndRun("DOL Info");
        ToolStripMenuItem exitMenuItem = new("E&xit");
        exitMenuItem.Click += (_, _) => Close();
        fileMenu.DropDownItems.AddRange([openMenuItem, new ToolStripSeparator(), discInfoMenuItem, dolInfoMenuItem, new ToolStripSeparator(), exitMenuItem]);

        foreach (DesktopPreset preset in _presetSelector.Items.OfType<DesktopPreset>())
        {
            ToolStripMenuItem item = new(preset.Name);
            item.Click += (_, _) => _presetSelector.SelectedItem = preset;
            _presetsMenu.DropDownItems.Add(item);
        }

        ToolStripMenuItem emulationMenu = new("&Emulation");
        _runMenuItem.ShortcutKeys = Keys.F5;
        _cancelRunMenuItem.ShortcutKeys = Keys.Shift | Keys.F5;
        emulationMenu.DropDownItems.AddRange([_runMenuItem, _cancelRunMenuItem, new ToolStripSeparator(), _clearOutputMenuItem]);

        ToolStripMenuItem optionsMenu = new("&Options");
        optionsMenu.DropDownItems.AddRange([
            _traceMenuItem,
            _verboseOutputMenuItem,
            _liveFrameCaptureMenuItem,
            _dumpMmioMenuItem,
            _dumpRegistersMenuItem,
            _fastForwardIdleMenuItem,
            new ToolStripSeparator(),
            _memoryCardAMenuItem,
            _memoryCardBMenuItem,
            new ToolStripSeparator(),
            _controllerMenu,
        ]);

        foreach (string controller in _controllerButtons.Items.Cast<string>())
        {
            ToolStripMenuItem item = new(controller);
            item.Click += (_, _) =>
            {
                _controllerButtons.SelectedItem = controller;
                SyncMenuItemsFromControls();
            };
            _controllerMenu.DropDownItems.Add(item);
        }

        ToolStripMenuItem viewMenu = new("&View");
        ToolStripMenuItem outputMenuItem = new("&Output");
        outputMenuItem.Click += (_, _) => _bottomTabs.SelectedTab = _bottomTabs.TabPages["Output"];
        ToolStripMenuItem telemetryMenuItem = new("&Telemetry");
        telemetryMenuItem.Click += (_, _) => _bottomTabs.SelectedTab = _bottomTabs.TabPages["Telemetry"];
        ToolStripMenuItem advancedMenuItem = new("&Advanced Options");
        advancedMenuItem.Click += (_, _) => _bottomTabs.SelectedTab = _bottomTabs.TabPages["Advanced Options"];
        ToolStripMenuItem commandPreviewMenuItem = new("&Command Preview");
        commandPreviewMenuItem.Click += (_, _) => _bottomTabs.SelectedTab = _bottomTabs.TabPages["Command Preview"];
        viewMenu.DropDownItems.AddRange([outputMenuItem, telemetryMenuItem, advancedMenuItem, commandPreviewMenuItem]);

        _traceMenuItem.CheckedChanged += (_, _) => _traceCheck.Checked = _traceMenuItem.Checked;
        _verboseOutputMenuItem.CheckedChanged += (_, _) => UpdateCommandPreview();
        _liveFrameCaptureMenuItem.CheckedChanged += (_, _) => UpdateCommandPreview();
        _dumpMmioMenuItem.CheckedChanged += (_, _) => _dumpMmio.Checked = _dumpMmioMenuItem.Checked;
        _dumpRegistersMenuItem.CheckedChanged += (_, _) => _dumpRegisters.Checked = _dumpRegistersMenuItem.Checked;
        _fastForwardIdleMenuItem.CheckedChanged += (_, _) => _fastForwardIdle.Checked = _fastForwardIdleMenuItem.Checked;
        _memoryCardAMenuItem.CheckedChanged += (_, _) => _memoryCardA.Checked = _memoryCardAMenuItem.Checked;
        _memoryCardBMenuItem.CheckedChanged += (_, _) => _memoryCardB.Checked = _memoryCardBMenuItem.Checked;

        _menuStrip.Items.AddRange([fileMenu, _presetsMenu, emulationMenu, optionsMenu, viewMenu]);
    }

    private void BuildCommandBar()
    {
        _commandBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _commandBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        _commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));

        _commandBar.Controls.Add(CreateInlineLabel("Preset"), 0, 0);
        _commandBar.Controls.Add(_presetSelector, 1, 0);
        _commandBar.Controls.Add(CreateInlineLabel("Mode"), 2, 0);
        _commandBar.Controls.Add(_commandSelector, 3, 0);
        _commandBar.Controls.Add(CreateInlineLabel("Controller"), 4, 0);
        _commandBar.Controls.Add(_controllerButtons, 5, 0);
        _commandBar.Controls.Add(_runButton, 6, 0);
        _commandBar.Controls.Add(_cancelButton, 7, 0);
        _commandBar.Controls.Add(_clearButton, 8, 0);

        _commandBar.Controls.Add(CreateInlineLabel("Input"), 0, 1);
        _commandBar.Controls.Add(_pathTextBox, 1, 1);
        _commandBar.SetColumnSpan(_pathTextBox, 7);
        _commandBar.Controls.Add(_browseButton, 8, 1);
    }

    private static Label CreateInlineLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(190, 196, 206),
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            Margin = new Padding(4, 0, 4, 0),
        };
    }

    private void BuildContentArea()
    {
        _screenPanel.Controls.Add(_screen);
        _rightSplit.Panel1.Controls.Add(_screenPanel);

        TabPage outputPage = new("Output") { Name = "Output" };
        outputPage.Controls.Add(_output);
        TabPage telemetryPage = new("Telemetry") { Name = "Telemetry" };
        telemetryPage.Controls.Add(_telemetryOutput);
        TabPage advancedPage = new("Advanced Options") { Name = "Advanced Options" };
        advancedPage.Controls.Add(CreateAdvancedOptionsPanel());
        TabPage commandPage = new("Command Preview") { Name = "Command Preview" };
        commandPage.Controls.Add(_commandPreview);
        _bottomTabs.TabPages.Add(outputPage);
        _bottomTabs.TabPages.Add(telemetryPage);
        _bottomTabs.TabPages.Add(advancedPage);
        _bottomTabs.TabPages.Add(commandPage);
        _rightSplit.Panel2.Controls.Add(_bottomTabs);
    }

    private Control CreateAdvancedOptionsPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(18, 18, 18),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(CreateInlineLabel("Max instructions"), 0, 0);
        panel.Controls.Add(_maxInstructionsInput, 0, 1);
        panel.Controls.Add(CreateInlineLabel("Live capture cadence (video frames)"), 0, 2);
        panel.Controls.Add(_frameCaptureStrideInput, 0, 3);
        panel.Controls.Add(CreateInlineLabel("Trace path"), 0, 4);
        panel.Controls.Add(_tracePathTextBox, 0, 5);
        panel.Controls.Add(CreateInlineLabel("Additional command-line arguments"), 0, 6);
        panel.Controls.Add(_additionalArgs, 0, 7);
        return panel;
    }

    private object[] BuildPresets()
    {
        string? sonicPath = FindWorkspaceFile("Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz");
        string? pikminPath = FindWorkspaceFile("Pikmin (USA).rvz");
        string? marioKartDebugPath = FindWorkspaceFile("Mario Kart - Double Dash!! (USA) (Debug).rvz");
        string? xfbSmokePath = FindWorkspaceFile("fixtures", "devkitpro", "xfb-smoke", "xfb-smoke.dol");

        return
        [
            new DesktopPreset("Custom", "Run Disc", null, DesktopDefaultMaxInstructions, MemoryCardA: true, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Play Sonic Adventure 2 Battle", "Run Disc", sonicPath, DesktopDefaultMaxInstructions, MemoryCardA: true, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Play Pikmin", "Run Disc", pikminPath, DesktopDefaultMaxInstructions, MemoryCardA: true, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Play Mario Kart Double Dash Debug", "Run Disc", marioKartDebugPath, DesktopDefaultMaxInstructions, MemoryCardA: true, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Dev: Quick DOL smoke - 1M", "Run DOL", xfbSmokePath, 1_000_000, MemoryCardA: false, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Dev: Sonic boot probe - 20M", "Run Disc", sonicPath, 20_000_000, MemoryCardA: true, MemoryCardB: false, "A", string.Empty),
            new DesktopPreset("Dev: Sonic Sega splash - 35M", "Run Disc", sonicPath, 35_000_000, MemoryCardA: true, MemoryCardB: false, "A", "--gx-frame-source largest-display-copy"),
            new DesktopPreset("Dev: Sonic resource flag watch - 35M", "Run Disc", sonicPath, 35_000_000, MemoryCardA: true, MemoryCardB: false, "A", "--watch-write-range 0x803ADCE0 0x4 --watch-write-after 0x01C00000 --stop-after-write-watch 12 --watch-limit 24 --trace-pc-after 0x01C00000 --profile-after 0x01C00000 --profile-pc 24"),
            new DesktopPreset("Dev: Pikmin boot probe - 20M", "Run Disc", pikminPath, 20_000_000, MemoryCardA: true, MemoryCardB: false, "None", "--gx-frame-source largest-display-copy"),
            new DesktopPreset("Dev: Mario Kart Debug startup - 5M", "Run Disc", marioKartDebugPath, 5_000_000, MemoryCardA: true, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Dev: Deep Sonic visual probe - 50M", "Run Disc", sonicPath, 50_000_000, MemoryCardA: true, MemoryCardB: false, "A", "--gx-frame-source largest-display-copy"),
        ];
    }

    private int SelectInitialPresetIndex()
    {
        for (int index = 0; index < _presetSelector.Items.Count; index++)
        {
            if (_presetSelector.Items[index] is DesktopPreset { Path: not null } preset
                && preset.Name.StartsWith("Play ", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private void UpdateModeState()
    {
        bool runMode = IsRunMode;
        bool discMode = IsDiscMode;

        _browseButton.Text = discMode ? "Disc..." : "DOL...";
        _maxInstructionsInput.Enabled = runMode;
        _traceCheck.Enabled = runMode;
        _tracePathTextBox.Enabled = runMode && _traceCheck.Checked;
        _frameCaptureStrideInput.Enabled = runMode && _liveFrameCaptureMenuItem.Checked;
        _dumpMmio.Enabled = runMode;
        _dumpRegisters.Enabled = runMode;
        _fastForwardIdle.Enabled = runMode;
        _memoryCardA.Enabled = runMode && discMode;
        _memoryCardB.Enabled = runMode && discMode;
        _controllerButtons.Enabled = runMode;
        _additionalArgs.Enabled = runMode;
        _runButton.Text = runMode ? "Run" : "Inspect";
        _runMenuItem.Text = _runButton.Text;
        _traceMenuItem.Enabled = runMode;
        _verboseOutputMenuItem.Enabled = runMode;
        _liveFrameCaptureMenuItem.Enabled = runMode;
        _dumpMmioMenuItem.Enabled = runMode;
        _dumpRegistersMenuItem.Enabled = runMode;
        _fastForwardIdleMenuItem.Enabled = runMode;
        _memoryCardAMenuItem.Enabled = runMode && discMode;
        _memoryCardBMenuItem.Enabled = runMode && discMode;
        _controllerMenu.Enabled = runMode;

        if (!discMode)
        {
            _memoryCardA.Checked = false;
            _memoryCardB.Checked = false;
        }
        else if (runMode && !_memoryCardA.Checked && !_memoryCardB.Checked)
        {
            _memoryCardA.Checked = true;
        }

        SyncMenuItemsFromControls();
        UpdateCommandPreview();
    }

    private void SyncMenuItemsFromControls()
    {
        SetMenuChecked(_traceMenuItem, _traceCheck.Checked);
        SetMenuChecked(_dumpMmioMenuItem, _dumpMmio.Checked);
        SetMenuChecked(_dumpRegistersMenuItem, _dumpRegisters.Checked);
        SetMenuChecked(_fastForwardIdleMenuItem, _fastForwardIdle.Checked);
        SetMenuChecked(_memoryCardAMenuItem, _memoryCardA.Checked);
        SetMenuChecked(_memoryCardBMenuItem, _memoryCardB.Checked);

        foreach (ToolStripMenuItem item in _controllerMenu.DropDownItems.OfType<ToolStripMenuItem>())
        {
            SetMenuChecked(item, string.Equals(item.Text, _controllerButtons.Text, StringComparison.Ordinal));
        }
    }

    private static void SetMenuChecked(ToolStripMenuItem item, bool isChecked)
    {
        if (item.Checked != isChecked)
        {
            item.Checked = isChecked;
        }
    }

    private void SelectCommandAndRun(string commandName)
    {
        _commandSelector.SelectedItem = commandName;
        OnRunClicked(this, EventArgs.Empty);
    }

    private bool IsRunMode => _commandSelector.SelectedItem is "Run DOL" or "Run Disc";

    private bool IsDiscMode => _commandSelector.SelectedItem is "Run Disc" or "Disc Info";

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Select input file",
            CheckFileExists = true,
            Filter = IsDiscMode
                ? "GameCube images (*.iso;*.gcm;*.rvz)|*.iso;*.gcm;*.rvz|All files (*.*)|*.*"
                : "DOL files (*.dol)|*.dol|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathTextBox.Text = dialog.FileName;
            MarkCustomPreset();
            UpdateCommandPreview();
        }
    }

    private void OnPresetChanged(object? sender, EventArgs e)
    {
        if (_applyingPreset || _presetSelector.SelectedItem is not DesktopPreset preset || preset.Name == "Custom")
        {
            return;
        }

        _applyingPreset = true;
        try
        {
            _commandSelector.SelectedItem = preset.CommandName;
            if (!string.IsNullOrWhiteSpace(preset.Path))
            {
                _pathTextBox.Text = preset.Path;
            }
            else
            {
                _pathTextBox.Clear();
            }

            _maxInstructionsInput.Value = Math.Clamp(preset.MaxInstructions, (int)_maxInstructionsInput.Minimum, (int)_maxInstructionsInput.Maximum);
            _memoryCardA.Checked = preset.MemoryCardA;
            _memoryCardB.Checked = preset.MemoryCardB;
            _controllerButtons.SelectedItem = preset.ControllerButtons;
            _additionalArgs.Text = preset.AdditionalArguments;
            _fastForwardIdle.Checked = true;
            _traceCheck.Checked = false;
            _liveFrameCaptureMenuItem.Checked = true;
            _frameCaptureStrideInput.Value = 2;
            _verboseOutputMenuItem.Checked = false;
            _dumpMmio.Checked = false;
            _dumpRegisters.Checked = false;
            _tracePathTextBox.Clear();
        }
        finally
        {
            _applyingPreset = false;
        }

        UpdateModeState();
    }

    private void RegisterPreviewUpdateHandlers()
    {
        _pathTextBox.TextChanged += OnManualRunSettingChanged;
        _maxInstructionsInput.ValueChanged += OnManualRunSettingChanged;
        _frameCaptureStrideInput.ValueChanged += OnManualRunSettingChanged;
        _traceCheck.CheckedChanged += OnManualRunSettingChanged;
        _tracePathTextBox.TextChanged += OnManualRunSettingChanged;
        _dumpMmio.CheckedChanged += OnManualRunSettingChanged;
        _dumpRegisters.CheckedChanged += OnManualRunSettingChanged;
        _fastForwardIdle.CheckedChanged += OnManualRunSettingChanged;
        _memoryCardA.CheckedChanged += OnManualRunSettingChanged;
        _memoryCardB.CheckedChanged += OnManualRunSettingChanged;
        _controllerButtons.SelectedIndexChanged += OnManualRunSettingChanged;
        _additionalArgs.TextChanged += OnManualRunSettingChanged;
    }

    private void OnManualRunSettingChanged(object? sender, EventArgs e)
    {
        if (!_applyingPreset)
        {
            MarkCustomPreset();
        }

        SyncMenuItemsFromControls();
        UpdateCommandPreview();
    }

    private void MarkCustomPreset()
    {
        if (_applyingPreset || _presetSelector.SelectedIndex == 0)
        {
            return;
        }

        _applyingPreset = true;
        try
        {
            _presetSelector.SelectedIndex = 0;
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        RunCommandRequest? request = BuildRunCommandRequest();
        if (request is null)
        {
            SetStatus("Input needed");
            return;
        }

        using CancellationTokenSource cancellation = new();
        _activeRunCancellation = cancellation;
        SetBusyState(isBusy: true);
        StartScreenCapture(request.LiveFrameCapture);
        AppendOutput($"{Environment.NewLine}--- {DateTime.Now:G} {request.CommandName} ---{Environment.NewLine}");
        AppendOutput($"Artifacts: {request.ArtifactDirectory}{Environment.NewLine}");
        AppendOutput($"Command: {FormatCommandPreview(request, request.ArtifactDirectory)}{Environment.NewLine}");

        try
        {
            bool completed = await Task.Run(() => ExecuteSelectedCommand(request, cancellation.Token), cancellation.Token);
            SetStatus(cancellation.IsCancellationRequested ? "Cancelled" : completed ? "Completed" : "Failed");
        }
        catch (OperationCanceledException)
        {
            AppendOutput($"{Environment.NewLine}Run cancelled.{Environment.NewLine}");
            SetStatus("Cancelled");
        }
        catch (Exception exception)
        {
            AppendOutput($"{Environment.NewLine}Execution failed: {exception.Message}{Environment.NewLine}");
            SetStatus("Failed");
        }
        finally
        {
            StopScreenCapture();
            if (ReferenceEquals(_activeRunCancellation, cancellation))
            {
                _activeRunCancellation = null;
            }

            SetBusyState(isBusy: false);
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _activeRunCancellation?.Cancel();
        SetStatus("Cancelling...");
    }

    private RunCommandRequest? BuildRunCommandRequest()
    {
        string path = _pathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AppendOutput("Select a valid file path first.\n");
            return null;
        }

        return new RunCommandRequest(
            _commandSelector.Text,
            path,
            (int)_maxInstructionsInput.Value,
            _traceCheck.Checked,
            _tracePathTextBox.Text.Trim(),
            _verboseOutputMenuItem.Checked,
            _liveFrameCaptureMenuItem.Checked,
            (int)_frameCaptureStrideInput.Value,
            _dumpMmio.Checked,
            _dumpRegisters.Checked,
            _fastForwardIdle.Checked,
            _memoryCardA.Checked,
            _memoryCardB.Checked,
            _controllerButtons.Text,
            CreateArtifactDirectory(_commandSelector.Text, path),
            ParseAdditionalArguments(_additionalArgs.Text));
    }

    private void UpdateCommandPreview()
    {
        if (_commandPreview.IsDisposed)
        {
            return;
        }

        string path = _pathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            _commandPreview.Text = "Select an input file to preview the command.";
            return;
        }

        RunCommandRequest request = new(
            _commandSelector.Text,
            path,
            (int)_maxInstructionsInput.Value,
            _traceCheck.Checked,
            _tracePathTextBox.Text.Trim(),
            _verboseOutputMenuItem.Checked,
            _liveFrameCaptureMenuItem.Checked,
            (int)_frameCaptureStrideInput.Value,
            _dumpMmio.Checked,
            _dumpRegisters.Checked,
            _fastForwardIdle.Checked,
            _memoryCardA.Checked,
            _memoryCardB.Checked,
            _controllerButtons.Text,
            "<artifact-dir>",
            ParseAdditionalArguments(_additionalArgs.Text));
        _commandPreview.Text = FormatCommandPreview(request, "<artifact-dir>");
    }

    private void SetBusyState(bool isBusy)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<bool>(SetBusyState), isBusy);
            return;
        }

        _runButton.Enabled = !isBusy;
        _cancelButton.Enabled = isBusy;
        _clearButton.Enabled = !isBusy;
        _browseButton.Enabled = !isBusy;
        _presetSelector.Enabled = !isBusy;
        _commandSelector.Enabled = !isBusy;
        _pathTextBox.Enabled = !isBusy;
        _maxInstructionsInput.Enabled = !isBusy && IsRunMode;
        _frameCaptureStrideInput.Enabled = !isBusy && IsRunMode && _liveFrameCaptureMenuItem.Checked;
        _traceCheck.Enabled = !isBusy && IsRunMode;
        _tracePathTextBox.Enabled = !isBusy && IsRunMode && _traceCheck.Checked;
        _dumpMmio.Enabled = !isBusy && IsRunMode;
        _dumpRegisters.Enabled = !isBusy && IsRunMode;
        _fastForwardIdle.Enabled = !isBusy && IsRunMode;
        _memoryCardA.Enabled = !isBusy && IsRunMode && IsDiscMode;
        _memoryCardB.Enabled = !isBusy && IsRunMode && IsDiscMode;
        _controllerButtons.Enabled = !isBusy && IsRunMode;
        _additionalArgs.Enabled = !isBusy && IsRunMode;
        _statusLabel.Text = isBusy ? "Running..." : _statusLabel.Text;
        _runMenuItem.Enabled = !isBusy;
        _cancelRunMenuItem.Enabled = isBusy;
        _clearOutputMenuItem.Enabled = !isBusy;
        _presetsMenu.Enabled = !isBusy;
        _traceMenuItem.Enabled = !isBusy && IsRunMode;
        _verboseOutputMenuItem.Enabled = !isBusy && IsRunMode;
        _liveFrameCaptureMenuItem.Enabled = !isBusy && IsRunMode;
        _dumpMmioMenuItem.Enabled = !isBusy && IsRunMode;
        _dumpRegistersMenuItem.Enabled = !isBusy && IsRunMode;
        _fastForwardIdleMenuItem.Enabled = !isBusy && IsRunMode;
        _memoryCardAMenuItem.Enabled = !isBusy && IsRunMode && IsDiscMode;
        _memoryCardBMenuItem.Enabled = !isBusy && IsRunMode && IsDiscMode;
        _controllerMenu.Enabled = !isBusy && IsRunMode;
    }

    private void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(SetStatus), message);
            return;
        }

        _statusLabel.Text = message;
    }

    private bool ExecuteSelectedCommand(RunCommandRequest request, CancellationToken cancellationToken)
    {
        return request.CommandName switch
        {
            "Run DOL" => ExecuteRunDol(request, cancellationToken),
            "Run Disc" => ExecuteRunDisc(request, cancellationToken),
            "Disc Info" => ExecuteDiscInfo(request),
            "DOL Info" => ExecuteDolInfo(request),
            _ => UnknownCommand(request.CommandName),
        };
    }

    private bool UnknownCommand(string commandName)
    {
        AppendOutput($"Unknown command: {commandName}\n");
        return false;
    }

    private bool ExecuteRunDol(RunCommandRequest request, CancellationToken cancellationToken)
    {
        if (!TryBuildRunOptions("run-dol", request, out RunDolOptions? options))
        {
            return false;
        }
        RunDolOptions runOptions = options!;
        DesktopRunTelemetry telemetry = DesktopRunTelemetry.Start(request);

        AppendOutput($"Starting run-dol for: {request.Path}\n");
        DolFile dol = DolFile.Load(request.Path);
        GameCubeBus bus = new();
        GameCubeStandaloneBoot.PrepareMemory(bus.Memory);
        using CountingTextWriter outputWriter = CreateOutputWriter(request);
        DolRunner runner = new(outputWriter, CreateWriter());
        EmulationFrameCapture frameCapture = new(
            runOptions.FrameAddress,
            runOptions.FrameWidth ?? DefaultFrameWidth,
            runOptions.FrameHeight ?? DefaultFrameHeight,
            runOptions.FrameFormat,
            request.FrameCaptureStride);
        Action<DolRunStep>? stepObserver = request.LiveFrameCapture
            ? step => CaptureFrameFromStep(step, frameCapture, telemetry)
            : null;
        int exitCode = runner.Run(dol, runOptions, bus, stepObserver, cancellationToken);
        telemetry.Finish(exitCode, outputWriter.BytesWritten, frameCapture);
        AppendTelemetry(telemetry.FormatSummary());
        AppendOutput(telemetry.FormatSummary());
        AppendOutput($"run-dol exited with code {exitCode}\n");
        return exitCode == 0;
    }

    private bool ExecuteRunDisc(RunCommandRequest request, CancellationToken cancellationToken)
    {
        if (!TryBuildRunOptions("run-disc", request, out RunDolOptions? options))
        {
            return false;
        }
        RunDolOptions runOptions = options!;
        DesktopRunTelemetry telemetry = DesktopRunTelemetry.Start(request);

        AppendOutput($"Starting run-disc for: {request.Path}\n");
        using DiscImageReader reader = DiscImageReader.Open(request.Path);
        DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
        GameCubeBus bus = new(reader);
        GameCubeDiscBoot.PrepareMemory(reader, bus.Memory);
        EmulationFrameCapture frameCapture = new(
            runOptions.FrameAddress,
            runOptions.FrameWidth ?? DefaultFrameWidth,
            runOptions.FrameHeight ?? DefaultFrameHeight,
            runOptions.FrameFormat,
            request.FrameCaptureStride);
        using CountingTextWriter outputWriter = CreateOutputWriter(request);
        DolRunner runner = new(outputWriter, CreateWriter());
        Action<DolRunStep>? stepObserver = request.LiveFrameCapture
            ? step => CaptureFrameFromStep(step, frameCapture, telemetry)
            : null;
        int exitCode = runner.Run(dol, runOptions, bus, stepObserver, cancellationToken);
        telemetry.Finish(exitCode, outputWriter.BytesWritten, frameCapture);
        AppendTelemetry(telemetry.FormatSummary());
        AppendOutput(telemetry.FormatSummary());
        AppendOutput($"run-disc exited with code {exitCode}\n");
        return exitCode == 0;
    }

    private bool TryBuildRunOptions(string command, RunCommandRequest request, out RunDolOptions? options)
    {
        options = null;

        Directory.CreateDirectory(request.ArtifactDirectory);
        List<string> args = BuildCommandArguments(command, request, request.ArtifactDirectory);

        using StringWriter parseOutput = new();
        if (!RunDolOptions.TryParse([.. args], out options, parseOutput))
        {
            AppendOutput(parseOutput.ToString());
            AppendOutput("Failed to parse options. Check the Advanced Options tab.\n");
            return false;
        }

        return true;
    }

    private static List<string> BuildCommandArguments(string command, RunCommandRequest request, string artifactDirectory)
    {
        List<string> args =
        [
            command,
            request.Path,
            "--max-instructions",
            request.MaxInstructions.ToString(CultureInfo.InvariantCulture),
        ];

        args.Add("--run-summary");
        args.Add(Path.Combine(artifactDirectory, "run-summary.json"));
        if (!request.VerboseRunnerOutput)
        {
            args.Add("--quiet");
        }

        if (ShouldAddDefaultGxFrameDump(request.AdditionalArguments))
        {
            args.Add("--dump-gx-frame");
            args.Add(Path.Combine(artifactDirectory, "gx-frame.png"));
        }

        if (request.TraceEnabled)
        {
            args.Add("--trace");
            args.Add("--trace-file");
            args.Add(string.IsNullOrWhiteSpace(request.TracePath)
                ? Path.Combine(artifactDirectory, "tail.trace")
                : request.TracePath);
        }

        if (request.DumpMmio)
        {
            args.Add("--dump-mmio");
        }

        if (!request.DumpRegisters)
        {
            args.Add("--no-registers");
        }

        if (request.FastForwardIdle)
        {
            args.Add("--fast-forward-idle");
        }

        if (request.MemoryCardA)
        {
            args.Add("--memory-card-a");
        }

        if (request.MemoryCardB)
        {
            args.Add("--memory-card-b");
        }

        if (!string.Equals(request.ControllerButtons, "None", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--controller-button");
            args.Add(request.ControllerButtons.ToLowerInvariant());
        }

        args.AddRange(request.AdditionalArguments);
        return args;
    }

    private static bool ShouldAddDefaultGxFrameDump(IReadOnlyList<string> additionalArguments)
    {
        if (additionalArguments.Any(static arg => string.Equals(arg, "--dump-gx-frame", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return additionalArguments.Any(static arg =>
            string.Equals(arg, "--gx-frame-source", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--gx-frame-copy-index", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--gx-frame-max-draws", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--gx-frame-skip-draws", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatCommandPreview(RunCommandRequest request, string artifactDirectory)
    {
        string command = request.CommandName switch
        {
            "Run DOL" => "run-dol",
            "Run Disc" => "run-disc",
            "Disc Info" => "disc-info",
            "DOL Info" => "dol-info",
            _ => request.CommandName,
        };

        if (command is "disc-info" or "dol-info")
        {
            return $"ngcsharp {command} {QuoteArgument(request.Path)}";
        }

        List<string> args = BuildCommandArguments(command, request, artifactDirectory);
        return "ngcsharp " + string.Join(" ", args.Select(QuoteArgument));
    }

    private static string? FindWorkspaceFile(params string[] pathParts)
    {
        foreach (string root in EnumerateWorkspaceRoots())
        {
            string candidate = Path.GetFullPath(Path.Combine([root, .. pathParts]));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateWorkspaceRoots()
    {
        string? current = Environment.CurrentDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            yield return current;
            current = Directory.GetParent(current)?.FullName;
        }

        current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            yield return current;
            current = Directory.GetParent(current)?.FullName;
        }
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string CreateArtifactDirectory(string commandName, string inputPath)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string inputName = Path.GetFileNameWithoutExtension(inputPath);
        string safeName = Regex.Replace(inputName, @"[^A-Za-z0-9._-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "input";
        }

        string safeCommand = Regex.Replace(commandName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return Path.GetFullPath(Path.Combine("artifacts", "desktop-runs", $"{timestamp}-{safeCommand}-{safeName}"));
    }

    private static List<string> ParseAdditionalArguments(string rawArguments)
    {
        List<string> arguments = [];
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return arguments;
        }

        foreach (Match match in Regex.Matches(rawArguments, @"""([^""]*)""|\S+").Cast<Match>())
        {
            string value = match.Value;
            if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
            {
                value = value[1..^1];
            }

            arguments.Add(value);
        }

        return arguments;
    }

    private bool ExecuteDiscInfo(RunCommandRequest request)
    {
        using DiscImageReader reader = DiscImageReader.Open(request.Path);
        DiscImageInfo info = reader.Info;
        GameCubeDiscLayout layout = GameCubeDiscLayout.Read(reader);

        StringBuilder lines = new();
        lines.AppendLine($"Path:       {Path.GetFullPath(info.Path)}");
        lines.AppendLine($"Kind:       {info.Kind}");
        lines.AppendLine($"Game ID:    {info.DiscHeader.GameId}");
        lines.AppendLine($"Maker:      {info.DiscHeader.MakerCode}");
        lines.AppendLine($"Disc #:     {info.DiscHeader.DiscNumber}");
        lines.AppendLine($"Version:    {info.DiscHeader.Version}");
        lines.AppendLine($"Magic:      0x{info.DiscHeader.Magic:X8} {(info.DiscHeader.IsGameCubeDisc ? "(GameCube)" : "(unexpected)")}");
        lines.AppendLine($"Title:      {info.DiscHeader.Title}");
        lines.AppendLine($"Disc size:  0x{info.DiscSize:X}");
        lines.AppendLine($"File size:  0x{info.ContainerSize:X}");
        lines.AppendLine($"Main DOL:   0x{layout.MainDolOffset:X8}");
        lines.AppendLine($"FST:        0x{layout.FileSystemTableOffset:X8} + 0x{layout.FileSystemTableSize:X}");
        lines.AppendLine($"FST max:    0x{layout.FileSystemTableMaxSize:X}");

        if (info.Kind == DiscImageKind.Rvz)
        {
            lines.AppendLine($"RVZ ver:    {FormatVersion(info.RvzVersion.GetValueOrDefault())}");
            lines.AppendLine($"RVZ compat: {FormatVersion(info.RvzCompatibleVersion.GetValueOrDefault())}");
            lines.AppendLine($"RVZ comp:   {FormatRvzCompression(info.RvzCompression.GetValueOrDefault())}");
            lines.AppendLine($"RVZ level:  {info.RvzCompressionLevel}");
            lines.AppendLine($"RVZ chunk:  0x{info.RvzChunkSize:X}");
            lines.AppendLine($"NKit mark:  {(info.HasNkitMarker ? "yes" : "no")}");
        }

        AppendOutput(lines.ToString());
        return true;
    }

    private bool ExecuteDolInfo(RunCommandRequest request)
    {
        DolFile dol = DolFile.Load(request.Path);
        StringBuilder lines = new();
        lines.AppendLine($"Entry: 0x{dol.EntryPoint:X8}");
        lines.AppendLine($"BSS:   0x{dol.BssAddress:X8} + 0x{dol.BssSize:X}");

        foreach (DolSection section in dol.Sections)
        {
            lines.AppendLine($"{section.Name,-5} file=0x{section.FileOffset:X8} mem=0x{section.Address:X8} size=0x{section.Size:X}");
        }

        AppendOutput(lines.ToString());
        return true;
    }

    private void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (text.Length > MaxOutputCharacters)
        {
            text = text[^MaxOutputCharacters..];
        }

        if (InvokeRequired)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action<string>(AppendOutput), text);
            return;
        }

        _output.AppendText(text);
        TrimOutputIfNeeded();
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
    }

    private void TrimOutputIfNeeded()
    {
        int overflow = _output.TextLength - MaxOutputCharacters;
        if (overflow <= 0)
        {
            return;
        }

        int trimCharacters = Math.Max(overflow, _output.TextLength - OutputTrimTargetCharacters);
        _output.Select(0, trimCharacters);
        _output.SelectedText = string.Empty;
    }

    private void AppendTelemetry(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (InvokeRequired)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action<string>(AppendTelemetry), text);
            return;
        }

        _telemetryOutput.AppendText(text);
        _telemetryOutput.SelectionStart = _telemetryOutput.TextLength;
        _telemetryOutput.ScrollToCaret();
    }

    private void ClearLogs()
    {
        _output.Clear();
        _telemetryOutput.Clear();
    }

    private void OnScreenFrameTick(object? sender, EventArgs e)
    {
        Bitmap? frame;
        lock (_screenFrameLock)
        {
            frame = _pendingScreenFrame;
            _pendingScreenFrame = null;
        }

        if (frame is null)
        {
            return;
        }

        Image? old = _screen.Image;
        _screen.Image = frame;
        old?.Dispose();
    }

    private void CaptureFrameFromStep(DolRunStep step, EmulationFrameCapture frameCapture, DesktopRunTelemetry telemetry)
    {
        telemetry.ObserveStep(step);
        if (!ShouldPollLiveFrameCapture(step))
        {
            return;
        }

        if (!frameCapture.TryCapture(step.Bus, out Bitmap? frame))
        {
            return;
        }

        lock (_screenFrameLock)
        {
            _pendingScreenFrame?.Dispose();
            _pendingScreenFrame = frame;
        }
    }

    private static bool ShouldPollLiveFrameCapture(DolRunStep step)
    {
        return step.IsInitial
            || step.IsFinal
            || step.ExecutedInstructions % LiveFrameCapturePollInstructions == 0;
    }

    private void StartScreenCapture(bool enabled)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<bool>(StartScreenCapture), enabled);
            return;
        }

        lock (_screenFrameLock)
        {
            _pendingScreenFrame?.Dispose();
            _pendingScreenFrame = null;
        }

        if (_screen.Image is not null)
        {
            _screen.Image.Dispose();
            _screen.Image = null;
        }

        if (enabled)
        {
            _screenUpdateTimer.Start();
        }
    }

    private void StopScreenCapture()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(StopScreenCapture));
            return;
        }

        _screenUpdateTimer.Stop();

        lock (_screenFrameLock)
        {
            _pendingScreenFrame?.Dispose();
            _pendingScreenFrame = null;
        }
    }

    private sealed class EmulationFrameCapture(
        uint? explicitAddress,
        int width,
        int height,
        FramebufferPixelFormat format,
        int frameStride)
    {
        private ulong _lastFrame = ulong.MaxValue;

        public int ObservedFrames { get; private set; }

        public int CapturedFrames { get; private set; }

        public bool TryCapture(GameCubeBus bus, out Bitmap? frame)
        {
            frame = null;
            ulong frameCounter = bus.VideoFrameCounter;
            if (frameCounter <= _lastFrame)
            {
                return false;
            }

            _lastFrame = frameCounter;
            ObservedFrames++;
            if (frameStride > 1 && frameCounter % (ulong)frameStride != 0)
            {
                return false;
            }

            if (!TryResolveFramebufferAddress(bus, out uint address, out int resolvedWidth, out int resolvedHeight, out FramebufferPixelFormat resolvedFormat))
            {
                return false;
            }

            if (!ValidateFramebufferSettings(resolvedWidth, resolvedHeight))
            {
                return false;
            }

            try
            {
                byte[] rgb = FramebufferDumper.CaptureRgb(bus.Memory, address, resolvedWidth, resolvedHeight, resolvedFormat);
                frame = CreateBitmapFromRgb(rgb, resolvedWidth, resolvedHeight);
                CapturedFrames++;
                return true;
            }
            catch (AddressTranslationException)
            {
                return false;
            }
        }

        private bool TryResolveFramebufferAddress(
            GameCubeBus bus,
            out uint address,
            out int resolvedWidth,
            out int resolvedHeight,
            out FramebufferPixelFormat resolvedFormat)
        {
            resolvedWidth = width;
            resolvedHeight = height;
            resolvedFormat = format;

            if (explicitAddress is uint candidateAddress && TryNormalizeVideoInterfaceAddress(candidateAddress, preferShifted: false, out address))
            {
                return true;
            }

            uint bestSplitAddress = 0;
            foreach ((uint highRegister, uint lowRegister) in ViFramebufferRegisterPairs)
            {
                if (bus.TryGetMmioValue(highRegister, out uint highValue)
                    && bus.TryGetMmioValue(lowRegister, out uint lowValue)
                    && TryNormalizeVideoInterfaceAddress(CombineVideoInterfaceAddress(highValue, lowValue), preferShifted: false, out uint resolvedAddress))
                {
                    bestSplitAddress = Math.Max(bestSplitAddress, resolvedAddress);
                }
            }

            if (bestSplitAddress != 0)
            {
                address = bestSplitAddress;
                return true;
            }

            foreach (uint register in ViFramebufferRegisters)
            {
                if (bus.TryGetMmioValue(register, out uint value) && TryNormalizeVideoInterfaceAddress(value, preferShifted: true, out address))
                {
                    return true;
                }
            }

            address = 0;
            return false;
        }

        private static bool ValidateFramebufferSettings(int frameWidth, int frameHeight)
        {
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                return false;
            }

            long totalPixels = (long)frameWidth * frameHeight;
            return totalPixels > 0 && totalPixels <= VideoFrameLimit;
        }

        private static uint CombineVideoInterfaceAddress(uint highValue, uint lowValue)
        {
            return ((highValue & 0xFF) << 16) | (lowValue & 0xFFFF);
        }

        private static bool TryNormalizeVideoInterfaceAddress(uint value, bool preferShifted, out uint normalized)
        {
            uint shifted = (value & 0x00FF_FFFF) << 5;
            if (preferShifted && shifted != value && GameCubeAddress.TryTranslateMainRam(shifted, out _))
            {
                normalized = shifted;
                return shifted != 0;
            }

            if (GameCubeAddress.TryTranslateMainRam(value, out _))
            {
                normalized = value;
                return value != 0;
            }

            if (!preferShifted && shifted != value && GameCubeAddress.TryTranslateMainRam(shifted, out _))
            {
                normalized = shifted;
                return shifted != 0;
            }

            normalized = 0;
            return false;
        }

        private static Bitmap CreateBitmapFromRgb(byte[] rgb, int frameWidth, int frameHeight)
        {
            Bitmap bitmap = new(frameWidth, frameHeight, PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, frameWidth, frameHeight), ImageLockMode.WriteOnly, bitmap.PixelFormat);

            try
            {
                int sourceStride = frameWidth * 3;
                for (int y = 0; y < frameHeight; y++)
                {
                    IntPtr destination = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(rgb, y * sourceStride, destination, sourceStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }
    }

    private static string FormatVersion(uint value)
    {
        return $"{value >> 24}.{(value >> 16) & 0xFF:00}";
    }

    private static string FormatRvzCompression(uint value)
    {
        return value switch
        {
            0 => "none",
            2 => "bzip2",
            3 => "lzma",
            4 => "lzma2",
            5 => "zstd",
            _ => $"unknown ({value})",
        };
    }

    private sealed class DesktopRunTelemetry
    {
        private const int MemorySampleIntervalInstructions = 1_000_000;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly long _startAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        private readonly long _startManagedBytes = GC.GetTotalMemory(forceFullCollection: false);
        private readonly string _summaryPath;
        private readonly bool _liveFrameCapture;
        private readonly int _frameCaptureStride;
        private readonly bool _verboseRunnerOutput;
        private long _nextMemorySampleInstruction = MemorySampleIntervalInstructions;

        private DesktopRunTelemetry(RunCommandRequest request)
        {
            _summaryPath = Path.Combine(request.ArtifactDirectory, "run-summary.json");
            _liveFrameCapture = request.LiveFrameCapture;
            _frameCaptureStride = request.FrameCaptureStride;
            _verboseRunnerOutput = request.VerboseRunnerOutput;
            PeakManagedBytes = _startManagedBytes;
        }

        public long LastObservedInstructions { get; private set; }

        public long ExecutedInstructions { get; private set; }

        public long AllocatedBytes { get; private set; }

        public long EndManagedBytes { get; private set; }

        public long PeakManagedBytes { get; private set; }

        public long RunnerStdoutBytes { get; private set; }

        public int ExitCode { get; private set; }

        public int ObservedFrames { get; private set; }

        public int CapturedFrames { get; private set; }

        public string? StopReason { get; private set; }

        public double? SummaryTotalMilliseconds { get; private set; }

        public double? SummaryEmulationMilliseconds { get; private set; }

        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public static DesktopRunTelemetry Start(RunCommandRequest request) => new(request);

        public void ObserveStep(DolRunStep step)
        {
            LastObservedInstructions = Math.Max(LastObservedInstructions, step.ExecutedInstructions);
            if (step.IsInitial || (step.ExecutedInstructions < _nextMemorySampleInstruction && !step.IsFinal))
            {
                return;
            }

            SampleManagedMemory();
            while (_nextMemorySampleInstruction <= step.ExecutedInstructions)
            {
                _nextMemorySampleInstruction += MemorySampleIntervalInstructions;
            }
        }

        public void Finish(int exitCode, long runnerStdoutBytes, EmulationFrameCapture frameCapture)
        {
            _stopwatch.Stop();
            ExitCode = exitCode;
            RunnerStdoutBytes = runnerStdoutBytes;
            ObservedFrames = frameCapture.ObservedFrames;
            CapturedFrames = frameCapture.CapturedFrames;
            SampleManagedMemory();
            AllocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - _startAllocatedBytes);
            EndManagedBytes = GC.GetTotalMemory(forceFullCollection: false);
            PeakManagedBytes = Math.Max(PeakManagedBytes, EndManagedBytes);

            if (TryReadRunSummary(_summaryPath, out RunSummaryInfo summary))
            {
                ExecutedInstructions = summary.ExecutedInstructions ?? LastObservedInstructions;
                StopReason = summary.StopReason;
                SummaryTotalMilliseconds = summary.TotalMilliseconds;
                SummaryEmulationMilliseconds = summary.EmulationMilliseconds;
            }
            else
            {
                ExecutedInstructions = LastObservedInstructions;
            }
        }

        public string FormatSummary()
        {
            double elapsedSeconds = Math.Max(Elapsed.TotalSeconds, 0.001);
            double mips = ExecutedInstructions / elapsedSeconds / 1_000_000.0;
            string capture = _liveFrameCapture
                ? $"{CapturedFrames}/{ObservedFrames} frames captured, stride {_frameCaptureStride}"
                : "live capture off";
            string stdoutMode = _verboseRunnerOutput ? "shown" : "suppressed";
            string coreTiming = SummaryEmulationMilliseconds is double emulationMs
                ? $", core emu {emulationMs:N1} ms"
                : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"Telemetry: {Elapsed.TotalSeconds:N2}s wall, {ExecutedInstructions:N0} instr, {mips:N2} MIPS, {capture}, allocated {FormatBytes(AllocatedBytes)}, managed {FormatBytes(_startManagedBytes)} -> {FormatBytes(EndManagedBytes)} peak {FormatBytes(PeakManagedBytes)}, runner stdout {FormatBytes(RunnerStdoutBytes)} {stdoutMode}, exit {ExitCode}, stop {StopReason ?? "unknown"}{coreTiming}{Environment.NewLine}");
        }

        private void SampleManagedMemory()
        {
            PeakManagedBytes = Math.Max(PeakManagedBytes, GC.GetTotalMemory(forceFullCollection: false));
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            double value = bytes;
            int unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:N1} {units[unitIndex]}";
        }

        private static bool TryReadRunSummary(string path, out RunSummaryInfo summary)
        {
            summary = default;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;
                long? executedInstructions = TryGetInt64(root, "executedInstructions");
                string? stopReason = TryGetString(root, "stopReason");
                double? totalMs = null;
                double? emulationMs = null;
                if (root.TryGetProperty("timings", out JsonElement timings))
                {
                    totalMs = TryGetDouble(timings, "totalMs");
                    emulationMs = TryGetDouble(timings, "emulationMs");
                }

                summary = new RunSummaryInfo(executedInstructions, stopReason, totalMs, emulationMs);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static long? TryGetInt64(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt64(out long value)
                ? value
                : null;
        }

        private static double? TryGetDouble(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetDouble(out double value)
                ? value
                : null;
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private readonly record struct RunSummaryInfo(
            long? ExecutedInstructions,
            string? StopReason,
            double? TotalMilliseconds,
            double? EmulationMilliseconds);
    }

    private sealed class CountingTextWriter(TextWriter inner) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;

        public long BytesWritten { get; private set; }

        public override void Write(char value)
        {
            BytesWritten += Encoding.GetByteCount(value.ToString());
            inner.Write(value);
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            BytesWritten += Encoding.GetByteCount(value);
            inner.Write(value);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write(Environment.NewLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private CountingTextWriter CreateOutputWriter(RunCommandRequest request)
    {
        return new CountingTextWriter(request.VerboseRunnerOutput ? CreateWriter() : TextWriter.Null);
    }

    private UiTextWriter CreateWriter()
    {
        return new UiTextWriter(AppendOutput);
    }

    private sealed class UiTextWriter(Action<string> append) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            append(value.ToString());
        }

        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                append(value);
            }
        }

        public override void WriteLine(string? value)
        {
            append((value ?? string.Empty) + Environment.NewLine);
        }
    }
}
