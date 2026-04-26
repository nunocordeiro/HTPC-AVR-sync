using System;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace HTPCAVRVolume
{
    internal class TVMonitor : IDisposable
    {
        private readonly string _tvIp;
        private readonly Control _uiControl;
        private System.Threading.Timer _pollTimer;
        private bool _tvOn;
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
            _tvOn = CheckTVPower();
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
            bool nowOn = CheckTVPower();
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

        private bool CheckTVPower()
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
                    // PowerState is "on" when active, "standby" when sleeping
                    return body.Contains("\"PowerState\":\"on\"");
                }
            }
            catch
            {
                return false; // TV unreachable (off at wall or network issue) = treat as off
            }
        }
    }
}
