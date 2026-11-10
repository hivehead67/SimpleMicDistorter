namespace VoicePitchChanger;

public class MainForm : Form
{
    private readonly AudioEngine _engine = new AudioEngine();

    private readonly ComboBox _inputCombo = NewCombo();
    private readonly ComboBox _cableCombo = NewCombo();
    private readonly ComboBox _monitorCombo = NewCombo();
    private readonly Button _startButton = new Button { Text = "Start", Width = 120, Height = 32 };
    private readonly CheckBox _monitorCheck = new CheckBox { Text = "Monitor (hear yourself)", AutoSize = true };
    private readonly ProgressBar _meter = new ProgressBar { Width = 330, Height = 14, Maximum = 100 };
    private readonly System.Windows.Forms.Timer _meterTimer = new System.Windows.Forms.Timer { Interval = 50 };

    public MainForm()
    {
        Text = "Simple Mic Distorter";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(layout);

        void Row(string label, Control c)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 3) });
            layout.Controls.Add(c);
        }

        // devices
        Row("Microphone:", _inputCombo);
        Row("Output (virtual cable):", _cableCombo);
        Row("Monitor device:", _monitorCombo);

        // effects
        var e = _engine.Effects;
        Row("Pitch (semitones):", Slider(-12, 12, 0, 1, v => e.PitchSemitones = v, v => v.ToString("+0;-0;0")));
        Row("Gain (dB):", Slider(-20, 20, 0, 1, v => e.GainDb = v, v => v.ToString("+0;-0;0") + " dB"));
        Row("Noise gate:", Slider(-80, -20, -80, 1, v => e.GateThresholdDb = v, v => v <= -80 ? "Off" : v + " dB"));
        Row("Echo mix:", Slider(0, 100, 0, 0.01f, v => e.EchoMix = v, v => (v * 100).ToString("0") + "%"));
        Row("Echo delay (ms):", Slider(50, 1000, 250, 1, v => e.EchoDelayMs = v, v => v + " ms"));
        Row("Monitor volume:", Slider(0, 100, 80, 0.01f, v => { _monitorVolume = v; _engine.MonitorVolume = v; }, v => (v * 100).ToString("0") + "%"));

        Row("Output level:", _meter);
        Row("", _monitorCheck);

        var resetButton = new Button { Text = "Reset effects", Width = 120, Height = 32 };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(resetButton);
        Row("", buttons);

        _startButton.Click += (_, _) => ToggleStart();
        _monitorCheck.CheckedChanged += (_, _) => _engine.MonitorEnabled = _monitorCheck.Checked;
        resetButton.Click += (_, _) => ResetSliders(layout);
        _meterTimer.Tick += (_, _) =>
        {
            _meter.Value = _engine.IsRunning ? (int)Math.Clamp(_engine.Effects.PeakLevel * 100, 0, 100) : 0;
        };
        _meterTimer.Start();

        LoadDevices();
    }

    private static ComboBox NewCombo() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };

    private void LoadDevices()
    {
        // skip the cable
        var inputs = AudioEngine.GetInputDevices();
        foreach (var d in inputs) _inputCombo.Items.Add(d);
        int micIdx = inputs.FindIndex(n => !IsVirtual(n));
        if (micIdx >= 0) _inputCombo.SelectedIndex = micIdx;
        else if (inputs.Count > 0) _inputCombo.SelectedIndex = 0;

        var outputs = AudioEngine.GetOutputDevices();
        foreach (var d in outputs) _cableCombo.Items.Add(d);
        int cableIdx = outputs.FindIndex(IsVirtual);
        if (cableIdx >= 0) _cableCombo.SelectedIndex = cableIdx;
        else if (outputs.Count > 0) _cableCombo.SelectedIndex = 0;
        _cableMissing = cableIdx < 0;

        _monitorCombo.Items.Add("Default output device");
        foreach (var d in outputs) _monitorCombo.Items.Add(d);
        int speakerIdx = outputs.FindIndex(n => !IsVirtual(n));
        _monitorCombo.SelectedIndex = speakerIdx >= 0 ? speakerIdx + 1 : 0;
    }

    private static bool IsVirtual(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VoiceMeeter", StringComparison.OrdinalIgnoreCase);

    private bool _cableMissing;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_cableMissing) return;

        var answer = MessageBox.Show(
            "No virtual audio cable was found.\n\n" +
            "To use this app as a microphone in other programs you need VB-Audio Virtual Cable (free).\n\n" +
            "Download and run the installer now? (Files go to your Downloads folder.)\n\n" +
            "After installing, restart your PC and reopen this app, then pick \"CABLE Input\" as the output here " +
            "and \"CABLE Output\" as the mic in Discord, OBS, etc.",
            "Virtual Cable Not Found", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (answer == DialogResult.Yes)
            await DownloadAndInstallCableAsync();
    }

    // sorry if directly pulling from the site directly is not allowed but also i dont care too much
    private const string CableZipUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip";

    private async Task DownloadAndInstallCableAsync()
    {
        string originalTitle = Text;
        Enabled = false;
        try
        {
            string downloads = GetDownloadsFolder();
            string zipPath = Path.Combine(downloads, "VBCABLE_Driver_Pack46.zip");
            string extractDir = Path.Combine(downloads, "VBCABLE_Driver_Pack46");

            // download
            using (var http = new HttpClient())
            using (var response = await http.GetAsync(CableZipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.Create(zipPath);
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read));
                    readTotal += read;
                    Text = total > 0
                        ? $"Downloading VB-Cable... {readTotal * 100 / total}%"
                        : $"Downloading VB-Cable... {readTotal / 1024} KB";
                }
            }

            Text = "Extracting VB-Cable...";
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            string setupName = Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe";
            string setup = Directory.GetFiles(extractDir, setupName, SearchOption.AllDirectories).FirstOrDefault();
            if (setup == null)
            {
                MessageBox.Show($"Couldn't find {setupName} in the download. Opening the folder instead.");
                System.Diagnostics.Process.Start("explorer.exe", extractDir);
                return;
            }

            // needs admin
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = setup,
                WorkingDirectory = Path.GetDirectoryName(setup),
                UseShellExecute = true,
                Verb = "runas"
            });

            MessageBox.Show(
                "The VB-Cable installer has been opened. Click \"Install Driver\" in it.\n\n" +
                "When it finishes, restart your PC and reopen this app.",
                "VB-Cable", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show("Install cancelled (admin permission was declined).", "VB-Cable");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't download or install VB-Cable:\n" + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Text = originalTitle;
            Enabled = true;
        }
    }

    // actual downloads folder (onedrive moves it sometimes)
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out string path);

    private static string GetDownloadsFolder()
    {
        var downloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref downloadsId, 0, IntPtr.Zero, out string path) == 0 && Directory.Exists(path))
            return path;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private void ToggleStart()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            _startButton.Text = "Start";
            SetDeviceCombosEnabled(true);
            return;
        }

        if (_inputCombo.SelectedIndex < 0 || _cableCombo.SelectedIndex < 0)
        {
            MessageBox.Show("Pick a microphone and an output device first.");
            return;
        }

        try
        {
            _engine.MonitorEnabled = _monitorCheck.Checked;
            _engine.Start(_inputCombo.SelectedIndex, _cableCombo.SelectedIndex,
                          _monitorCombo.SelectedIndex - 1, _monitorVolume);
            _startButton.Text = "Stop";
            SetDeviceCombosEnabled(false);
        }
        catch (Exception ex)
        {
            _engine.Stop();
            MessageBox.Show("Could not start audio:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private float _monitorVolume = 0.8f;

    private void SetDeviceCombosEnabled(bool enabled)
    {
        _inputCombo.Enabled = _cableCombo.Enabled = _monitorCombo.Enabled = enabled;
    }

    // slider with label thing
    private Control Slider(int min, int max, int initial, float scale, Action<float> onChange, Func<float, string> format)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var bar = new TrackBar
        {
            Minimum = min, Maximum = max, Value = initial, Width = 260,
            TickStyle = TickStyle.None, AutoSize = false, Height = 28,
            Tag = initial
        };
        var label = new Label { AutoSize = false, Width = 65, TextAlign = ContentAlignment.MiddleLeft, Height = 28 };

        void UpdateValue()
        {
            float v = bar.Value * scale;
            label.Text = format(v);
            onChange(v);
        }
        bar.ValueChanged += (_, _) => UpdateValue();
        UpdateValue();

        panel.Controls.Add(bar);
        panel.Controls.Add(label);
        return panel;
    }

    private static void ResetSliders(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is TrackBar tb && tb.Tag is int init) tb.Value = init;
            else if (c.HasChildren) ResetSliders(c);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _meterTimer.Stop();
        _engine.Dispose();
        base.OnFormClosing(e);
    }
}
