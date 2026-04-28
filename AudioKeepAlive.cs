using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HTPCAVRVolume
{
    /// <summary>
    /// Holds a silent WASAPI shared-mode stream on the AVR audio device to prevent
    /// the driver from power-gating the endpoint between playback sessions.
    ///
    /// When an exclusive-mode app (e.g. a bit-streaming media player) claims the
    /// device, Windows fires OnSessionDisconnected on our IAudioSessionEvents sink.
    /// We tear down cleanly and retry after 10 seconds so the exclusive app is
    /// never blocked.  The retry loop also handles the device being absent (AVR off).
    /// </summary>
    internal sealed class AudioKeepAlive : IDisposable
    {
        private readonly string _deviceId;
        private volatile bool _disposed;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _retryNow = new ManualResetEventSlim(false);

        public AudioKeepAlive(string deviceId)
        {
            _deviceId = deviceId;
            _thread   = new Thread(RunLoop) { IsBackground = true, Name = "AudioKeepAlive" };
            _thread.Start();
        }

        public void Dispose()
        {
            _disposed = true;
            _retryNow.Set();   // unblock any Wait so the thread exits promptly
        }

        // ── Background loop ────────────────────────────────────────────────────

        private void RunLoop()
        {
            while (!_disposed)
            {
                try   { TryRunStream(); }
                catch (Exception ex) { Logger.Log($"KeepAlive: error — {ex.Message}"); }

                if (!_disposed)
                {
                    // Wait up to 10 s; OnDisconnected fires _retryNow early so we
                    // reopen quickly after an exclusive-mode app finishes.
                    _retryNow.Wait(10_000);
                    _retryNow.Reset();
                }
            }
        }

        private void TryRunStream()
        {
            // ── Get device ────────────────────────────────────────────────────
            IMMDevice device = GetDevice(_deviceId);
            if (device == null) return;   // AVR off or not enumerated yet

            try
            {
                // ── Activate IAudioClient ─────────────────────────────────────
                var iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
                int hr = device.Activate(ref iidClient, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out object clientObj);
                if (hr != 0) { Logger.Log($"KeepAlive: Activate failed 0x{hr:X8}"); return; }

                var client = (IAudioClient)clientObj;
                try   { RunClient(client); }
                finally { Marshal.ReleaseComObject(client); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }

        private void RunClient(IAudioClient client)
        {
            // ── Format: use whatever the device's mix engine prefers ──────────
            int hr = client.GetMixFormat(out IntPtr pFormat);
            if (hr != 0) { Logger.Log($"KeepAlive: GetMixFormat failed 0x{hr:X8}"); return; }

            try
            {
                hr = client.Initialize(
                    0,           // AUDCLNT_SHAREMODE_SHARED
                    0,           // no special stream flags
                    5_000_000,   // 500 ms buffer (in 100-ns units)
                    0,
                    pFormat,
                    IntPtr.Zero);
            }
            finally { Marshal.FreeCoTaskMem(pFormat); }

            if (hr != 0) { Logger.Log($"KeepAlive: Initialize failed 0x{hr:X8}"); return; }

            client.GetBufferSize(out uint bufferFrames);

            // ── Render client ─────────────────────────────────────────────────
            var iidRender = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
            client.GetService(ref iidRender, out IntPtr renderPtr);
            var render = (IAudioRenderClient)Marshal.GetObjectForIUnknown(renderPtr);
            Marshal.Release(renderPtr);

            // ── Session events (detect exclusive-mode override) ───────────────
            var iidSession = new Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");
            client.GetService(ref iidSession, out IntPtr sessionPtr);
            var session = (IAudioSessionControl)Marshal.GetObjectForIUnknown(sessionPtr);
            Marshal.Release(sessionPtr);

            var events = new SessionEvents(this);
            session.RegisterAudioSessionNotification(events);

            // ── Pre-fill and start ────────────────────────────────────────────
            render.GetBuffer(bufferFrames, out _);
            render.ReleaseBuffer(bufferFrames, SILENT);   // mark entire buffer as silence

            client.Start();
            Logger.Log($"KeepAlive: stream started ({bufferFrames} frames)");

            try
            {
                while (!_disposed && !events.Disconnected)
                {
                    Thread.Sleep(200);

                    if (client.GetCurrentPadding(out uint padding) != 0) break;
                    uint available = bufferFrames - padding;
                    if (available == 0) continue;

                    if (render.GetBuffer(available, out _) != 0) break;
                    render.ReleaseBuffer(available, SILENT);
                }
            }
            finally
            {
                client.Stop();
                session.UnregisterAudioSessionNotification(events);
                Marshal.ReleaseComObject(session);
                Marshal.ReleaseComObject(render);
                Logger.Log("KeepAlive: stream stopped");
            }
        }

        // Called by SessionEvents on a COM thread when an exclusive-mode app takes over
        internal void OnDisconnected()
        {
            Logger.Log("KeepAlive: session disconnected — will retry in 10 s");
            _retryNow.Set();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private const uint SILENT = 0x2;   // AUDCLNT_BUFFERFLAGS_SILENT

        private static IMMDevice GetDevice(string deviceId)
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
                // GetDevice is not [PreserveSig], so it throws on failure
                en.GetDevice(deviceId, out IMMDevice dev);
                Marshal.ReleaseComObject(en);
                return dev;
            }
            catch { return null; }
        }

        // ── WASAPI COM interfaces (private; IMMDevice/Enumerator reused from AudioDeviceMonitor) ──

        [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
            [PreserveSig] int GetStreamLatency(out long phnsLatency);
            [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
            [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid riid, out IntPtr ppv);
        }

        [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioRenderClient
        {
            [PreserveSig] int GetBuffer(uint numFramesRequested, out IntPtr ppData);
            [PreserveSig] int ReleaseBuffer(uint numFramesWritten, uint dwFlags);
        }

        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            [PreserveSig] int GetState(out int pRetVal);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid pRetVal);
            [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IAudioSessionEvents newNotifications);
            [PreserveSig] int UnregisterAudioSessionNotification(IAudioSessionEvents newNotifications);
        }

        [Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEvents
        {
            void OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string newDisplayName, ref Guid eventContext);
            void OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string newIconPath, ref Guid eventContext);
            void OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, ref Guid eventContext);
            void OnChannelVolumeChanged(uint channelCount, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] newVolumes, uint changedChannel, ref Guid eventContext);
            void OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext);
            void OnStateChanged(int newState);
            void OnSessionDisconnected(int disconnectReason);
        }

        /// <summary>Managed CCW that Windows calls when our audio session changes state.</summary>
        private sealed class SessionEvents : IAudioSessionEvents
        {
            private readonly AudioKeepAlive _owner;
            internal volatile bool Disconnected;

            public SessionEvents(AudioKeepAlive owner) { _owner = owner; }

            // Only OnSessionDisconnected matters; all others are no-ops.
            public void OnDisplayNameChanged(string n, ref Guid ctx) { }
            public void OnIconPathChanged(string p, ref Guid ctx) { }
            public void OnSimpleVolumeChanged(float v, bool m, ref Guid ctx) { }
            public void OnChannelVolumeChanged(uint c, float[] a, uint ch, ref Guid ctx) { }
            public void OnGroupingParamChanged(ref Guid g, ref Guid ctx) { }
            public void OnStateChanged(int s) { }

            public void OnSessionDisconnected(int reason)
            {
                // reason 4 = DisconnectReasonExclusiveModeOverride (bit-streaming player)
                Disconnected = true;
                _owner.OnDisconnected();
            }
        }
    }
}
