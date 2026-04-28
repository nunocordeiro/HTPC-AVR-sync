using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace HTPCAVRVolume
{
    public partial class HTPCAVRVolume : Form
    {
        private readonly string _configPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\HTPCAVRVolumeConfig.txt";
        private bool _hotkeysRegistered = false;

        // AVR failure tracking
        private DateTime? _avrFirstFailureTime;
        private bool _avrAlertShown;

        private KeyboardHook hookVolUp;
        private KeyboardHook hookVolDown;
        private KeyboardHook hookToggleMute;

        private AVRDevices.IAVRDevice _AVR;
        private AudioDeviceMonitor _audioMonitor;
        private TVMonitor _tvMonitor;
        private VolumeOSD _volumeOSD;

        public HTPCAVRVolume()
        {
            InitializeComponent();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            notifyIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3).Replace(".0", "");
            Text = $"HTPC-AVR-sync v{version}";

            _volumeOSD = new VolumeOSD();
        }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "HTPCAVRVolume";

        private void LoadStartupCheckbox()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    chkStartWithWindows.Checked = key?.GetValue(RunValueName) != null;
            }
            catch (Exception ex) { Logger.LogException("LoadStartupCheckbox", ex); }
        }

        private void ChkStartWithWindows_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (chkStartWithWindows.Checked)
                    {
                        string exePath = Assembly.GetEntryAssembly().Location;
                        key.SetValue(RunValueName, $"\"{exePath}\"");
                        Logger.Log($"Startup enabled: {exePath}");
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                        Logger.Log("Startup disabled");
                    }
                }
            }
            catch (Exception ex) { Logger.LogException("ChkStartWithWindows_CheckedChanged", ex); }
        }

        private void HTPCAVRVolume_Load(object sender, EventArgs e)
        {
            hookVolUp = new KeyboardHook(Constants.NOMOD, Keys.VolumeUp, this);
            hookVolDown = new KeyboardHook(Constants.NOMOD, Keys.VolumeDown, this);
            hookToggleMute = new KeyboardHook(Constants.NOMOD, Keys.VolumeMute, this);

            LoadStartupCheckbox();

            bool loaded = LoadDevice();
            if (!loaded)
            {
                // No config yet — register hotkeys unconditionally (original behaviour)
                RegisterHotkeys();
            }
        }

        private bool LoadDevice()
        {
            try
            {
                string[] lines = File.ReadAllLines(_configPath);
                if (lines.Length == 0) return false;

                string[] first = lines[0].Split(new char[] { '=' }, 2);
                if (first.Length < 2) return false;

                string deviceType = first[0];
                string ip = first[1];

                cmbDevice.SelectedItem = deviceType;
                tbIP.Text = ip;

                // Dispose previous device (DenonMarantz holds a persistent connection)
                (_AVR as IDisposable)?.Dispose();
                _AVR = null;

                switch (deviceType)
                {
                    case "DenonMarantz":
                        var denon = new AVRDevices.DenonMarantzDevice(ip);
                        denon.StatusChanged  += msg => AppendLog(msg);
                        denon.VolumeChanged  += (level, min, max) => _volumeOSD.ShowVolume(level, min, max);
                        _AVR = denon;
                        break;
                    case "StormAudio":
                        _AVR = new AVRDevices.StormAudioDevice(ip);
                        break;
                }

                string audioDeviceId   = null;
                string audioDeviceName = null;
                string tvIp = null;
                for (int i = 1; i < lines.Length; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq < 0) continue;
                    string key = lines[i].Substring(0, eq);
                    string val = lines[i].Substring(eq + 1);
                    if (key == "AudioDevice")
                    {
                        // Format: id|FriendlyName  (pipe-separated; legacy entries have no pipe)
                        int pipe = val.IndexOf('|');
                        if (pipe >= 0) { audioDeviceId = val.Substring(0, pipe); audioDeviceName = val.Substring(pipe + 1); }
                        else             audioDeviceId = val;
                    }
                    if (key == "TVIP") tvIp = val;
                }

                if (tvIp != null) tbTVIP.Text = tvIp;
                StartAudioMonitoring(audioDeviceId, audioDeviceName);
                StartTVMonitoring(tvIp);
                return true;
            }
            catch { return false; }
        }

        private void StartTVMonitoring(string tvIp)
        {
            _tvMonitor?.Dispose();
            _tvMonitor = null;

            if (string.IsNullOrWhiteSpace(tvIp))
            {
                UpdateTVStatus("Not configured");
                return;
            }

            _tvMonitor = new TVMonitor(tvIp, this);
            _tvMonitor.TVTurnedOn += OnTVTurnedOn;
            _tvMonitor.TVTurnedOff += OnTVTurnedOff;
            _tvMonitor.Start();
            UpdateTVStatus(_tvMonitor.IsTVOn ? "On" : "Off / Standby");
        }

        private void OnTVTurnedOn(object sender, EventArgs e)
        {
            // Feature 5: TV wakes first; wait 2 s before powering on the AVR so
            // the GPU sees the TV's EDID before the AVR comes online, preventing
            // Windows from falling back to a generic 1024x768 resolution.
            AppendLog("TV turned on — powering on AVR in 2 s");
            var avr = _AVR;
            Task.Delay(2000).ContinueWith(_ => SendAVRCommand(() => avr.PowerOn()));
            UpdateTVStatus("On");
        }

        private void OnTVTurnedOff(object sender, EventArgs e)
        {
            AppendLog("TV turned off — sending PowerOff to AVR");
            SendAVRCommand(() => _AVR.PowerOff());
            UpdateTVStatus("Off / Standby");
        }

        private void UpdateTVStatus(string status)
        {
            lblTVStatus.Text = "TV: " + status;
        }

        private void StartAudioMonitoring(string audioDeviceId, string audioDeviceName = null)
        {
            _audioMonitor?.Dispose();
            _audioMonitor = null;

            if (string.IsNullOrEmpty(audioDeviceId))
            {
                RegisterHotkeys();
                UpdateAudioStatus("Not configured");
                return;
            }

            _audioMonitor = new AudioDeviceMonitor(this);
            _audioMonitor.AVRBecameActive += OnAVRBecameActive;
            _audioMonitor.AVRBecameInactive += OnAVRBecameInactive;
            string resolvedId = _audioMonitor.Start(audioDeviceId, audioDeviceName);

            // If Start() resolved the device to a new GUID via name matching,
            // persist the updated ID to config so future starts don't need to re-resolve.
            if (resolvedId != audioDeviceId && !string.IsNullOrEmpty(resolvedId))
            {
                AppendLog($"Device GUID changed — auto-saving updated ID");
                SaveAudioDeviceToConfig(resolvedId, audioDeviceName);
            }

            if (_audioMonitor.IsAVRActive)
            {
                RegisterHotkeys();
                UpdateAudioStatus("Active");
            }
            else
            {
                UnregisterHotkeys();
                UpdateAudioStatus("Inactive (hotkeys paused)");
            }
        }

        private void RegisterHotkeys()
        {
            if (_hotkeysRegistered) return;
            bool up   = hookVolUp.Register();
            bool down = hookVolDown.Register();
            bool mute = hookToggleMute.Register();
            Logger.Log($"RegisterHotkeys: up={up} down={down} mute={mute}");
            if (!up || !down || !mute)
            {
                // Registration failed — another app holds the keys.
                // Unregister any that did succeed and schedule a retry.
                if (up)   hookVolUp.Unregister();
                if (down) hookVolDown.Unregister();
                if (mute) hookToggleMute.Unregister();
                AppendLog("Hotkey registration failed — will retry in 5 s (another app may hold the keys)");
                var t = new System.Windows.Forms.Timer { Interval = 5000 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); RegisterHotkeys(); };
                t.Start();
                return;
            }
            _hotkeysRegistered = true;
        }

        private void UnregisterHotkeys()
        {
            if (!_hotkeysRegistered) return;
            hookVolUp.Unregister();
            hookVolDown.Unregister();
            hookToggleMute.Unregister();
            _hotkeysRegistered = false;
            Logger.Log("UnregisterHotkeys: done");
        }

        private void OnAVRBecameActive(object sender, EventArgs e)
        {
            AppendLog("Audio output: AVR active — hotkeys enabled");
            try
            {
                RegisterHotkeys();
                UpdateAudioStatus("Active");

                // Wait 1s: Windows restores its remembered per-device volume after the switch.
                // We must let that settle before overriding to 100%.
                var timer = new System.Windows.Forms.Timer { Interval = 1000 };
                timer.Tick += (ts, te) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    Logger.Log("OnAVRBecameActive: deferred SetMasterVolumeToMax firing");
                    AudioDeviceMonitor.SetMasterVolumeToMax();
                };
                timer.Start();

                Logger.Log("OnAVRBecameActive: done");
            }
            catch (Exception ex)
            {
                Logger.LogException("OnAVRBecameActive", ex);
            }
        }

        private void OnAVRBecameInactive(object sender, EventArgs e)
        {
            AppendLog("Audio output: switched away from AVR — hotkeys paused");
            try
            {
                UnregisterHotkeys();
                UpdateAudioStatus("Inactive (hotkeys paused)");
                Logger.Log("OnAVRBecameInactive: done");
            }
            catch (Exception ex)
            {
                Logger.LogException("OnAVRBecameInactive", ex);
            }
        }

        private void UpdateAudioStatus(string status)
        {
            lblAudioStatus.Text = "Audio: " + status;
            string tip = "HTPC-AVR-sync - " + status;
            notifyIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Constants.WM_HOTKEY_MSG_ID)
            {
                Keys k = GetKey(m.LParam);
                Logger.Log($"WndProc: WM_HOTKEY key={k} avrNull={_AVR == null}");
                switch (k)
                {
                    case Keys.VolumeUp:
                        SendAVRCommand(() => _AVR.VolUp());
                        break;
                    case Keys.VolumeDown:
                        SendAVRCommand(() => _AVR.VolDown());
                        break;
                    case Keys.VolumeMute:
                        SendAVRCommand(() => _AVR.ToggleMute());
                        break;
                }
            }
            base.WndProc(ref m);
        }

        private Keys GetKey(IntPtr LParam)
        {
            return (Keys)((LParam.ToInt32()) >> 16);
        }

        private void SaveAudioDeviceToConfig(string deviceId, string deviceName)
        {
            try
            {
                // Re-read current config to preserve AVR type/IP/TVIP, only update AudioDevice line
                string[] lines = File.Exists(_configPath) ? File.ReadAllLines(_configPath) : new string[0];
                string avrLine  = lines.Length > 0 ? lines[0] : (cmbDevice.SelectedItem?.ToString() + "=" + tbIP.Text);
                string tvLine   = "TVIP=" + tbTVIP.Text.Trim();
                string audioVal = string.IsNullOrEmpty(deviceName) ? deviceId : deviceId + "|" + deviceName;
                File.WriteAllText(_configPath,
                    avrLine + "\r\nAudioDevice=" + audioVal + "\r\n" + tvLine);
            }
            catch { }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                string audioDeviceId   = AudioDeviceMonitor.GetCurrentDefaultDeviceId() ?? "";
                string audioDeviceName = AudioDeviceMonitor.GetDeviceFriendlyName(audioDeviceId) ?? "";
                string audioVal        = string.IsNullOrEmpty(audioDeviceName)
                    ? audioDeviceId
                    : audioDeviceId + "|" + audioDeviceName;
                string configText = cmbDevice.SelectedItem.ToString() + "=" + tbIP.Text
                    + "\r\nAudioDevice=" + audioVal
                    + "\r\nTVIP=" + tbTVIP.Text.Trim();
                File.WriteAllText(_configPath, configText);
                Logger.Log($"Saved audio device: {audioDeviceName} ({audioDeviceId})");
                LoadDevice();
            }
            catch
            {
                MessageBox.Show("Try running as Administrator and saving your config again.", "Error saving config");
            }
        }

        private void BtnVolUp_Click(object sender, EventArgs e)
        {
            SendAVRCommand(() => _AVR.VolUp());
        }

        private void BtnVolDown_Click(object sender, EventArgs e)
        {
            SendAVRCommand(() => _AVR.VolDown());
        }

        private void BtnToggleMute_Click(object sender, EventArgs e)
        {
            SendAVRCommand(() => _AVR.ToggleMute());
        }

        private void BtnAVROn_Click(object sender, EventArgs e)
        {
            SendAVRCommand(() => _AVR.PowerOn());
        }

        private void BtnAVROff_Click(object sender, EventArgs e)
        {
            SendAVRCommand(() => _AVR.PowerOff());
        }

        // ── AVR command dispatch ──────────────────────────────────────────────

        private void SendAVRCommand(Action cmd)
        {
            if (_AVR == null) return;
            Task.Run(() =>
            {
                try
                {
                    cmd();
                    // Success — clear failure streak
                    _avrFirstFailureTime = null;
                    _avrAlertShown = false;
                }
                catch (Exception ex)
                {
                    Logger.LogException("SendAVRCommand", ex);
                    HandleAVRFailure(ex.Message);
                }
            });
        }

        private void HandleAVRFailure(string message)
        {
            if (_avrFirstFailureTime == null)
                _avrFirstFailureTime = DateTime.Now;

            AppendLog("AVR error: " + message);

            if (!_avrAlertShown &&
                (DateTime.Now - _avrFirstFailureTime.Value).TotalSeconds >= 30)
            {
                _avrAlertShown = true;
                BeginInvoke((Action)(() =>
                    notifyIcon.ShowBalloonTip(5000, "HTPC-AVR-sync",
                        "AVR unreachable for 30+ seconds", ToolTipIcon.Warning)));
            }
        }

        // ── UI log ────────────────────────────────────────────────────────────

        private void AppendLog(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logger.Log(line);
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)(() =>
                {
                    rtbLog.AppendText(line + Environment.NewLine);
                    rtbLog.ScrollToCaret();
                }));
        }

        private void HTPCAVRVolume_FormClosed(object sender, FormClosedEventArgs e)
        {
            _tvMonitor?.Dispose();
            _audioMonitor?.Dispose();
            (_AVR as IDisposable)?.Dispose();
            _volumeOSD?.Dispose();
            UnregisterHotkeys();
        }

        private void NotifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void HTPCAVRVolume_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == WindowState)
            {
                notifyIcon.Visible = true;
                Hide();
            }
            else if (FormWindowState.Normal == WindowState)
            {
                notifyIcon.Visible = false;
            }
        }

        private void HTPCAVRVolume_Shown(object sender, EventArgs e)
        {
            if (cmbDevice.SelectedItem != null)
            {
                WindowState = FormWindowState.Minimized;
            }
        }
    }
}
