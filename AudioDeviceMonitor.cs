using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;

namespace HTPCAVRVolume
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorCoClass { }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant propvar);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;
        // VT_LPWSTR = 31
        public string AsString() => vt == 31 ? Marshal.PtrToStringUni(pwszVal) : null;
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out int pdwState);
    }

    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);            // vtable 3
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);          // vtable 4
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);               // vtable 5
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, IntPtr guid);      // vtable 6
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);              // vtable 7
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, IntPtr guid);  // vtable 8
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);          // vtable 9
        // stubs to keep vtable alignment correct up to VolumeStepUp
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, IntPtr guid);        // vtable 10
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, IntPtr guid);    // vtable 11
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);                // vtable 12
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);            // vtable 13
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, IntPtr guid);         // vtable 14
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);                 // vtable 15
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);                 // vtable 16
        [PreserveSig] int VolumeStepUp(IntPtr pguidEventContext);                                   // vtable 17
        [PreserveSig] int VolumeStepDown(IntPtr pguidEventContext);                                 // vtable 18
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);                    // vtable 19
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    internal class AudioDeviceMonitor : IMMNotificationClient, IDisposable
    {
        // eRender=0 (output), eMultimedia=1 (role used by media players)
        private const int EDataFlowRender = 0;
        private const int ERoleMultimedia = 1;
        private const int ClsctxAll = 23;

        private readonly IMMDeviceEnumerator _enumerator;
        private readonly Control _uiControl;
        private string _avrDeviceId;
        private bool _avrActive;
        private bool _monitoring;
        private bool _disposed;

        public event EventHandler AVRBecameActive;
        public event EventHandler AVRBecameInactive;

        public bool IsAVRActive => _avrActive;

        public AudioDeviceMonitor(Control uiControl)
        {
            _uiControl = uiControl;
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
        }

        // Returns the resolved device ID — may differ from avrDeviceId if the GUID
        // changed and we recovered via the saved device name.
        public string Start(string avrDeviceId, string avrDeviceName = null)
        {
            if (_monitoring)
                _enumerator.UnregisterEndpointNotificationCallback(this);

            _avrDeviceId = avrDeviceId;
            string current = GetCurrentDefaultDeviceId();

            // If the saved GUID no longer matches, try to find the device by name.
            // This self-heals when Windows re-enumerates the audio device (e.g. after
            // a driver update or AVR power cycle) and assigns a new GUID.
            if (current != avrDeviceId && !string.IsNullOrEmpty(avrDeviceName))
            {
                string resolvedId = FindDeviceIdByName(avrDeviceName);
                if (!string.IsNullOrEmpty(resolvedId) && resolvedId != avrDeviceId)
                {
                    Logger.Log($"AudioMonitor: GUID changed — resolved '{avrDeviceName}' -> {resolvedId}");
                    _avrDeviceId = resolvedId;
                }
            }

            _avrActive = current == _avrDeviceId;
            Logger.Log($"AudioMonitor.Start: avrId={_avrDeviceId}, currentId={current}, avrActive={_avrActive}");
            _enumerator.RegisterEndpointNotificationCallback(this);
            _monitoring = true;
            return _avrDeviceId;
        }

        // PKEY_Device_FriendlyName = {A45C254E-DF1C-4EFD-8020-67D146A850E0}, pid=14
        private static readonly PropertyKey PkeyFriendlyName = new PropertyKey
        {
            fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            pid   = 14
        };

        public static string GetDeviceFriendlyName(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
                if (en.GetDevice(deviceId, out IMMDevice dev) != 0) { Marshal.ReleaseComObject(en); return null; }
                string name = GetFriendlyName(dev);
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
                return name;
            }
            catch { return null; }
        }

        private static string GetFriendlyName(IMMDevice device)
        {
            try
            {
                var key = PkeyFriendlyName;
                if (device.OpenPropertyStore(0 /*STGM_READ*/, out IPropertyStore store) != 0) return null;
                store.GetValue(ref key, out PropVariant pv);
                string name = pv.AsString();
                Marshal.ReleaseComObject(store);
                return name;
            }
            catch { return null; }
        }

        // Returns the device ID of the first active render endpoint whose friendly
        // name contains namePart (case-insensitive).
        public static string FindDeviceIdByName(string namePart)
        {
            if (string.IsNullOrEmpty(namePart)) return null;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
                en.EnumAudioEndpoints(EDataFlowRender, 0x1 /*DEVICE_STATE_ACTIVE*/, out IMMDeviceCollection col);
                col.GetCount(out uint count);
                string found = null;
                for (uint i = 0; i < count && found == null; i++)
                {
                    col.Item(i, out IMMDevice dev);
                    string name = GetFriendlyName(dev);
                    if (name != null && name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        dev.GetId(out found);
                        Logger.Log($"FindDeviceIdByName: '{namePart}' matched '{name}' -> {found}");
                    }
                    Marshal.ReleaseComObject(dev);
                }
                Marshal.ReleaseComObject(col);
                Marshal.ReleaseComObject(en);
                return found;
            }
            catch { return null; }
        }

        public void Stop()
        {
            if (!_monitoring) return;
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _monitoring = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            Marshal.ReleaseComObject(_enumerator);
        }

        public static string GetCurrentDefaultDeviceId()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
                if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out IMMDevice device) != 0)
                    return null;
                device.GetId(out string id);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
                return id;
            }
            catch { return null; }
        }

        public static void SetMasterVolumeToMax()
        {
            Logger.Log("SetMasterVolumeToMax: queuing on MTA thread");
            System.Threading.ThreadPool.QueueUserWorkItem(_ => SetMasterVolumeToMaxMTA());
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private static void SetMasterVolumeToMaxMTA()
        {
            Logger.Log("SetMasterVolumeToMaxMTA: entering on ThreadPool (MTA)");
            bool comSucceeded = false;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out IMMDevice device);
                if (hr != 0) { Marshal.ReleaseComObject(enumerator); goto fallback; }

                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object volObj);
                var vol = (IAudioEndpointVolume)volObj;

                // Diagnose: read current level and hardware support
                vol.GetMasterVolumeLevelScalar(out float levelBefore);
                vol.QueryHardwareSupport(out uint hwSupport);
                Logger.Log($"SetMasterVolumeToMaxMTA: level={levelBefore:F3} hwSupport=0x{hwSupport:X} (vol bit={(hwSupport & 1) != 0})");

                // Try direct scalar set first (may crash on some drivers)
                bool scalarWorked = false;
                try
                {
                    int setHr = vol.SetMasterVolumeLevelScalar(1.0f, IntPtr.Zero);
                    vol.GetMasterVolumeLevelScalar(out float levelAfterScalar);
                    Logger.Log($"SetMasterVolumeToMaxMTA: SetMasterVolumeLevelScalar hr=0x{setHr:X8} -> level={levelAfterScalar:F3}");
                    scalarWorked = setHr == 0 && levelAfterScalar >= 0.99f;
                }
                catch (Exception ex) { Logger.Log($"SetMasterVolumeToMaxMTA: SetMasterVolumeLevelScalar threw {ex.GetType().Name} — will try VolumeStepUp"); }

                if (!scalarWorked)
                {
                    // Fallback: step up via VolumeStepUp until we reach max
                    vol.GetVolumeStepInfo(out uint currentStep, out uint stepCount);
                    Logger.Log($"SetMasterVolumeToMaxMTA: VolumeStepUp path — step {currentStep}/{stepCount}");
                    uint stepsNeeded = stepCount > 0 ? (stepCount - 1 - currentStep) : 0;
                    for (uint i = 0; i < stepsNeeded; i++)
                        vol.VolumeStepUp(IntPtr.Zero);
                    vol.GetMasterVolumeLevelScalar(out float levelAfterStep);
                    Logger.Log($"SetMasterVolumeToMaxMTA: after {stepsNeeded} VolumeStepUp calls -> level={levelAfterStep:F3}");
                    comSucceeded = levelAfterStep >= 0.99f;
                }
                else
                {
                    comSucceeded = true;
                }

                Marshal.ReleaseComObject(vol);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
            catch (Exception ex)
            {
                Logger.LogException("SetMasterVolumeToMaxMTA (COM path)", ex);
            }

            fallback:
            if (!comSucceeded)
            {
                // Last resort: legacy winmm waveOut volume (0xFFFFFFFF = max on both channels)
                try
                {
                    int r = waveOutSetVolume(IntPtr.Zero, 0xFFFFFFFF);
                    Logger.Log($"SetMasterVolumeToMaxMTA: waveOutSetVolume fallback result={r}");
                }
                catch (Exception ex) { Logger.LogException("SetMasterVolumeToMaxMTA (waveOut fallback)", ex); }
            }
        }

        void IMMNotificationClient.OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
        {
            Logger.Log($"OnDefaultDeviceChanged: flow={flow} role={role} deviceId={defaultDeviceId}");
            if (flow != EDataFlowRender || role != ERoleMultimedia) return;
            bool nowActive = defaultDeviceId == _avrDeviceId;
            Logger.Log($"OnDefaultDeviceChanged: nowActive={nowActive} wasActive={_avrActive} avrId={_avrDeviceId}");
            if (nowActive == _avrActive) return;
            _avrActive = nowActive;
            try
            {
                if (_uiControl.IsHandleCreated && !_uiControl.IsDisposed)
                {
                    Logger.Log($"OnDefaultDeviceChanged: invoking {(nowActive ? "AVRBecameActive" : "AVRBecameInactive")} on UI thread");
                    _uiControl.BeginInvoke(nowActive
                        ? (Action)(() => AVRBecameActive?.Invoke(this, EventArgs.Empty))
                        : (Action)(() => AVRBecameInactive?.Invoke(this, EventArgs.Empty)));
                }
                else
                {
                    Logger.Log("OnDefaultDeviceChanged: skipped BeginInvoke — handle not ready or disposed");
                }
            }
            catch (ObjectDisposedException) { Logger.Log("OnDefaultDeviceChanged: ObjectDisposedException swallowed"); }
        }

        void IMMNotificationClient.OnDeviceStateChanged(string deviceId, int newState) { }
        void IMMNotificationClient.OnDeviceAdded(string deviceId) { }
        void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
        void IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }
}
