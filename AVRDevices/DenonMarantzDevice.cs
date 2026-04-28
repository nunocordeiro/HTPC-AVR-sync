using System;
using System.Text;
using System.Net.Sockets;
using System.Threading;

namespace HTPCAVRVolume.AVRDevices
{
    class DenonMarantzDevice : IAVRDevice, IDisposable
    {
        private readonly string _ip;

        // Persistent connection
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _readerThread;
        private System.Threading.Timer _keepAliveTimer;
        private System.Threading.Timer _reconnectTimer;
        private int _connectionGeneration;

        // Tracked state
        private volatile bool _muted     = false;
        private volatile bool _connected = false;
        private volatile bool _disposed  = false;
        private volatile bool _sending      = false;
        private volatile bool _flushPending = false;

        private readonly object _sendLock = new object();

        // Volume state — all protected by _volumeLock
        // _volumeLevel is the single source of truth: updated instantly by user
        // input and synced from AVR feedback only when the roller has been idle
        // for VolumeIdleMs.  This prevents the Denon's intermediate feedback
        // (and its hard floor/ceiling) from corrupting mid-roll accumulation.
        private float    _volumeLevel    = 50f;
        private DateTime _lastVolInput   = DateTime.MinValue;
        private System.Threading.Timer _volumeDebounceTimer;
        private readonly object _volumeLock = new object();

        private float       _volumeMax   = 70f;  // updated from MVMAX; safe default matches typical Denon cap
        private const float VolumeMin    = 10f;  // Denon floor ~ -70 dB display = protocol 10
        private const int   DebounceMs   = 80;   // ms to wait after last notch before sending
        private const int   VolumeIdleMs = 1000; // ms idle before accepting AVR vol feedback

        public event Action<string>        StatusChanged;
        public event Action<float, float, float> VolumeChanged; // (level, min, max)

        public DenonMarantzDevice(string ip)
        {
            _ip = ip;
            Connect();
        }

        // ── Connection management ─────────────────────────────────────────────

        private void Connect()
        {
            if (_disposed) return;
            int myGen = Interlocked.Increment(ref _connectionGeneration);
            try
            {
                _client?.Close();
                _client = new TcpClient();
                var task = _client.ConnectAsync(_ip, 23);
                if (!task.Wait(3000))
                    throw new TimeoutException($"Timeout connecting to {_ip}:23");

                _stream = _client.GetStream();
                _stream.WriteTimeout = 2000;  // prevent SendCmd from blocking indefinitely
                _connected = true;

                _readerThread = new Thread(() => ReadLoop(myGen))
                {
                    IsBackground = true,
                    Name = "DenonReader"
                };
                _readerThread.Start();

                // Keep-alive every 60 s to prevent the Denon's NIC from entering
                // deep sleep and dropping the HDMI handshake.
                _keepAliveTimer?.Dispose();
                _keepAliveTimer = new System.Threading.Timer(
                    _ => TrySend("PW?"), null, 60_000, 60_000);

                // Query initial state — feedback accepted immediately (no recent user input)
                TrySend("MV?");
                TrySend("MU?");

                StatusChanged?.Invoke("AVR connected");
            }
            catch (Exception ex)
            {
                _connected = false;
                StatusChanged?.Invoke($"AVR connection failed: {ex.Message}");
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (_disposed) return;
            _reconnectTimer?.Dispose();
            _reconnectTimer = new System.Threading.Timer(
                _ => { if (!_connected && !_disposed) Connect(); },
                null, 10_000, Timeout.Infinite);
        }

        // ── Reader loop ───────────────────────────────────────────────────────

        private void ReadLoop(int myGen)
        {
            var sb  = new StringBuilder();
            var buf = new byte[256];
            try
            {
                while (!_disposed)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n == 0) break;

                    sb.Append(Encoding.ASCII.GetString(buf, 0, n));

                    int cr;
                    while ((cr = sb.ToString().IndexOf('\r')) >= 0)
                    {
                        string line = sb.ToString(0, cr).Trim();
                        sb.Remove(0, cr + 1);
                        if (line.Length > 0) ProcessLine(line);
                    }
                }
            }
            catch (Exception) when (_disposed) { }
            catch (Exception ex)
            {
                if (myGen != _connectionGeneration) return;
                _connected = false;
                StatusChanged?.Invoke($"AVR disconnected: {ex.Message}");
                ScheduleReconnect();
            }
        }

        private void ProcessLine(string line)
        {
            // Log every AVR line to file for diagnostics
            Logger.Log($"AVR< {line}");

            if (line.StartsWith("MVMAX"))
            {
                // MVMAX 70  = ceiling is 70.0    MVMAX 695 = ceiling is 69.5
                string maxStr = line.Substring(5).Trim();
                if (int.TryParse(maxStr, out int rawMax))
                {
                    float maxLevel = rawMax >= 100 ? rawMax / 10f : (float)rawMax;
                    lock (_volumeLock) { _volumeMax = maxLevel; }
                    Logger.Log($"AVR vol max learned: {maxLevel}");
                }
            }
            else if (line.StartsWith("MV") && line.Length > 2)
            {
                string volStr = line.Substring(2);
                // MV45  = 45.0 dB   MV455 = 45.5 dB (3 digits => divide by 10)
                if (int.TryParse(volStr, out int raw))
                {
                    float confirmed = raw >= 100 ? raw / 10f : (float)raw;
                    lock (_volumeLock)
                    {
                        bool idle = (DateTime.Now - _lastVolInput).TotalMilliseconds > VolumeIdleMs;
                        if (idle && Math.Abs(_volumeLevel - confirmed) > 0.1f)
                        {
                            _volumeLevel = confirmed;
                            Logger.Log($"AVR vol synced: {confirmed}");
                        }
                    }
                }
            }
            else if (line == "MUON")  { _muted = true;  }
            else if (line == "MUOFF") { _muted = false; }
        }

        // ── IAVRDevice ────────────────────────────────────────────────────────

        public void VolUp()   => AccumulateVolume(+0.5f);
        public void VolDown() => AccumulateVolume(-0.5f);

        private void AccumulateVolume(float delta)
        {
            float level, min, max;
            lock (_volumeLock)
            {
                _lastVolInput = DateTime.Now;
                _volumeLevel  = Math.Max(VolumeMin, Math.Min(_volumeMax, _volumeLevel + delta));
                level = _volumeLevel;
                min   = VolumeMin;
                max   = _volumeMax;

                _volumeDebounceTimer?.Dispose();
                _volumeDebounceTimer = new System.Threading.Timer(
                    _ => FlushVolume(), null, DebounceMs, Timeout.Infinite);
            }
            // Fire immediately so the OSD appears with no perceptible delay
            VolumeChanged?.Invoke(level, min, max);
        }

        private void FlushVolume()
        {
            // If a write is already in-flight, note that we have a pending update
            // and return — the finally block below will re-invoke us once the
            // in-flight write completes, so no roller notch is ever silently lost.
            if (_sending) { _flushPending = true; return; }
            _flushPending = false;
            _sending = true;
            try
            {
                float target;
                lock (_volumeLock) { target = _volumeLevel; }

                string cmd = FormatVolume(target);
                Logger.Log($"AVR> {cmd}  (vol={target})");
                try
                {
                    SendCmd(cmd);
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"AVR volume error: {ex.Message}");
                    // Close the stream so the reader thread notices and triggers reconnect.
                    _connected = false;
                    try { _stream?.Close(); } catch { }
                }
            }
            finally
            {
                _sending = false;
                if (_flushPending) FlushVolume();
            }
        }

        public void ToggleMute() => SendCmd(_muted ? "MUOFF" : "MUON");
        public void PowerOn()    => SendCmd("PWON");
        public void PowerOff()   => SendCmd("PWSTANDBY");

        // ── Helpers ───────────────────────────────────────────────────────────

        // MV45.0 -> "MV45"    MV45.5 -> "MV455"
        private static string FormatVolume(float level)
        {
            float rounded = (float)(Math.Round(level * 2) / 2.0);
            if (rounded % 1.0f < 0.01f)
                return "MV" + ((int)rounded).ToString();
            return "MV" + ((int)(rounded * 10)).ToString();
        }

        private void SendCmd(string cmd)
        {
            if (!_connected)
                throw new InvalidOperationException("AVR not connected");
            Logger.Log($"AVR> {cmd}");
            lock (_sendLock)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(cmd + "\r");
                _stream.Write(bytes, 0, bytes.Length);
            }
        }

        private void TrySend(string cmd)
        {
            if (!_connected) return;
            try { SendCmd(cmd); } catch { }
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed  = true;
            _connected = false;
            _keepAliveTimer?.Dispose();
            _reconnectTimer?.Dispose();
            _volumeDebounceTimer?.Dispose();
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
        }
    }
}
