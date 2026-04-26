# HTPC-AVR-sync

A lightweight Windows tray app that intercepts your HTPC's volume keys and forwards them directly to your home theater AVR over the network. It also keeps your AVR in sync with your TV's power state.

## Why?

When you use audio bit-streaming (Dolby TrueHD, DTS-HD, Atmos, etc.), Windows' own volume slider has no effect on the actual audio output. This means the volume keys on your keyboard or remote do nothing. HTPC-AVR-sync solves this by capturing those keys and sending the real commands to your AVR instead.

## Features

- **Volume control** — VolumeUp / VolumeDown / Mute keys are intercepted and sent to the AVR
- **Headphone detection** — hotkeys automatically pause when you switch to headphones or any non-AVR output, and resume when you switch back
- **TV power sync** — polls your Samsung TV's REST API; when the TV turns on or off, the AVR follows automatically
- **Start with Windows** — optional autostart via a checkbox (no manual registry editing)
- **Mute sync** — mute state is queried from the AVR before each toggle, so it stays in sync even if you mute from the AVR's own remote
- **Graceful error handling** — failed commands are logged in the UI; a tray alert only fires after 30 seconds of continuous failures

## Supported AVRs

- Denon
- Marantz
- StormAudio

Adding support for another AVR is straightforward — see [Adding a new AVR](#adding-a-new-avr) below.

## Supported TVs

- Samsung Smart TVs with network standby (REST API on port 8001)

## How To Use

1. Download the latest release and run `HTPCAVRVolume.exe`
2. Select your AVR model from the dropdown and enter its IP address
3. Optionally enter your TV's IP address for power sync
4. Click **Save** — the app will minimise to the tray and start working immediately
5. Check **Start with Windows** if you want the app to launch automatically on boot

The volume log at the bottom of the window shows recent events (errors, TV state changes, audio device switches) when the window is open.

## Adding a new AVR

1. Create `AVRDevices/<Name>Device.cs` implementing `IAVRDevice`
2. Add a `case "<Name>":` block in `LoadDevice()` in `HTPCAVRVolume.cs`
3. Add `"<Name>"` to `cmbDevice.Items` in `HTPCAVRVolume.Designer.cs`
4. Register the new file in `HTPCAVRVolume.csproj`

## Building from source

Requires Visual Studio 2019+ or the .NET Framework 4.7.2 SDK.

```powershell
.\build.ps1                        # Debug (default)
.\build.ps1 -Configuration Release
```

Output: `bin\<Configuration>\HTPCAVRVolume.exe`

## Credits

Based on [HTPCAVRVolume](https://github.com/nicko88/HTPCAVRVolume) by nicko88.
