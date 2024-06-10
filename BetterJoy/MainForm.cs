using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterJoy.Config;
using BetterJoy.Exceptions;
using BetterJoy.Properties;
using Microsoft.Win32;

namespace BetterJoy
{
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

        public readonly List<KeyValuePair<string, short[]>> CaliIMUData = new();
        public readonly List<KeyValuePair<string, ushort[]>> CaliSticksData = new();

        private int _count;
        private Timer _countDown;

        private ControllerAction _currentAction = ControllerAction.None;
        private bool _selectController = false;

        private const string _logFilePath = "LogDebug.txt";
        private Logger _logger;

        private bool _closing = false;
        private bool _close = false;

        public MainForm()
        {
            InitializeComponent();
            InitializeConsoleTextBox();
            InitializeLogger();
            LogDebugInfos();

            Config = new(this);
            Config.Update();

            if (!Config.AllowCalibration)
            {
                btn_calibrate.Hide();
            }

            version_lbl.Text = GetProgramVersion();

            _con = new List<Button> { con1, con2, con3, con4, con5, con6, con7, con8 };

            //list all options
            var myConfigs = ConfigurationManager.AppSettings.AllKeys;
            var childSize = new Size(180, 20);
            for (var i = 0; i != myConfigs.Length; i++)
            {
                settingsTable.RowCount++;
                settingsTable.Controls.Add(
                    new Label
                    {
                        Text = myConfigs[i],
                        TextAlign = ContentAlignment.BottomLeft,
                        AutoEllipsis = true,
                        Size = childSize,
                        AutoSize = false
                    },
                    0,
                    i
                );

                var value = ConfigurationManager.AppSettings[myConfigs[i]];
                Control childControl;
                if (value == "true" || value == "false")
                {
                    var checkBox = new CheckBox { Checked = bool.Parse(value), Size = childSize };
                    checkBox.CheckedChanged += checkBox_Changed;
                    childControl = checkBox;
                }
                else
                {
                    childControl = new TextBox { Text = value, Size = childSize };
                }

                settingsTable.Controls.Add(childControl, 1, i);
            }

            Shown += MainForm_Shown;
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

        private void InitializeLogger()
        {
            try
            {
                _logger = new Logger(_logFilePath);
            }
            catch (Exception e)
            {
                Log($"Error initializing log file.", e);
            }
        }

        private string GetProgramVersion()
        {
            var programVersion = Assembly.GetExecutingAssembly().GetName().Version;
            return $"v{programVersion.Major}.{programVersion.Minor}.{programVersion.Build}";
        }

        private void LogDebugInfos()
        {
            if (_logger == null)
            {
                return;
            }

            var programFullName = Assembly.GetExecutingAssembly().GetName();
            var osArch = Environment.Is64BitProcess ? "x64" : "x86";
            _logger.Log($"{programFullName.Name} {GetProgramVersion()}", Logger.LogLevel.Debug);
            _logger.Log($"Windows version: {Environment.OSVersion} {osArch}", Logger.LogLevel.Debug);
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
                notifyIcon.BalloonTipText = "Double click the tray icon to maximise!";
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
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = Resources.betterjoy_icon;
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

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ShowFromTray();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Settings.Init(CaliIMUData, CaliSticksData);

            startInTrayBox.Checked = Settings.IntValue("StartInTray") == 1;

            if (Settings.IntValue("StartInTray") == 1)
            {
                HideToTray(true);
            }
            else
            {
                ShowFromTray(true);
            }

            SystemEvents.PowerModeChanged += OnPowerChange;
            Refresh();
        }

        private void MainForm_Shown(object sender, EventArgs e)
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

            Log("Closing...");
            SystemEvents.PowerModeChanged -= OnPowerChange;
            await Program.Stop();
            Log($"Closed.", Logger.LogLevel.Debug);

            _close = true;
            Close(); // we're done with the UI thread, close it for real now
            _logger?.Dispose();
        }

        private void OnPowerChange(object s, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                    Log("Resume session.");
                    Program.SetSuspended(false);
                    break;
                case PowerModes.Suspend:
                    Log("Suspend session.");
                    Program.SetSuspended(true);
                    break;
            }
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

        public void Log(string message, Logger.LogLevel level = Logger.LogLevel.Info)
        {
            // https://stackoverflow.com/questions/519233/writing-to-a-textbox-from-another-thread
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, Logger.LogLevel>(Log), message, level);
                return;
            }

            if (level != Logger.LogLevel.Debug)
            {
                console.AppendText(message + Environment.NewLine);
            }
            
            _logger?.Log(message, level);
        }

        public void Log(string message, Exception e, Logger.LogLevel level = Logger.LogLevel.Error)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, Exception, Logger.LogLevel>(Log), message, e, level);
                return;
            }

            if (level != Logger.LogLevel.Debug)
            {
                console.AppendText($"{message} {e.Display()}{Environment.NewLine}");
            }
            
            _logger?.Log(message, e, level);
        }

        private async Task LocateController(Joycon controller)
        {
            SetLocate(false);

            controller.SetRumble(160.0f, 320.0f, 1.0f);
            await Task.Delay(300);
            controller.SetRumble(160.0f, 320.0f, 0);
        }

        private async void ConBtnClick(object sender, EventArgs e)
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

        private void startInTrayBox_CheckedChanged(object sender, EventArgs e)
        {
            Settings.SetValue("StartInTray", startInTrayBox.Checked ? "1" : "0");
            Settings.Save();
        }

        private void btn_open3rdP_Click(object sender, EventArgs e)
        {
            using var partyForm = new _3rdPartyControllers();
            partyForm.StartPosition = FormStartPosition.CenterParent;
            partyForm.ShowDialog(this);
        }

        private async void settingsApply_Click(object sender, EventArgs e)
        {
            var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = configFile.AppSettings.Settings;

            for (var row = 0; row < ConfigurationManager.AppSettings.AllKeys.Length; row++)
            {
                var valCtl = settingsTable.GetControlFromPosition(1, row);
                var keyCtl = settingsTable.GetControlFromPosition(0, row).Text;

                if (valCtl.GetType() == typeof(CheckBox) && settings[keyCtl] != null)
                {
                    settings[keyCtl].Value = ((CheckBox)valCtl).Checked.ToString().ToLower();
                }
                else if (valCtl.GetType() == typeof(TextBox) && settings[keyCtl] != null)
                {
                    settings[keyCtl].Value = ((TextBox)valCtl).Text.ToLower();
                }
            }

            try
            {
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);

                await ApplyConfig();
                Log("Configuration applied.");
            }
            catch (ConfigurationErrorsException ex)
            {
                Log("Error writing app settings.", ex);
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

        private void Restart()
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

        private async void checkBox_Changed(object sender, EventArgs e)
        {
            var checkBox = sender as CheckBox;
            var coord = settingsTable.GetPositionFromControl(checkBox);
            var keyCtl = settingsTable.GetControlFromPosition(coord.Column - 1, coord.Row).Text;

            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (settings[keyCtl] != null)
                {
                    settings[keyCtl].Value = checkBox.Checked.ToString().ToLower();
                }

                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);

                await ApplyConfig();
                Log("Configuration applied.");
            }
            catch (ConfigurationErrorsException ex)
            {
                Log("Error writing app settings.", ex);
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
                    Log("Click on a controller to calibrate.");
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
                    Log("Click on a controller to locate.");
                    break;
            }
        }

        private List<Joycon> GetActiveControllers()
        {
            var controllers = new List<Joycon>();

            foreach (var button in _con)
            {
                if (!button.Enabled)
                {
                    continue;
                }

                controllers.Add((Joycon)button.Tag);
            }

            return controllers;
        }

        private void StartCalibrate(Joycon controller)
        {
            SetCalibrateButtonText();
            btn_calibrate.Enabled = false;

            _countDown = new Timer();
            _count = 4;
            _countDown.Tick += CountDownIMU;
            _countDown.Interval = 1000;
            _countDown.Tag = controller;
            CountDownIMU(null, null);
            _countDown.Start();
        }

        private void btn_reassign_open_Click(object sender, EventArgs e)
        {
            using var mapForm = new Reassign();
            mapForm.StartPosition = FormStartPosition.CenterParent;
            mapForm.ShowDialog(this);
        }

        private void ShowReassignDialog(Joycon controller)
        {
            // Not implemented
        }

        private void CountDownIMU(object sender, EventArgs e)
        {
            var controller = (Joycon)_countDown.Tag;

            if (controller.State != Joycon.Status.IMUDataOk)
            {
                CancelCalibrate(controller, true);
                return;
            }

            if (_count == 0)
            {
                console.Text = "Calibrating IMU...\r\n";
                _countDown.Stop();

                controller.StartIMUCalibration();
                _count = 3;
                _countDown = new Timer();
                _countDown.Tick += CalcIMUData;
                _countDown.Interval = 1000;
                _countDown.Tag = controller;
                _countDown.Start();
            }
            else
            {
                console.Text = "Please keep the controller flat.\r\n";
                console.Text += "Calibration will start in " + _count + " seconds.\r\n";
                _count--;
            }
        }

        private void CalcIMUData(object sender, EventArgs e)
        {
            var controller = (Joycon)_countDown.Tag;

            if (controller.State != Joycon.Status.IMUDataOk)
            {
                CancelCalibrate(controller, true);
                return;
            }

            if (_count == 0)
            {
                _countDown.Stop();
                controller.StopIMUCalibration();

                if (controller.CalibrationIMUDatas.Count == 0)
                {
                    Log("No IMU data received, proceed to stick calibration anyway. Is the controller working ?", Logger.LogLevel.Warning);
                }
                else
                {
                    var imuData = ActiveCaliIMUData(controller.SerialOrMac, true);

                    var rnd = new Random();

                    var xG = new List<int>();
                    var yG = new List<int>();
                    var zG = new List<int>();
                    var xA = new List<int>();
                    var yA = new List<int>();
                    var zA = new List<int>();

                    foreach (var calibrationData in controller.CalibrationIMUDatas)
                    {
                        xG.Add(calibrationData.Xg);
                        yG.Add(calibrationData.Yg);
                        zG.Add(calibrationData.Zg);
                        xA.Add(calibrationData.Xa);
                        yA.Add(calibrationData.Ya);
                        zA.Add(calibrationData.Za);
                    }

                    imuData[0] = (short)quickselect_median(xG, rnd.Next);
                    imuData[1] = (short)quickselect_median(yG, rnd.Next);
                    imuData[2] = (short)quickselect_median(zG, rnd.Next);
                    imuData[3] = (short)quickselect_median(xA, rnd.Next);
                    imuData[4] = (short)quickselect_median(yA, rnd.Next);
                    imuData[5] = (short)quickselect_median(zA, rnd.Next);

                    console.Text += "IMU calibration completed!!!\r\n";

                    Settings.SaveCaliIMUData(CaliIMUData);
                    controller.GetActiveIMUData();
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

        private void CountDownSticksCenter(object sender, EventArgs e)
        {
            var controller = (Joycon)_countDown.Tag;

            if (controller.State != Joycon.Status.IMUDataOk)
            {
                CancelCalibrate(controller, true);
                return;
            }

            if (_count == 0)
            {
                _countDown.Stop();
                controller.StartSticksCalibration();

                console.Text = "Calibrating Sticks center position...\r\n";

                _count = 3;
                _countDown = new Timer();
                _countDown.Tick += CalcSticksCenterData;
                _countDown.Interval = 1000;
                _countDown.Tag = controller;
                _countDown.Start();
            }
            else
            {
                console.Text = "Please keep the sticks at the center position.\r\n";
                console.Text += "Calibration will start in " + _count + " seconds.\r\n";
                _count--;
            }
        }

        private void CalcSticksCenterData(object sender, EventArgs e)
        {
            var controller = (Joycon)_countDown.Tag;

            if (controller.State != Joycon.Status.IMUDataOk)
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
                    Log("No stick positions received, calibration canceled. Is the controller working ?", Logger.LogLevel.Warning);
                    CancelCalibrate(controller);
                    return;
                }

                var stickData = ActiveCaliSticksData(controller.SerialOrMac, true);
                var leftStickData = stickData.AsSpan(0, 6);
                var rightStickData = stickData.AsSpan(6, 6);

                var rnd = new Random();

                var xS1 = new List<int>();
                var yS1 = new List<int>();
                var xS2 = new List<int>();
                var yS2 = new List<int>();

                foreach (var calibrationData in controller.CalibrationStickDatas)
                {
                    xS1.Add(calibrationData.Xs1);
                    yS1.Add(calibrationData.Ys1);
                    xS2.Add(calibrationData.Xs2);
                    yS2.Add(calibrationData.Ys2);
                }

                leftStickData[2] = (ushort)Math.Round(quickselect_median(xS1, rnd.Next));
                leftStickData[3] = (ushort)Math.Round(quickselect_median(yS1, rnd.Next));

                rightStickData[2] = (ushort)Math.Round(quickselect_median(xS2, rnd.Next));
                rightStickData[3] = (ushort)Math.Round(quickselect_median(yS2, rnd.Next));

                ClearCalibrateDatas(controller);

                console.Text += "Sticks center position calibration completed!!!\r\n";

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

        private void CountDownSticksMinMax(object sender, EventArgs e)
        {
            var controller = (Joycon)_countDown.Tag;

            if (controller.State != Joycon.Status.IMUDataOk)
            {
                CancelCalibrate(controller, true);
                return;
            }

            if (_count == 0)
            {
                _countDown.Stop();
                controller.StartSticksCalibration();

                console.Text = "Calibrating Sticks min and max position...\r\n";

                _count = 5;
                _countDown = new Timer();
                _countDown.Tick += CalcSticksMinMaxData;
                _countDown.Interval = 1000;
                _countDown.Tag = controller;
                _countDown.Start();
            }
            else
            {
                console.Text = "Please move the sticks in a circle when the calibration starts." + "\r\n";
                console.Text += "Calibration will start in " + _count + " seconds.\r\n";
                _count--;
            }
        }

        private void CalcSticksMinMaxData(object sender, EventArgs e)
        {
            var controller = (Joycon)_countDown.Tag;

            if (controller.State != Joycon.Status.IMUDataOk)
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
                    Log("No stick positions received, calibration canceled. Is the controller working ?", Logger.LogLevel.Warning);
                    CancelCalibrate(controller);
                    return;
                }

                var stickData = ActiveCaliSticksData(controller.SerialOrMac, true);
                var leftStickData = stickData.AsSpan(0, 6);
                var rightStickData = stickData.AsSpan(6, 6);

                var xS1 = new List<ushort>();
                var yS1 = new List<ushort>();
                var xS2 = new List<ushort>();
                var yS2 = new List<ushort>();

                foreach (var calibrationData in controller.CalibrationStickDatas)
                {
                    xS1.Add(calibrationData.Xs1);
                    yS1.Add(calibrationData.Ys1);
                    xS2.Add(calibrationData.Xs2);
                    yS2.Add(calibrationData.Ys2);
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

                console.Text += "Sticks min and max position calibration completed!!!\r\n";

                Settings.SaveCaliSticksData(CaliSticksData);
                controller.GetActiveSticksData();

                CancelCalibrate(controller);
            }
            else
            {
                _count--;
            }
        }

        private void ClearCalibrateDatas(Joycon controller)
        {
            controller.StopIMUCalibration(true);
            controller.StopSticksCalibration(true);
        }

        private void CancelCalibrate(Joycon controller, bool disconnected = false)
        {
            if (disconnected)
            {
                Log("Controller disconnected, calibration canceled.");
            }

            SetCalibrate(false);
            btn_calibrate.Enabled = true;

            ClearCalibrateDatas(controller);
        }

        private double quickselect_median(List<int> l, Func<int, int> pivotFn)
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

        private int Quickselect(List<int> l, int k, Func<int, int> pivotFn)
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

        public short[] ActiveCaliIMUData(string serNum, bool init = false)
        {
            for (var i = 0; i < CaliIMUData.Count; i++)
            {
                if (CaliIMUData[i].Key == serNum)
                {
                    return CaliIMUData[i].Value;
                }
            }

            if (init)
            {
                var arr = new short[6];
                CaliIMUData.Add(
                    new KeyValuePair<string, short[]>(
                        serNum,
                        arr
                    )
                );
                return arr;
            }

            return null;
        }

        public ushort[] ActiveCaliSticksData(string serNum, bool init = false)
        {
            for (var i = 0; i < CaliSticksData.Count; i++)
            {
                if (CaliSticksData[i].Key == serNum)
                {
                    return CaliSticksData[i].Value;
                }
            }

            if (init)
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

            return null;
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

        public void SetBatteryColor(Joycon controller, Joycon.BatteryLevel batteryLevel)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<Joycon, Joycon.BatteryLevel>(SetBatteryColor), controller, batteryLevel);
                return;
            }

            foreach (var button in _con)
            {
                if (button.Tag != controller)
                {
                    continue;
                }

                switch (batteryLevel)
                {
                    case Joycon.BatteryLevel.Full:
                        button.BackColor = Color.FromArgb(0xAA, 0, 150, 0);
                        break;
                    case Joycon.BatteryLevel.Medium:
                        button.BackColor = Color.FromArgb(0xAA, 150, 230, 0);
                        break;
                    case Joycon.BatteryLevel.Low:
                        button.BackColor = Color.FromArgb(0xAA, 250, 210, 0);
                        break;
                    case Joycon.BatteryLevel.Critical:
                        button.BackColor = Color.FromArgb(0xAA, 250, 150, 0);
                        break;
                    default:
                        button.BackColor = Color.FromArgb(0xAA, 230, 0, 0);
                        break;
                }
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

        private void SetControllerImage(Button button, Joycon.ControllerType controllerType, bool joined = false, bool charging = false)
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
}
