using BetterJoy.Config;
using BetterJoy.Controller;
using BetterJoy.Exceptions;
using BetterJoy.Hardware.SubCommand;
using BetterJoy.Properties;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BetterJoy.Forms;

public partial class MainForm : Form
{
    public enum ControllerAction
    {
        None,
        Calibrate,
        Remap, // not implemented
        Locate
    }

    private readonly List<Button> _con;

    public readonly MainFormConfig Config;

    public readonly List<KeyValuePair<string, short[]>> CalibrationMotionData = [];
    public readonly List<KeyValuePair<string, ushort[]>> CaliSticksData = [];

    private int _count;
    private Timer? _countDown;

    private ControllerAction _currentAction = ControllerAction.None;
    private bool _selectController = false;

    private bool _closing = false;
    private bool _close = false;

    private readonly Logger? _logger;

    public MainForm(Logger? logger)
    {
        InitializeComponent();
        InitializeConsoleTextBox();

        _logger = logger;
        if (_logger != null)
        {
            _logger.OnMessageLogged += OnMessageLogged;
        }
        else
        {
            Print("Error initializing the log file.");
        }

        Config = new(_logger);
        Config.Update();

        if (!Config.AllowCalibration)
        {
            btn_calibrate.Hide();
        }

        SetIcon();
        SetTaskbarIcon();
        version_lbl.Text = Program.ProgramVersion;

        _con = [con1, con2, con3, con4, con5, con6, con7, con8];

        InitializeConfigPanel();

        Shown += MainForm_Shown;
    }

    private void SetIcon()
    {
        var oldIcon = Icon;
        Icon = Resources.betterjoy_icon;
        oldIcon?.Dispose();
    }

    private void SetTaskbarIcon()
    {
        var oldIcon = notifyIcon.Icon;
        notifyIcon.Icon = Resources.betterjoy_icon;
        oldIcon?.Dispose();
    }

    private Control GenerateConfigItem(string? key, string? value)
    {
        Control childControl;

        if (key == "DebugType" ||
            key == "GyroToJoyOrMouse" ||
            key == "DoNotRejoinJoycons")
        {
            var comboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            var items = new List<string>();

            if (key == "DebugType")
            {
                var enumValues = Enum.GetValues<Joycon.DebugType>();
                items.AddRange([.. enumValues.Cast<Joycon.DebugType>().Select(e => e.ToString().ToLower())]);
            }
            else if (key == "GyroToJoyOrMouse")
            {
                items.AddRange(["none", "joy_left", "joy_right", "mouse"]);
            }
            else if (key == "DoNotRejoinJoycons")
            {
                var enumValues = Enum.GetValues<Joycon.Orientation>();
                items.AddRange([.. enumValues.Cast<Joycon.Orientation>().Select(e => e.ToString().ToLower())]);
            }

            int index = 0;
            foreach (var item in items)
            {
                comboBox.Items.Add(item);

                if (item.Equals(value, StringComparison.CurrentCultureIgnoreCase))
                {
                    comboBox.SelectedIndex = index;
                }
                ++index;
            }

            comboBox.SelectedIndexChanged += ConfigItemChanged;
            childControl = comboBox;
        }
        else if (value == "true" || value == "false")
        {
            var checkBox = new CheckBox { Checked = bool.Parse(value) };
            checkBox.CheckedChanged += ConfigItemChanged;
            childControl = checkBox;
        }
        else
        {
            childControl = new TextBox { Text = value };
        }

        return childControl;
    }

    private void InitializeConfigPanel()
    {
        const float defaultDpi = 96f;
        var myConfigs = ConfigurationManager.AppSettings.AllKeys;
        settingsTable.RowStyles.Clear();

        for (var i = 0; i != myConfigs.Length; i++)
        {
            settingsTable.RowCount++;
            settingsTable.RowStyles.Add(
                new RowStyle
                {
                    SizeType = SizeType.Absolute,
                    Height = MathF.Round(30f * AutoScaleDimensions.Height / defaultDpi)
                }
            );
            settingsTable.Controls.Add(
                new Label
                {
                    Text = myConfigs[i],
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left
                },
                0,
                i
            );

            var key = myConfigs[i];
            var value = ConfigurationManager.AppSettings[key];
            var childControl = GenerateConfigItem(key, value);
            childControl.AutoSize = true;
            childControl.Anchor = AnchorStyles.Left;
            childControl.Size = new Size(settingsTable.Size.Width, 0); // the control won't take more space than available thanks to AutoSize

            settingsTable.Controls.Add(childControl, 1, i);
        }
    }

    private void InitializeConsoleTextBox()
    {
        // Trick to have bottom padding in the console control
        console.Controls.Add(new Label()
        {
            Height = 6,
            Dock = DockStyle.Bottom,
            BackColor = console.BackColor,
        });
    }

    private void HideToTray(bool init = false)
    {
        if (notifyIcon.Visible && !init)
        {
            return;
        }

        WindowState = FormWindowState.Minimized;
        notifyIcon.Visible = true;

        if (!init)
        {
            notifyIcon.BalloonTipText = "Click the tray icon to maximise!";
            notifyIcon.ShowBalloonTip(0);
        }

        ShowInTaskbar = false;
        Hide();
    }

    private void ShowFromTray(bool init = false)
    {
        if (!notifyIcon.Visible && !init)
        {
            return;
        }

        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        notifyIcon.Visible = false;

        // Scroll to end
        console.SelectionStart = console.Text.Length;
        console.ScrollToCaret();
    }

    private void MainForm_Resize(object sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void notifyIcon_MouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ShowFromTray();
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
        Settings.Init(CalibrationMotionData, CaliSticksData);

        startInTrayBox.Checked = Settings.IntValue("StartInTray") == 1;

        if (Settings.IntValue("StartInTray") == 1)
        {
            HideToTray(true);
        }
        else
        {
            ShowFromTray(true);
        }

        try
        {
            startOnBoot.Checked = IsRunOnBootSet();
        }
        catch (Exception ex)
        {
            _logger?.Log("Cannot retrieve run on boot state.", ex);
        }

        SystemEvents.PowerModeChanged += OnPowerChange;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Refresh();
    }

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        Program.Start();
    }

    private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (_close)
        {
            // We can close now (happens after the Close() call)
            return;
        }

        e.Cancel = true;

        if (_closing)
        {
            // Prevent disposing the UI thread when the user hammers the close button
            return;
        }

        _closing = true;
        Enabled = false;

        _logger?.Log("Closing...");
        SystemEvents.PowerModeChanged -= OnPowerChange;
        await Program.Stop();
        _logger?.Log("Closed.", Logger.LogLevel.Debug);

        _close = true;
        Close(); // we're done with the UI thread, close it for real now
    }

    private void OnPowerChange(object s, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Resume:
                _logger?.Log("Resume session.");
                Program.SetSuspended(false);
                break;
            case PowerModes.Suspend:
                _logger?.Log("Suspend session.");
                Program.SetSuspended(true);
                break;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // avoid blurry icon after resolution/dpi changes
        SetTaskbarIcon();
    }

    private void showToolStripMenuItem_Click(object sender, EventArgs e)
    {
        ShowFromTray();
    }

    private void exitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        lb_github.LinkVisited = true;
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/d3xMachina/BetterJoy",
            UseShellExecute = true
        });
    }

    private void OnMessageLogged(string message, Logger.LogLevel level, Exception? e)
    {
        if (level == Logger.LogLevel.Debug)
        {
            return;
        }

        if (e == null)
        {
            Print(message);
        }
        else
        {
            Print(message, e);
        }
    }

    public void Print(string message)
    {
        // https://stackoverflow.com/questions/519233/writing-to-a-textbox-from-another-thread
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Print), message);
            return;
        }

        console.AppendText(message + Environment.NewLine);
    }

    public void Print(string message, Exception e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string, Exception>(Print), message, e);
            return;
        }

        console.AppendText($"{message} {e.Display()}{Environment.NewLine}");
    }

    private async Task LocateController(Joycon controller)
    {
        SetLocate(false);

        controller.SetRumble(160f, 320f, 1f, 1f);
        await Task.Delay(300);
        controller.SetRumble(160f, 320f, 0f, 0f);
    }

    private async void ConBtnClick(object? sender, EventArgs e)
    {
        var button = sender as Button;

        if (button?.Tag is not Joycon controller)
        {
            return;
        }

        var action = _currentAction;

        if (_selectController)
        {
            switch (action)
            {
                case ControllerAction.Remap:
                    ShowReassignDialog(controller);
                    break;
                case ControllerAction.Calibrate:
                    StartCalibrate(controller);
                    break;
                case ControllerAction.Locate:
                    await LocateController(controller);
                    break;
            }

            _selectController = false;
            return;
        }

        if (action != ControllerAction.None)
        {
            return;
        }

        if (!controller.IsJoycon)
        {
            return;
        }

        Program.Mgr.JoinOrSplitJoycon(controller);
    }

    private void startInTrayBox_Click(object sender, EventArgs e)
    {
        Settings.SetValue("StartInTray", startInTrayBox.Checked ? "1" : "0");
        Settings.Save();
    }

    private void startOnBoot_Click(object sender, EventArgs e)
    {
        try
        {
            SetRunOnBoot(startOnBoot.Checked);
        }
        catch (Exception ex)
        {
            _logger?.Log("Cannot set run on boot.", ex);
            startOnBoot.Checked = !startOnBoot.Checked;
        }
    }

    private static void SetRunOnBoot(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key != null && Application.ProductName is string programName)
        {
            var programPath = Application.ExecutablePath;

            if (enable)
            {
                key.SetValue(programName, programPath);
            }
            else
            {
                key.DeleteValue(programName);
            }
        }
    }

    private static bool IsRunOnBootSet()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        if (key != null)
        {
            var programPath = Application.ExecutablePath;
            var programName = Application.ProductName;
            var value = key.GetValue(programName);

            if (value is string path)
            {
                return path.Equals(programPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private void btn_open3rdP_Click(object sender, EventArgs e)
    {
        using var partyForm = new _3rdPartyControllers();
        partyForm.ShowDialog(this);
    }

    private async void settingsApply_Click(object sender, EventArgs e)
    {
        var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
        var settings = configFile.AppSettings.Settings;

        for (var row = 0; row < ConfigurationManager.AppSettings.AllKeys.Length; row++)
        {
            var valCtl = settingsTable.GetControlFromPosition(1, row);

            if (settingsTable.GetControlFromPosition(0, row)?.Text is string keyCtl &&
                settings[keyCtl] != null)
            {
                if (valCtl is CheckBox checkBox)
                {
                    settings[keyCtl].Value = checkBox.Checked.ToString().ToLower();
                }
                else if (valCtl is ComboBox comboBox)
                {
                    settings[keyCtl].Value = comboBox.SelectedItem!.ToString()!.ToLower();
                }
                else if (valCtl is TextBox textBox)
                {
                    settings[keyCtl].Value = textBox.Text.ToLower();
                }
                else
                {
                    throw new NotImplementedException("control not implemented");
                }
            }
        }

        try
        {
            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);

            await ApplyConfig();
            _logger?.Log("Configuration applied.");
        }
        catch (ConfigurationErrorsException ex)
        {
            _logger?.Log("Error writing app settings.", ex);
        }
    }

    public async Task ApplyConfig()
    {
        var oldConfig = Config.Clone();
        Config.Update();

        if (oldConfig.AllowCalibration != Config.AllowCalibration)
        {
            btn_calibrate.Visible = Config.AllowCalibration;
        }

        await Program.ApplyConfig();
    }

    private static void Restart()
    {
        var info = new ProcessStartInfo
        {
            Arguments = "",
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Application.ExecutablePath
        };
        Process.Start(info);
        Application.Exit();
    }

    private void foldLbl_Click(object sender, EventArgs e)
    {
        rightPanel.Visible = !rightPanel.Visible;
        foldLbl.Text = rightPanel.Visible ? "<" : ">";
    }

    private async void ConfigItemChanged(object? sender, EventArgs e)
    {
        var control = sender as Control;
        var coord = settingsTable.GetPositionFromControl(control);

        try
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;
            if (settingsTable.GetControlFromPosition(coord.Column - 1, coord.Row)?.Text is string keyCtl &&
                settings[keyCtl] != null)
            {
                if (sender is CheckBox checkBox)
                {
                    settings[keyCtl].Value = checkBox.Checked.ToString().ToLower();
                }
                else if (sender is ComboBox comboBox)
                {
                    settings[keyCtl].Value = comboBox.SelectedItem!.ToString()!.ToLower();
                }
                else
                {
                    throw new NotImplementedException("control not implemented");
                }
            }

            configFile.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);

            await ApplyConfig();
            _logger?.Log("Configuration applied.");
        }
        catch (ConfigurationErrorsException ex)
        {
            _logger?.Log("Error writing app settings.", ex);
        }
    }

    private void SetCalibrateButtonText(bool ongoing = false)
    {
        btn_calibrate.Text = ongoing ? "Select..." : "Calibrate";
    }

    private void SetCalibrate(bool calibrate = true)
    {
        if ((_currentAction == ControllerAction.Calibrate && calibrate) ||
            (_currentAction != ControllerAction.Calibrate && !calibrate))
        {
            return;
        }

        _currentAction = calibrate ? ControllerAction.Calibrate : ControllerAction.None;
        SetCalibrateButtonText(calibrate);
    }

    private void StartCalibrate(object sender, EventArgs e)
    {
        switch (_currentAction)
        {
            case ControllerAction.Calibrate:
                SetCalibrate(false);
                return;
            case ControllerAction.Locate:
                SetLocate(false);
                break;
        }

        SetCalibrate();

        var controllers = GetActiveControllers();

        switch (controllers.Count)
        {
            case 0:
                return;
            case 1:
                StartCalibrate(controllers.First());
                break;
            default:
                _selectController = true;
                _logger?.Log("Click on a controller to calibrate.");
                break;
        }
    }

    private void SetLocateButtonText(bool ongoing = false)
    {
        btn_locate.Text = ongoing ? "Select..." : "Locate";
    }

    private void SetLocate(bool locate = true)
    {
        if ((_currentAction == ControllerAction.Locate && locate) ||
            (_currentAction != ControllerAction.Locate && !locate))
        {
            return;
        }

        _currentAction = locate ? ControllerAction.Locate : ControllerAction.None;
        SetLocateButtonText(locate);
    }

    private async void StartLocate(object sender, EventArgs e)
    {
        switch (_currentAction)
        {
            case ControllerAction.Locate:
                SetLocate(false);
                return;
            case ControllerAction.Calibrate:
                SetCalibrate(false);
                break;
        }

        SetLocate();

        var controllers = GetActiveControllers();

        switch (controllers.Count)
        {
            case 0:
                return;
            case 1:
                await LocateController(controllers.First());
                break;
            default:
                _selectController = true;
                _logger?.Log("Click on a controller to locate.");
                break;
        }
    }

    private List<Joycon> GetActiveControllers()
    {
        var controllers = new List<Joycon>();

        foreach (var button in _con)
        {
            if (button is { Enabled: true, Tag: Joycon controller })
            {
                controllers.Add(controller);
            }
        }

        return controllers;
    }

    private void StartCalibrate(Joycon controller)
    {
        SetCalibrateButtonText();
        btn_calibrate.Enabled = false;

        _countDown = new Timer();
        _count = 4;
        _countDown.Interval = 1000;
        _countDown.Tag = controller;

        if (controller.MotionSupported())
        {
            _countDown.Tick += CountDownMotion;
            CountDownMotion(null, null);
        }
        else
        {
            _countDown.Tick += CountDownSticksCenter;
            CountDownSticksCenter(null, null);
        }

        _countDown.Start();
    }

    private void btn_reassign_open_Click(object sender, EventArgs e)
    {
        using var mapForm = new Reassign();
        mapForm.ActionAssigned += (sender, e) => Program.UpdateInputEvents();
        mapForm.ShowDialog(this);
    }

    private void ShowReassignDialog(Joycon controller)
    {
        throw new NotImplementedException();
    }

    private void CountDownMotion(object? sender, EventArgs? e)
    {
        if (_countDown?.Tag is not Joycon controller)
        {
            return;
        }

        if (controller.State != Joycon.Status.MotionDataOk)
        {
            CancelCalibrate(controller, true);
            return;
        }

        if (_count == 0)
        {
            console.Text = $"Calibrating motion...{Environment.NewLine}";
            _countDown.Stop();

            controller.StartMotionCalibration();
            _count = 3;
            _countDown = new Timer();
            _countDown.Tick += CalcMotionData;
            _countDown.Interval = 1000;
            _countDown.Tag = controller;
            _countDown.Start();
        }
        else
        {
            console.Text = $"Please keep the controller flat.{Environment.NewLine}";
            console.Text += $"Calibration will start in {_count} seconds.{Environment.NewLine}";
            _count--;
        }
    }

    private void CalcMotionData(object? sender, EventArgs e)
    {
        if (_countDown?.Tag is not Joycon controller)
        {
            return;
        }

        if (controller.State != Joycon.Status.MotionDataOk)
        {
            CancelCalibrate(controller, true);
            return;
        }

        if (_count == 0)
        {
            _countDown.Stop();
            controller.StopMotionCalibration();

            if (controller.CalibrationMotionDatas.Count == 0)
            {
                _logger?.Log("No motion data received, proceed to stick calibration anyway. Is the controller working ?", Logger.LogLevel.Warning);
            }
            else
            {
                var motionData = GetOrInitCalibrationMotionData(controller.SerialOrMac);

                var rnd = new Random();

                var xG = new List<int>();
                var yG = new List<int>();
                var zG = new List<int>();
                var xA = new List<int>();
                var yA = new List<int>();
                var zA = new List<int>();

                foreach (var calibrationData in controller.CalibrationMotionDatas)
                {
                    xG.Add(calibrationData.Gyroscope.X);
                    yG.Add(calibrationData.Gyroscope.Y);
                    zG.Add(calibrationData.Gyroscope.Z);
                    xA.Add(calibrationData.Accelerometer.X);
                    yA.Add(calibrationData.Accelerometer.Y);
                    zA.Add(calibrationData.Accelerometer.Z);
                }

                motionData[0] = (short)QuickselectMedian(xG, rnd.Next);
                motionData[1] = (short)QuickselectMedian(yG, rnd.Next);
                motionData[2] = (short)QuickselectMedian(zG, rnd.Next);
                motionData[3] = (short)QuickselectMedian(xA, rnd.Next);
                motionData[4] = (short)QuickselectMedian(yA, rnd.Next);
                motionData[5] = (short)QuickselectMedian(zA, rnd.Next);

                console.Text += $"Motion calibration completed!!!{Environment.NewLine}";

                Settings.SaveCalibrationMotionData(CalibrationMotionData);
                controller.GetActiveMotionData();
            }

            ClearCalibrateDatas(controller);

            _countDown = new Timer();
            _count = 5;
            _countDown.Tick += CountDownSticksCenter;
            _countDown.Interval = 1000;
            _countDown.Tag = controller;
            CountDownSticksCenter(null, null);
            _countDown.Start();
        }
        else
        {
            _count--;
        }
    }

    private void CountDownSticksCenter(object? sender, EventArgs? e)
    {
        if (_countDown?.Tag is not Joycon controller)
        {
            return;
        }

        if (controller.State != Joycon.Status.MotionDataOk)
        {
            CancelCalibrate(controller, true);
            return;
        }

        if (_count == 0)
        {
            _countDown.Stop();
            controller.StartSticksCalibration();

            console.Text = $"Calibrating Sticks center position...{Environment.NewLine}";

            _count = 3;
            _countDown = new Timer();
            _countDown.Tick += CalcSticksCenterData;
            _countDown.Interval = 1000;
            _countDown.Tag = controller;
            _countDown.Start();
        }
        else
        {
            console.Text = $"Please keep the sticks at the center position.{Environment.NewLine}";
            console.Text += $"Calibration will start in {_count} seconds.{Environment.NewLine}";
            _count--;
        }
    }

    private void CalcSticksCenterData(object? sender, EventArgs e)
    {
        if (_countDown?.Tag is not Joycon controller)
        {
            return;
        }

        if (controller.State != Joycon.Status.MotionDataOk)
        {
            CancelCalibrate(controller, true);
            return;
        }

        if (_count == 0)
        {
            _countDown.Stop();
            controller.StopSticksCalibration();

            if (controller.CalibrationStickDatas.Count == 0)
            {
                _logger?.Log("No stick positions received, calibration canceled. Is the controller working ?", Logger.LogLevel.Warning);
                CancelCalibrate(controller);
                return;
            }

            var stickData = GetOrInitCaliSticksData(controller.SerialOrMac);
            var leftStickData = stickData.AsSpan(0, 6);
            var rightStickData = stickData.AsSpan(6, 6);

            var rnd = new Random();

            var xS1 = new List<int>();
            var yS1 = new List<int>();
            var xS2 = new List<int>();
            var yS2 = new List<int>();

            foreach (var calibrationData in controller.CalibrationStickDatas)
            {
                xS1.Add(calibrationData.Stick1.X);
                yS1.Add(calibrationData.Stick1.Y);
                xS2.Add(calibrationData.Stick2.X);
                yS2.Add(calibrationData.Stick2.Y);
            }

            leftStickData[2] = (ushort)Math.Round(QuickselectMedian(xS1, rnd.Next));
            leftStickData[3] = (ushort)Math.Round(QuickselectMedian(yS1, rnd.Next));

            rightStickData[2] = (ushort)Math.Round(QuickselectMedian(xS2, rnd.Next));
            rightStickData[3] = (ushort)Math.Round(QuickselectMedian(yS2, rnd.Next));

            ClearCalibrateDatas(controller);

            console.Text += $"Sticks center position calibration completed!!!{Environment.NewLine}";

            _count = 5;
            _countDown = new Timer();
            _countDown.Tick += CountDownSticksMinMax;
            _countDown.Interval = 1000;
            _countDown.Tag = controller;
            CountDownSticksMinMax(null, null);
            _countDown.Start();
        }
        else
        {
            _count--;
        }
    }

    private void CountDownSticksMinMax(object? sender, EventArgs? e)
    {
        if (_countDown?.Tag is not Joycon controller)
        {
            return;
        }

        if (controller.State != Joycon.Status.MotionDataOk)
        {
            CancelCalibrate(controller, true);
            return;
        }

        if (_count == 0)
        {
            _countDown.Stop();
            controller.StartSticksCalibration();

            console.Text = $"Calibrating Sticks min and max position...{Environment.NewLine}";

            _count = 5;
            _countDown = new Timer();
            _countDown.Tick += CalcSticksMinMaxData;
            _countDown.Interval = 1000;
            _countDown.Tag = controller;
            _countDown.Start();
        }
        else
        {
            console.Text = $"Please move the sticks in a circle when the calibration starts.{Environment.NewLine}";
            console.Text += $"Calibration will start in {_count} seconds.{Environment.NewLine}";
            _count--;
        }
    }

    private void CalcSticksMinMaxData(object? sender, EventArgs e)
    {
        if (_countDown?.Tag is not Joycon controller)
        {
            return;
        }

        if (controller.State != Joycon.Status.MotionDataOk)
        {
            CancelCalibrate(controller, true);
            return;
        }

        if (_count == 0)
        {
            _countDown.Stop();
            controller.StopSticksCalibration();

            if (controller.CalibrationStickDatas.Count == 0)
            {
                _logger?.Log("No stick positions received, calibration canceled. Is the controller working ?", Logger.LogLevel.Warning);
                CancelCalibrate(controller);
                return;
            }

            var stickData = GetOrInitCaliSticksData(controller.SerialOrMac);
            var leftStickData = stickData.AsSpan(0, 6);
            var rightStickData = stickData.AsSpan(6, 6);

            var xS1 = new List<ushort>();
            var yS1 = new List<ushort>();
            var xS2 = new List<ushort>();
            var yS2 = new List<ushort>();

            foreach (var calibrationData in controller.CalibrationStickDatas)
            {
                xS1.Add(calibrationData.Stick1.X);
                yS1.Add(calibrationData.Stick1.Y);
                xS2.Add(calibrationData.Stick2.X);
                yS2.Add(calibrationData.Stick2.Y);
            }

            leftStickData[0] = (ushort)Math.Abs(xS1.Max() - leftStickData[2]);
            leftStickData[1] = (ushort)Math.Abs(yS1.Max() - leftStickData[3]);
            leftStickData[4] = (ushort)Math.Abs(leftStickData[2] - xS1.Min());
            leftStickData[5] = (ushort)Math.Abs(leftStickData[3] - yS1.Min());

            rightStickData[0] = (ushort)Math.Abs(xS2.Max() - rightStickData[2]);
            rightStickData[1] = (ushort)Math.Abs(yS2.Max() - rightStickData[3]);
            rightStickData[4] = (ushort)Math.Abs(rightStickData[2] - xS2.Min());
            rightStickData[5] = (ushort)Math.Abs(rightStickData[3] - yS2.Min());

            ClearCalibrateDatas(controller);

            console.Text += $"Sticks min and max position calibration completed!!!{Environment.NewLine}";

            Settings.SaveCaliSticksData(CaliSticksData);
            controller.GetActiveSticksData();

            CancelCalibrate(controller);
        }
        else
        {
            _count--;
        }
    }

    private static void ClearCalibrateDatas(Joycon controller)
    {
        controller.StopMotionCalibration(true);
        controller.StopSticksCalibration(true);
    }

    private void CancelCalibrate(Joycon controller, bool disconnected = false)
    {
        if (disconnected)
        {
            _logger?.Log("Controller disconnected, calibration canceled.");
        }

        SetCalibrate(false);
        btn_calibrate.Enabled = true;

        ClearCalibrateDatas(controller);
    }

    private static double QuickselectMedian(List<int> l, Func<int, int> pivotFn)
    {
        if (l.Count == 0)
        {
            return 0;
        }

        var ll = l.Count;
        if (ll % 2 == 1)
        {
            return Quickselect(l, ll / 2, pivotFn);
        }

        return 0.5 * (Quickselect(l, ll / 2 - 1, pivotFn) + Quickselect(l, ll / 2, pivotFn));
    }

    private static int Quickselect(List<int> l, int k, Func<int, int> pivotFn)
    {
        if (l.Count == 1 && k == 0)
        {
            return l[0];
        }

        var pivot = l[pivotFn(l.Count)];
        var lows = l.Where(x => x < pivot).ToList();
        var highs = l.Where(x => x > pivot).ToList();
        var pivots = l.Where(x => x == pivot).ToList();
        if (k < lows.Count)
        {
            return Quickselect(lows, k, pivotFn);
        }

        if (k < lows.Count + pivots.Count)
        {
            return pivots[0];
        }

        return Quickselect(highs, k - lows.Count - pivots.Count, pivotFn);
    }

    public short[] GetOrInitCalibrationMotionData(string serNum) =>
        ActiveCalibrationMotionData(serNum) ?? InitCalibrationMotionData(serNum);

    public short[]? ActiveCalibrationMotionData(string serNum)
    {
        foreach (var calibrationMotionDatum in CalibrationMotionData)
        {
            if (calibrationMotionDatum.Key == serNum)
            {
                return calibrationMotionDatum.Value;
            }
        }

        return null;
    }

    public short[] InitCalibrationMotionData(string serNum)
    {
        var arr = new short[6];
        CalibrationMotionData.Add(
            new KeyValuePair<string, short[]>(
                serNum,
                arr
            )
        );

        return arr;
    }

    public ushort[] GetOrInitCaliSticksData(string serNum) =>
        ActiveCaliSticksData(serNum) ?? InitCaliSticksData(serNum);

    public ushort[]? ActiveCaliSticksData(string serNum)
    {
        foreach (var caliSticksDatum in CaliSticksData)
        {
            if (caliSticksDatum.Key == serNum)
            {
                return caliSticksDatum.Value;
            }
        }

        return null;
    }

    public ushort[] InitCaliSticksData(string serNum)
    {
        const int stickCaliSize = 6;
        var arr = new ushort[stickCaliSize * 2];
        CaliSticksData.Add(
            new KeyValuePair<string, ushort[]>(
                serNum,
                arr
            )
        );
        return arr;
    }

    public void Tooltip(string msg)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Tooltip), msg);
            return;
        }

        notifyIcon.Visible = true;
        notifyIcon.BalloonTipText = msg;
        notifyIcon.ShowBalloonTip(0);
    }

    public void SetBatteryColor(Joycon controller, BatteryLevel batteryLevel)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<Joycon, BatteryLevel>(SetBatteryColor), controller, batteryLevel);
            return;
        }

        foreach (var button in _con)
        {
            if (button.Tag != controller)
            {
                continue;
            }

            button.BackColor = batteryLevel switch
            {
                BatteryLevel.Full => Color.FromArgb(0xAA, 0, 150, 0),
                BatteryLevel.Medium => Color.FromArgb(0xAA, 150, 230, 0),
                BatteryLevel.Low => Color.FromArgb(0xAA, 250, 210, 0),
                BatteryLevel.Critical => Color.FromArgb(0xAA, 250, 150, 0),
                _ => Color.FromArgb(0xAA, 230, 0, 0),
            };
        }
    }

    public void SetCharging(Joycon controller, bool charging)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<Joycon, bool>(SetCharging), controller, charging);
            return;
        }

        foreach (var button in _con)
        {
            if (button.Tag != controller)
            {
                continue;
            }

            bool joined = controller.Other != null;
            SetControllerImage(button, controller.Type, joined, charging);
        }
    }

    private static void SetControllerImage(Button button, Joycon.ControllerType controllerType, bool joined = false, bool charging = false)
    {
        Bitmap temp;
        switch (controllerType)
        {
            case Joycon.ControllerType.JoyconLeft:
                if (joined)
                {
                    temp = charging ? Resources.jc_left_charging : Resources.jc_left;
                }
                else
                {
                    temp = charging ? Resources.jc_left_s_charging : Resources.jc_left_s;
                }
                break;
            case Joycon.ControllerType.JoyconRight:
                if (joined)
                {
                    temp = charging ? Resources.jc_right_charging : Resources.jc_right;
                }
                else
                {
                    temp = charging ? Resources.jc_right_s_charging : Resources.jc_right_s;
                }
                break;
            case Joycon.ControllerType.Pro:
                temp = charging ? Resources.pro_charging : Resources.pro;
                break;
            case Joycon.ControllerType.SNES:
                temp = charging ? Resources.snes_charging : Resources.snes;
                break;
            case Joycon.ControllerType.NES:
                temp = charging ? Resources.nes_charging : Resources.nes;
                break;
            case Joycon.ControllerType.FamicomI:
                temp = charging ? Resources.famicom_i_charging : Resources.famicom_i;
                break;
            case Joycon.ControllerType.FamicomII:
                temp = charging ? Resources.famicom_ii_charging : Resources.famicom_ii;
                break;
            case Joycon.ControllerType.N64:
                temp = charging ? Resources.n64_charging : Resources.n64;
                break;
            default:
                temp = Resources.cross;
                break;
        }

        SetBackgroundImage(button, temp);
    }

    public static void SetBackgroundImage(Button button, Bitmap bitmap)
    {
        var oldImage = button.BackgroundImage;
        button.BackgroundImage = bitmap;
        oldImage?.Dispose();
    }

    public void AddController(Joycon controller)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<Joycon>(AddController), controller);
            return;
        }

        int nbControllers = GetActiveControllers().Count;
        if (nbControllers == 0)
        {
            btn_calibrate.Enabled = true;
            btn_locate.Enabled = true;
        }
        else if (nbControllers == _con.Count || controller.PadId >= _con.Count)
        {
            return;
        }

        var button = _con[controller.PadId];
        button.Tag = controller; // assign controller to button
        button.Enabled = true;
        button.Click += ConBtnClick;
        SetControllerImage(button, controller.Type);
    }

    public void RemoveController(Joycon controller)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<Joycon>(RemoveController), controller);
            return;
        }

        int nbControllers = GetActiveControllers().Count;
        if (nbControllers == 0 || controller.PadId >= _con.Count)
        {
            return;
        }

        var button = _con[controller.PadId];
        if (!button.Enabled)
        {
            return;
        }

        button.BackColor = Color.FromArgb(0x00, SystemColors.Control);
        button.Tag = null;
        button.Enabled = false;
        button.Click -= ConBtnClick;
        SetBackgroundImage(button, Resources.cross);

        if (nbControllers == 1)
        {
            btn_calibrate.Enabled = false;
            btn_locate.Enabled = false;
        }
    }

    public void JoinJoyconToSelf(Joycon controller) => JoinJoycon(controller, controller);

    public void JoinJoycon(Joycon controller, Joycon other)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<Joycon, Joycon>(JoinJoycon), controller, other);
            return;
        }

        foreach (var button in _con)
        {
            if (button.Tag != controller && button.Tag != other)
            {
                continue;
            }

            var currentJoycon = button.Tag == controller ? controller : other;
            SetControllerImage(button, currentJoycon.Type, true, currentJoycon.Charging);
        }
    }

    public void SplitJoycon(Joycon controller, Joycon other)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<Joycon, Joycon>(SplitJoycon), controller, other);
            return;
        }

        foreach (var button in _con)
        {
            if (button.Tag != controller && button.Tag != other)
            {
                continue;
            }

            var currentJoycon = button.Tag == controller ? controller : other;
            SetControllerImage(button, currentJoycon.Type, false, currentJoycon.Charging);
        }
    }
}
