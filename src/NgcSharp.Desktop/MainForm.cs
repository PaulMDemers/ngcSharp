using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using NgcSharp.App;
using NgcSharp.Core;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace NgcSharp.Desktop;

public sealed class MainForm : Form
{
    private const int DefaultFrameWidth = 640;
    private const int DefaultFrameHeight = 480;
    private const int VideoFrameLimit = 4_194_304;

    private readonly SplitContainer _mainSplit = new()
    {
        Dock = DockStyle.Fill,
        FixedPanel = FixedPanel.Panel1,
        SplitterWidth = 1,
        SplitterDistance = 330,
    };

    private readonly TableLayoutPanel _controlPanel = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        Padding = new Padding(12),
        BackColor = Color.FromArgb(30, 32, 36),
    };

    private readonly ComboBox _commandSelector = new()
    {
        Dock = DockStyle.Top,
        DropDownStyle = ComboBoxStyle.DropDownList,
    };

    private readonly ComboBox _presetSelector = new()
    {
        Dock = DockStyle.Top,
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
        Dock = DockStyle.Top,
        Minimum = 0,
        Maximum = int.MaxValue,
        Value = 1_000_000,
        Increment = 100_000,
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
        Checked = true,
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
        Dock = DockStyle.Top,
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

    private readonly Label _statusLabel = new()
    {
        Text = "Ready",
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = Color.Gainsboro,
        Padding = new Padding(0, 8, 0, 0),
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

    private readonly TextBox _additionalArgs = new()
    {
        Multiline = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f),
        BackColor = Color.FromArgb(24, 24, 24),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = "Additional ngcsharp options, e.g. --fast-forward-idle --dump-gx-frame frame.png",
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

        BuildControlPanel();
        BuildContentArea();

        Controls.Add(_mainSplit);
        _mainSplit.Panel1.Controls.Add(_controlPanel);
        _mainSplit.Panel2.Controls.Add(_rightSplit);

        _presetSelector.Items.AddRange(BuildPresets());
        _presetSelector.SelectedIndex = 0;
        _commandSelector.Items.AddRange(["Run DOL", "Run Disc", "Disc Info", "DOL Info"]);
        _commandSelector.SelectedIndex = 0;
        _controllerButtons.Items.AddRange(["None", "A", "Start", "B", "X", "Y", "A+Start"]);
        _controllerButtons.SelectedIndex = 0;

        _traceCheck.CheckedChanged += (_, _) => _tracePathTextBox.Enabled = _traceCheck.Checked;
        _browseButton.Click += OnBrowseClicked;
        _runButton.Click += OnRunClicked;
        _cancelButton.Click += OnCancelClicked;
        _clearButton.Click += (_, _) => _output.Clear();
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
        bool DumpMmio,
        bool DumpRegisters,
        bool FastForwardIdle,
        bool MemoryCardA,
        bool MemoryCardB,
        string ControllerButtons,
        string ArtifactDirectory,
        IReadOnlyList<string> AdditionalArguments);

    private void BuildControlPanel()
    {
        _controlPanel.RowStyles.Clear();
        _controlPanel.RowCount = 0;

        AddControlRow(CreateTitleBlock());
        AddControlRow(CreateLabeledControl("Preset", _presetSelector));
        AddControlRow(CreateLabeledControl("Mode", _commandSelector));
        AddControlRow(CreateFilePicker());
        AddControlRow(CreateLabeledControl("Max instructions", _maxInstructionsInput));
        AddControlRow(CreateOptionsGroup());
        AddControlRow(CreateLabeledControl("Controller", _controllerButtons));
        AddControlRow(CreateRunButtons());
        AddControlRow(_statusLabel, true);
    }

    private Control CreateTitleBlock()
    {
        TableLayoutPanel title = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 14),
        };
        title.Controls.Add(new Label
        {
            Text = "ngcSharp",
            Dock = DockStyle.Top,
            Font = new Font(Font.FontFamily, 18f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
        });
        title.Controls.Add(new Label
        {
            Text = "GameCube emulator workbench",
            Dock = DockStyle.Top,
            ForeColor = Color.FromArgb(170, 176, 186),
            AutoSize = true,
        });
        return title;
    }

    private Control CreateFilePicker()
    {
        TableLayoutPanel picker = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 10),
        };
        picker.Controls.Add(CreateSectionLabel("Input file"));

        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Top,
            Height = 30,
            ColumnCount = 2,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        row.Controls.Add(_pathTextBox, 0, 0);
        row.Controls.Add(_browseButton, 1, 0);
        picker.Controls.Add(row);
        return picker;
    }

    private Control CreateOptionsGroup()
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
        };
        panel.Controls.Add(_traceCheck);
        panel.Controls.Add(_tracePathTextBox);
        panel.Controls.Add(_dumpMmio);
        panel.Controls.Add(_dumpRegisters);
        panel.Controls.Add(_fastForwardIdle);
        panel.Controls.Add(_memoryCardA);
        panel.Controls.Add(_memoryCardB);
        return panel;
    }

    private Control CreateRunButtons()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Top,
            Height = 40,
            ColumnCount = 3,
            Margin = new Padding(0, 8, 0, 12),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        row.Controls.Add(_runButton, 0, 0);
        row.Controls.Add(_cancelButton, 1, 0);
        row.Controls.Add(_clearButton, 2, 0);
        return row;
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 196, 206),
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
    }

    private static Control CreateLabeledControl(string label, Control control)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 10),
        };
        panel.Controls.Add(CreateSectionLabel(label));
        panel.Controls.Add(control);
        return panel;
    }

    private void AddControlRow(Control control, bool fill = false)
    {
        _controlPanel.RowStyles.Add(new RowStyle(fill ? SizeType.Percent : SizeType.AutoSize, fill ? 100 : 0));
        _controlPanel.Controls.Add(control, 0, _controlPanel.RowCount++);
    }

    private void BuildContentArea()
    {
        _screenPanel.Controls.Add(_screen);
        _rightSplit.Panel1.Controls.Add(_screenPanel);

        TabPage outputPage = new("Output");
        outputPage.Controls.Add(_output);
        TabPage advancedPage = new("Advanced Options");
        advancedPage.Controls.Add(_additionalArgs);
        TabPage commandPage = new("Command Preview");
        commandPage.Controls.Add(_commandPreview);
        _bottomTabs.TabPages.Add(outputPage);
        _bottomTabs.TabPages.Add(advancedPage);
        _bottomTabs.TabPages.Add(commandPage);
        _rightSplit.Panel2.Controls.Add(_bottomTabs);
    }

    private object[] BuildPresets()
    {
        return
        [
            new DesktopPreset("Custom", "Run DOL", null, 1_000_000, MemoryCardA: false, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Quick DOL smoke - 1M", "Run DOL", FindWorkspaceFile("fixtures", "devkitpro", "xfb-smoke", "xfb-smoke.dol"), 1_000_000, MemoryCardA: false, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("One frame-ish - 8M", "Run Disc", FindWorkspaceFile("Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz"), 8_000_000, MemoryCardA: true, MemoryCardB: false, "A", string.Empty),
            new DesktopPreset("Retail boot probe - 20M", "Run Disc", FindWorkspaceFile("Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz"), 20_000_000, MemoryCardA: true, MemoryCardB: false, "A", string.Empty),
            new DesktopPreset("Sonic Sega splash - 35M", "Run Disc", FindWorkspaceFile("Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz"), 35_000_000, MemoryCardA: true, MemoryCardB: false, "A", "--gx-frame-source largest-display-copy"),
            new DesktopPreset("Sonic resource flag watch - 35M", "Run Disc", FindWorkspaceFile("Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz"), 35_000_000, MemoryCardA: true, MemoryCardB: false, "A", "--watch-write-range 0x803ADCE0 0x4 --watch-write-after 0x01C00000 --stop-after-write-watch 12 --watch-limit 24 --trace-pc-after 0x01C00000 --profile-after 0x01C00000 --profile-pc 24"),
            new DesktopPreset("Pikmin boot probe - 20M", "Run Disc", FindWorkspaceFile("Pikmin (USA).rvz"), 20_000_000, MemoryCardA: true, MemoryCardB: false, "None", "--gx-frame-source largest-display-copy"),
            new DesktopPreset("Mario Kart Debug startup - 5M", "Run Disc", FindWorkspaceFile("Mario Kart - Double Dash!! (USA) (Debug).rvz"), 5_000_000, MemoryCardA: true, MemoryCardB: false, "None", string.Empty),
            new DesktopPreset("Deep retail probe - 50M", "Run Disc", FindWorkspaceFile("Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz"), 50_000_000, MemoryCardA: true, MemoryCardB: false, "A", "--gx-frame-source largest-display-copy"),
        ];
    }

    private void UpdateModeState()
    {
        bool runMode = IsRunMode;
        bool discMode = IsDiscMode;

        _browseButton.Text = discMode ? "Disc..." : "DOL...";
        _maxInstructionsInput.Enabled = runMode;
        _traceCheck.Enabled = runMode;
        _tracePathTextBox.Enabled = runMode && _traceCheck.Checked;
        _dumpMmio.Enabled = runMode;
        _dumpRegisters.Enabled = runMode;
        _fastForwardIdle.Enabled = runMode;
        _memoryCardA.Enabled = runMode && discMode;
        _memoryCardB.Enabled = runMode && discMode;
        _controllerButtons.Enabled = runMode;
        _additionalArgs.Enabled = runMode;
        _runButton.Text = runMode ? "Run" : "Inspect";

        if (!discMode)
        {
            _memoryCardA.Checked = false;
            _memoryCardB.Checked = false;
        }
        else if (runMode && !_memoryCardA.Checked && !_memoryCardB.Checked)
        {
            _memoryCardA.Checked = true;
        }

        UpdateCommandPreview();
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
        StartScreenCapture();
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

        AppendOutput($"Starting run-dol for: {request.Path}\n");
        DolFile dol = DolFile.Load(request.Path);
        GameCubeBus bus = new();
        GameCubeStandaloneBoot.PrepareMemory(bus.Memory);
        DolRunner runner = new(CreateWriter(), CreateWriter());
        EmulationFrameCapture frameCapture = new(
            runOptions.FrameAddress,
            runOptions.FrameWidth ?? DefaultFrameWidth,
            runOptions.FrameHeight ?? DefaultFrameHeight,
            runOptions.FrameFormat);
        int executed = runner.Run(dol, runOptions, bus, step => CaptureFrameFromStep(step, frameCapture), cancellationToken);
        AppendOutput($"run-dol completed: {executed} instructions\n");
        return true;
    }

    private bool ExecuteRunDisc(RunCommandRequest request, CancellationToken cancellationToken)
    {
        if (!TryBuildRunOptions("run-disc", request, out RunDolOptions? options))
        {
            return false;
        }
        RunDolOptions runOptions = options!;

        AppendOutput($"Starting run-disc for: {request.Path}\n");
        using DiscImageReader reader = DiscImageReader.Open(request.Path);
        DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
        GameCubeBus bus = new(reader);
        GameCubeDiscBoot.PrepareMemory(reader, bus.Memory);
        EmulationFrameCapture frameCapture = new(
            runOptions.FrameAddress,
            runOptions.FrameWidth ?? DefaultFrameWidth,
            runOptions.FrameHeight ?? DefaultFrameHeight,
            runOptions.FrameFormat);
        DolRunner runner = new(CreateWriter(), CreateWriter());
        int executed = runner.Run(dol, runOptions, bus, step => CaptureFrameFromStep(step, frameCapture), cancellationToken);
        AppendOutput($"run-disc completed: {executed} instructions\n");
        return true;
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
        args.Add("--dump-gx-frame");
        args.Add(Path.Combine(artifactDirectory, "gx-frame.png"));

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

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendOutput), text);
            return;
        }

        _output.AppendText(text);
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
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

    private void CaptureFrameFromStep(DolRunStep step, EmulationFrameCapture frameCapture)
    {
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

    private void StartScreenCapture()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(StartScreenCapture));
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

        _screenUpdateTimer.Start();
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
        FramebufferPixelFormat format)
    {
        private ulong _lastFrame = ulong.MaxValue;

        public bool TryCapture(GameCubeBus bus, out Bitmap? frame)
        {
            frame = null;
            ulong frameCounter = bus.VideoFrameCounter;
            if (frameCounter <= _lastFrame)
            {
                return false;
            }

            _lastFrame = frameCounter;

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
