# Android Emulator Setup

> Read `00-What-Is-Maui.md` first if you haven't.

## Read this before doing anything else

**The emulator cannot test Bluetooth.** Android emulators have no real Bluetooth
radio, and there is no supported way to scan for or connect to a real external BLE
device (the treadmill) from one. This is a hard platform limitation, not a
configuration problem — no setup fixes it.

So: the emulator is a **UI/crash smoke-test tool only**. Good for "does this screen
render, does navigation work, does the app crash on launch." Never a substitute for
testing on your actual phone once BLE is involved (which is most of this app).

If all you want is to see a screen you just built without a USB cable, keep reading.
If you specifically want to test scanning/connecting/control, skip this and sideload
to your phone instead (see the main `phases/phase-00-probe-app/HUMAN-RUNBOOK.md` for
that flow).

---

## Prerequisites

You need the Android SDK's command-line tools already installed (they are, if you've
built this project before — check for `C:\Program Files (x86)\Android\android-sdk`).
You also need a JDK; this machine already has one bundled at
`C:\Program Files\Android\openjdk\jdk-21.0.8` from the Android tooling install.

## Step 1 — Use a folder you can actually write to

**This is the trap that will cost you an hour if you skip it.** The existing SDK at
`C:\Program Files (x86)\Android\android-sdk` is **not writable** by a normal user
account — installing new packages (the emulator, a system image) into it fails
partway through with confusing errors (`Failed to read or create install properties
file`) after appearing to make some progress.

Fix: install the emulator and system image into a **new folder inside your own user
profile** instead, e.g. `C:\Users\<you>\android-emulator-sdk`. You can still reuse the
existing SDK's `sdkmanager.exe` to do the installing — you just point it at a
different destination with `--sdk_root`.

```powershell
$env:JAVA_HOME = "C:\Program Files\Android\openjdk\jdk-21.0.8"
$existingSdk   = "C:\Program Files (x86)\Android\android-sdk"
$newSdkRoot    = "$env:USERPROFILE\android-emulator-sdk"
New-Item -ItemType Directory -Force -Path $newSdkRoot | Out-Null
```

## Step 2 — Accept the SDK licenses (Windows quirk)

`sdkmanager --licenses` needs to receive a `y` for each license shown, but **piping
into it from PowerShell (`"y" | sdkmanager ...`) does not work reliably** — the
license prompts get consumed by the download-progress phase and the accept never
lands. What actually works: write the `y`s to a file and redirect it in via `cmd.exe`,
not a PowerShell pipe.

```powershell
$yesFile = "$env:TEMP\sdk_yes.txt"
(1..80 | ForEach-Object { "y" }) -join "`r`n" | Set-Content -Path $yesFile -Encoding ascii

cmd /c "`"$existingSdk\cmdline-tools\latest\bin\sdkmanager.bat`" --sdk_root=`"$newSdkRoot`" --licenses < `"$yesFile`""
```

You should see `All SDK package licenses accepted` at the end.

## Step 3 — Install the emulator and a system image

Pick an **`x86_64`** system image, not `arm64-v8a` — even though your phone is
arm64, this PC's CPU is x86-family, and only the matching architecture gets hardware
acceleration (WHPX/Hyper-V on Windows). An arm64 image on an x86_64 host runs under
emulation and is painfully slow.

```powershell
cmd /c "`"$existingSdk\cmdline-tools\latest\bin\sdkmanager.bat`" --sdk_root=`"$newSdkRoot`" `"emulator`" `"platform-tools`" `"system-images;android-36;google_apis;x86_64`" < `"$yesFile`""
```

**Set expectations on time**: the system image alone is roughly 1–1.5 GB, plus the
emulator package (~250–300 MB). On a slow connection this can genuinely take hours,
not minutes — it was still at 12% after 30 minutes the one time this was tried on
this machine. If it's crawling, there's no configuration fix; it's just a bandwidth
problem. Check progress with:

```powershell
Get-ChildItem $newSdkRoot -Recurse -File | Measure-Object -Property Length -Sum |
    Select-Object Count, @{N='TotalMB';E={$_.Sum/1MB}}
```

If it's not converging in a reasonable time for you, it's genuinely fine to skip the
emulator and just test on your phone via USB (see Step 5 below for the same
information applied to a real device instead).

## Step 4 — Create an AVD (Android Virtual Device)

Once the image is installed:

```powershell
$env:ANDROID_SDK_ROOT = $newSdkRoot
& "$newSdkRoot\cmdline-tools\latest\bin\avdmanager.bat" create avd `
    --name "MyHiTest" `
    --package "system-images;android-36;google_apis;x86_64" `
    --device "pixel_6"
```

`avdmanager` will ask if you want a custom hardware profile — answering `no` (default)
is fine.

## Step 5 — Launch it and install the app

```powershell
& "$newSdkRoot\emulator\emulator.exe" -avd MyHiTest
```

This opens a window with the emulated phone. Wait for it to fully boot (first boot
is slower — a couple of minutes). Then, from the project's `src/` folder:

```powershell
dotnet build MyHi.Companion/MyHi.Companion.csproj -t:Run -f net10.0-android
```

This detects the running emulator the same way it would detect a USB-connected phone,
installs the app, and launches it.

## What to actually check here

Since real BLE is off the table, use the emulator for:
- Does the app launch without crashing (DI wiring correct, no startup exceptions)?
- Does every screen in the Home list navigate correctly?
- Do layouts look right at the emulator's default size?
- Are there any obvious binding errors (blank labels where text should be, buttons
  that don't visually respond)?

Then always confirm the real thing — anything BLE-related — on your phone.
