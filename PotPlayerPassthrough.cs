using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace HTPCAVRVolume
{
    /// <summary>
    /// Keeps PotPlayer's audio pass-through (bit-streaming) in step with the Windows default output.
    ///
    /// PotPlayer follows the default output device, but its per-codec pass-through flags are global.
    /// A bitstream that the AVR decodes (AC3 / E-AC3 / TrueHD ...) is silence or loud clicking on
    /// headphones. So: AVR is the default output -> the saved flags are restored; any other device
    /// -> the flags are set to 0 and PotPlayer decodes to PCM, which every device can play.
    ///
    /// PotPlayer reads its settings at launch and rewrites them at exit, so the registry is only
    /// touched while PotPlayer is not running. A switch made mid-file applies from the next launch.
    ///
    /// The "AVR profile" is learned, not hard-coded: whatever pass-through flags are set while the
    /// AVR is the default output are remembered, so a change made in PotPlayer's preferences while
    /// on the AVR sticks. A change made while on another device is overwritten.
    ///
    /// Nothing polls. A pass runs when asked (default output changed), when PotPlayer's settings
    /// key changes (registry notification) and when a running PotPlayer exits.
    /// </summary>
    internal sealed class PotPlayerPassthrough : IDisposable
    {
        public const string DefaultSettingsKey = @"Software\DAUM\PotPlayerMini64\Settings";
        public const string DefaultProcessName = "PotPlayerMini64";

        private const string FlagMarker = "Spdif";   // Ac3Spdif_new, Eac3Spdif, TrueHDSpdif, ...
        private const int DebounceMs = 500;          // PotPlayer writes many values in one go at exit
        private const int RetryMs = 5000;            // only used when a running PotPlayer cannot be waited on

        private readonly Func<bool?> _avrActive;     // true = AVR is the default output, null = unknown
        private readonly Action<string> _log;
        private readonly string _statePath;
        private readonly string _settingsKey;        // under HKEY_CURRENT_USER
        private readonly string _processName;

        private readonly object _lock = new object();
        private readonly Timer _timer;
        private readonly List<Process> _watched = new List<Process>();
        private Thread _watcher;
        private ManualResetEvent _watcherStop;

        private bool _enabled;
        private bool _idle = true;                   // switched off and the AVR profile is back in place
        private bool _disposed;

        // Persisted state
        private bool _stateLoaded;
        private bool _flagsHoldProfile = true;       // false = this class zeroed the flags
        private Dictionary<string, int> _profile = new Dictionary<string, int>();

        public PotPlayerPassthrough(Func<bool?> avrActive, Action<string> log, string statePath,
            string settingsKey = DefaultSettingsKey, string processName = DefaultProcessName)
        {
            _avrActive = avrActive;
            _log = log ?? (_ => { });
            _statePath = statePath;
            _settingsKey = settingsKey;
            _processName = processName;
            _timer = new Timer(_ => SyncNow(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// On: start syncing. Off: put the AVR profile back (once PotPlayer is closed) and go idle,
        /// so switching the option off never leaves pass-through disabled behind the user's back.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            lock (_lock)
            {
                if (_disposed) return;
                if (enabled == _enabled && !_idle) return;   // already doing that
                if (!enabled && _idle) return;               // off, and nothing to put back
                _enabled = enabled;
                _idle = false;
            }
            RequestSync();
        }

        /// <summary>Ask for a pass, e.g. after the default output device changed.</summary>
        public void RequestSync()
        {
            lock (_lock)
            {
                if (_disposed || _idle) return;
                _timer.Change(DebounceMs, Timeout.Infinite);
            }
        }

        private void SyncNow()
        {
            lock (_lock)
            {
                if (_disposed || _idle) return;
                try
                {
                    if (_enabled) EnsureWatcher();
                    if (PlayerRunning()) return;     // a pass follows from its Exited event

                    bool? active = _enabled ? _avrActive() : true;
                    if (active != null)
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_settingsKey, true))
                        {
                            if (key != null) Apply(key, active.Value);
                        }
                    }

                    if (!_enabled)
                    {
                        _idle = true;
                        StopWatcher();
                    }
                }
                catch (Exception ex)
                {
                    _log("PotPlayer pass-through sync failed: " + ex.Message);
                }
            }
        }

        private void Apply(RegistryKey key, bool avrActive)
        {
            Dictionary<string, int> now = ReadFlags(key);
            if (!_stateLoaded) LoadState(now);

            if (avrActive)
            {
                if (!_flagsHoldProfile)
                {
                    foreach (KeyValuePair<string, int> kv in _profile)
                        key.SetValue(kv.Key, kv.Value, RegistryValueKind.DWord);
                    _flagsHoldProfile = true;
                    SaveState();
                    _log(_profile.Count > 0
                        ? "PotPlayer pass-through ON (" + Names(_profile) + ") - AVR is the default output"
                        : "PotPlayer pass-through: no profile learned yet - enable pass-through in PotPlayer while on the AVR");
                }
                else if (!SameFlags(now, _profile))
                {
                    // Pass-through was changed in PotPlayer while on the AVR: that is the new profile.
                    _profile = now;
                    SaveState();
                    _log("PotPlayer pass-through profile learned: " + (now.Count > 0 ? Names(now) : "(none)"));
                }
            }
            else if (_flagsHoldProfile || now.Count > 0)
            {
                foreach (string name in _profile.Keys.Union(now.Keys).ToList())
                    key.SetValue(name, 0, RegistryValueKind.DWord);
                _flagsHoldProfile = false;
                SaveState();
                _log("PotPlayer pass-through OFF - default output is not the AVR");
            }
        }

        // ── PotPlayer process ─────────────────────────────────────────────────

        private bool PlayerRunning()
        {
            _watched.RemoveAll(p =>
            {
                bool gone;
                try { gone = p.HasExited; } catch { gone = true; }
                if (gone) p.Dispose();
                return gone;
            });
            if (_watched.Count > 0) return true;

            Process[] found = Process.GetProcessesByName(_processName);
            if (found.Length == 0) return false;

            bool allHooked = true;
            foreach (Process p in found)
            {
                try
                {
                    p.Exited += (s, e) => RequestSync();
                    p.EnableRaisingEvents = true;    // raises Exited at once if it is already gone
                    _watched.Add(p);
                }
                catch
                {
                    allHooked = false;               // e.g. an elevated PotPlayer: fall back to a retry
                    p.Dispose();
                }
            }
            if (!allHooked) _timer.Change(RetryMs, Timeout.Infinite);
            return true;
        }

        // ── Registry change notification ──────────────────────────────────────

        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle hKey, bool watchSubtree,
            int notifyFilter, SafeWaitHandle hEvent, bool asynchronous);

        private const int RegNotifyChangeName = 0x1;
        private const int RegNotifyChangeLastSet = 0x4;

        private void EnsureWatcher()
        {
            if (_watcher != null && _watcher.IsAlive) return;
            RegistryKey key = Registry.CurrentUser.OpenSubKey(_settingsKey, false);
            if (key == null) return;                 // PotPlayer has no settings yet; retried on the next pass

            var stop = new ManualResetEvent(false);
            _watcherStop = stop;
            _watcher = new Thread(() => WatchSettingsKey(key, stop))
            {
                IsBackground = true,
                Name = "PotPlayerSettingsWatcher"
            };
            _watcher.Start();
        }

        private void StopWatcher()
        {
            _watcherStop?.Set();
            _watcherStop = null;
            _watcher = null;
        }

        private void WatchSettingsKey(RegistryKey key, ManualResetEvent stop)
        {
            try
            {
                using (key)
                using (var changed = new AutoResetEvent(false))
                {
                    var handles = new WaitHandle[] { stop, changed };
                    while (true)
                    {
                        // The registration is one-shot and tied to this thread, hence the loop.
                        int rc = RegNotifyChangeKeyValue(key.Handle, false,
                            RegNotifyChangeName | RegNotifyChangeLastSet, changed.SafeWaitHandle, true);
                        if (rc != 0) return;         // key deleted etc.; EnsureWatcher starts a new one
                        if (WaitHandle.WaitAny(handles) == 0) return;
                        RequestSync();
                    }
                }
            }
            catch (Exception ex)
            {
                _log("PotPlayer settings watcher stopped: " + ex.Message);
            }
            finally
            {
                stop.Dispose();
            }
        }

        // ── Flags and state ───────────────────────────────────────────────────

        // name -> value for every non-zero pass-through flag. PotPlayer omits values that are at
        // their default, so a missing flag and a 0 flag mean the same thing: decode.
        private static Dictionary<string, int> ReadFlags(RegistryKey key)
        {
            var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in key.GetValueNames())
            {
                if (name.IndexOf(FlagMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (key.GetValueKind(name) != RegistryValueKind.DWord) continue;
                int value = (int)key.GetValue(name, 0);
                if (value != 0) flags[name] = value;
            }
            return flags;
        }

        private static bool SameFlags(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (KeyValuePair<string, int> kv in a)
                if (!b.TryGetValue(kv.Key, out int other) || other != kv.Value) return false;
            return true;
        }

        private static string Names(Dictionary<string, int> flags)
        {
            return string.Join(",", flags.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        }

        // State file, next to the config:
        //   Mode=avr|other          avr = the registry holds the profile, other = flags were zeroed
        //   Profile=Name:1,Name:1   the pass-through flags to restore on the AVR
        private void LoadState(Dictionary<string, int> flagsNow)
        {
            _stateLoaded = true;
            try
            {
                if (File.Exists(_statePath))
                {
                    string mode = null;
                    var profile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in File.ReadAllLines(_statePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "Mode") mode = v;
                        if (k != "Profile") continue;
                        foreach (string item in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string[] nv = item.Split(':');
                            if (nv.Length == 2 && int.TryParse(nv[1], out int n) && n != 0 &&
                                nv[0].IndexOf(FlagMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                                profile[nv[0].Trim()] = n;
                        }
                    }
                    if (mode == "avr" || mode == "other")
                    {
                        _profile = profile;
                        _flagsHoldProfile = mode == "avr";
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _log("PotPlayer pass-through state unreadable, starting over: " + ex.Message);
            }

            // First run: whatever is set in PotPlayer right now is the AVR profile.
            _profile = flagsNow;
            _flagsHoldProfile = true;
            SaveState();
            _log("PotPlayer pass-through sync: initial profile " + (flagsNow.Count > 0 ? Names(flagsNow) : "(none)"));
        }

        private void SaveState()
        {
            try
            {
                string profile = string.Join(",", _profile
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => kv.Key + ":" + kv.Value));
                File.WriteAllText(_statePath,
                    "Mode=" + (_flagsHoldProfile ? "avr" : "other") + "\r\nProfile=" + profile + "\r\n");
            }
            catch (Exception ex)
            {
                _log("PotPlayer pass-through state not saved: " + ex.Message);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                StopWatcher();
                _timer.Dispose();
                foreach (Process p in _watched) p.Dispose();
                _watched.Clear();
            }
        }
    }
}
