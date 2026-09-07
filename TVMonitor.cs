using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace HTPCAVRVolume
{
    internal class TVMonitor : IDisposable
    {
        // Result of a single probe. "Unreachable" is deliberately distinct from
        // "Standby": a Samsung with network standby enabled still answers :8001
        // and reports PowerState "standby" -- that is how wake-up is detected in
        // the first place. So a timeout does NOT mean the TV is off, it means we
        // did not get an answer, and acting on it powers the AVR down under a
        // TV that never left "on" (see Readme, TV power sync).
        private enum TVProbe { On, Standby, Unreachable }

        // Consecutive unreachable probes before we give up and treat the TV as
        // off. Covers the real "off at the wall / off the network" case that the
        // old unconditional catch-to-false was written for, at the cost of a
        // ~15 s delay on that path (poll interval x this).
        private const int UnreachableTolerance = 3;

        private readonly string _tvIp;
        private readonly Control _uiControl;
        private System.Threading.Timer _pollTimer;
        private bool _tvOn;
        private int _unreachableStreak;
        private int _polling;          // 0 = idle, 1 = a poll is in flight
        private bool _disposed;

        public event EventHandler TVTurnedOn;
        public event EventHandler TVTurnedOff;

        public bool IsTVOn => _tvOn;

        public TVMonitor(string tvIp, Control uiControl)
        {
            _tvIp = tvIp;
            _uiControl = uiControl;
        }

        public void Start()
        {
            _tvOn = CheckTVPower() == TVProbe.On;
            Logger.Log($"TVMonitor.Start: ip={_tvIp} initialState={(IsTVOn ? "on" : "off")}");
            // Poll every 5 seconds; first poll after 5s (initial state captured above)
            _pollTimer = new System.Threading.Timer(Poll, null, 5000, 5000);
        }

        public void Stop()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private void Poll(object state)
        {
            // CheckTVPower blocks for up to its timeout. If one ever outruns the
            // poll interval the periodic timer queues another callback on a second
            // threadpool thread, so guard the state machine against re-entry.
            if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
            {
                Logger.Log("TVMonitor: poll still in flight, skipping this tick");
                return;
            }

            try
            {
                TVProbe probe = CheckTVPower();
                bool nowOn;

                if (probe == TVProbe.Unreachable)
                {
                    _unreachableStreak++;
                    if (_unreachableStreak < UnreachableTolerance)
                    {
                        // Hold the last known state. This is the weekly false
                        // PowerOff: one slow answer from the TV's REST service.
                        Logger.Log($"TVMonitor: probe unreachable ({_unreachableStreak}/{UnreachableTolerance}) - holding state '{(_tvOn ? "on" : "off")}'");
                        return;
                    }

                    Logger.Log($"TVMonitor: probe unreachable {_unreachableStreak}x - treating TV as off");
                    nowOn = false;
                }
                else
                {
                    if (_unreachableStreak > 0)
                        Logger.Log($"TVMonitor: probe recovered after {_unreachableStreak} unreachable poll(s)");
                    _unreachableStreak = 0;
                    nowOn = probe == TVProbe.On;
                }

                if (nowOn == _tvOn) return;

                _tvOn = nowOn;
                Logger.Log($"TVMonitor: state changed -> {(nowOn ? "on" : "off")}");

                try
                {
                    if (_uiControl.IsHandleCreated && !_uiControl.IsDisposed)
                        _uiControl.BeginInvoke(nowOn
                            ? (Action)(() => TVTurnedOn?.Invoke(this, EventArgs.Empty))
                            : (Action)(() => TVTurnedOff?.Invoke(this, EventArgs.Empty)));
                }
                catch (ObjectDisposedException) { }
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        private TVProbe CheckTVPower()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create($"http://{_tvIp}:8001/api/v2/");
                request.Timeout = 3000;
                request.Method = "GET";
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string body = reader.ReadToEnd();
                    // PowerState is "on" when active, "standby" when sleeping.
                    // The TV answered either way, so this is a real reading.
                    return body.Contains("\"PowerState\":\"on\"") ? TVProbe.On : TVProbe.Standby;
                }
            }
            catch (Exception ex)
            {
                // Timeout, refused, no route, malformed response. Never silently
                // equated to "off" any more -- the caller decides after N of these.
                Logger.Log($"TVMonitor: probe failed - {ex.GetType().Name}: {ex.Message}");
                return TVProbe.Unreachable;
            }
        }
    }
}
