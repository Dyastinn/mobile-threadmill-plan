# Device and emulator setup

> Read `00-Expo-And-React-Native.md` first if you haven't: it explains what a
> development build is and why this project needs one.

## Read this before doing anything else

**The emulator cannot test Bluetooth.** Android emulators have no real Bluetooth
radio, and there is no supported way to scan for or connect to a real external BLE
device (the treadmill) from one. This is a hard platform limitation, not a
configuration problem. No setup fixes it.

So: **the phone is the main development target**, and the emulator is a UI/crash
smoke-test tool only. Good for "does this screen render, does navigation work, does
the app crash on launch," when the phone isn't handy. Never a substitute for the
phone once BLE is involved (which is most of this app).

The canonical, always-current version of the steps below is Expo's
[Set up your environment](https://docs.expo.dev/get-started/set-up-your-environment/)
guide (choose: Android device, development build, build locally). This doc adds what
that guide doesn't know: this PC's quirks and this phone's.

---

## 1. Tools on the PC (once)

- **Node.js 24 LTS.** `node --version` should print `v24.x`. Node 24 also matters for
  tests: `node:sqlite` (used by the database tests) is built in.
- **A JDK for Gradle.** The Expo guide names JDK 17. This machine already has JDK 21
  at `C:\Program Files\Android\openjdk\jdk-21.0.8` from the old MAUI tooling; try it
  first by pointing `JAVA_HOME` at it. If the first `npx expo run:android` fails in
  Gradle with a Java version error, install JDK 17 and point `JAVA_HOME` there instead.
- **The Android SDK** with `platform-tools` (that's `adb`), a recent `build-tools`,
  and the Android 36 platform. Either install Android Studio (simplest: its SDK
  Manager does all of this) or reuse the existing command-line SDK (see "The
  non-writable SDK trap" below).
- Environment variables, then a new terminal:
  ```powershell
  [Environment]::SetEnvironmentVariable('ANDROID_HOME', "$env:LOCALAPPDATA\Android\Sdk", 'User')
  [Environment]::SetEnvironmentVariable('JAVA_HOME', 'C:\Program Files\Android\openjdk\jdk-21.0.8', 'User')
  ```
  and add `%ANDROID_HOME%\platform-tools` to `Path`. Check with `adb --version`.

### The non-writable SDK trap

**This is the trap that will cost you an hour if you skip it.** The existing SDK at
`C:\Program Files (x86)\Android\android-sdk` is **not writable** by a normal user
account. Installing new packages into it fails partway through with confusing errors
(`Failed to read or create install properties file`) after appearing to make some
progress. Gradle may also try to download missing SDK pieces into it during the first
build and fail the same way.

Fix: use an SDK **inside your own user profile**: Android Studio's default
(`%LOCALAPPDATA%\Android\Sdk`) is fine, or a new folder you install into with the
existing `sdkmanager.bat` and `--sdk_root`:

```powershell
$env:JAVA_HOME = "C:\Program Files\Android\openjdk\jdk-21.0.8"
$existingSdk   = "C:\Program Files (x86)\Android\android-sdk"
$newSdkRoot    = "$env:LOCALAPPDATA\Android\Sdk"
New-Item -ItemType Directory -Force -Path $newSdkRoot | Out-Null
```

### Accepting SDK licenses (Windows quirk)

`sdkmanager --licenses` needs a `y` for each license, but **piping into it from
PowerShell (`"y" | sdkmanager ...`) does not work reliably**: the prompts get consumed
by the download-progress phase and the accept never lands. What works: write the `y`s
to a file and redirect it in via `cmd.exe`.

```powershell
$yesFile = "$env:TEMP\sdk_yes.txt"
(1..80 | ForEach-Object { "y" }) -join "`r`n" | Set-Content -Path $yesFile -Encoding ascii

cmd /c "`"$existingSdk\cmdline-tools\latest\bin\sdkmanager.bat`" --sdk_root=`"$newSdkRoot`" --licenses < `"$yesFile`""
cmd /c "`"$existingSdk\cmdline-tools\latest\bin\sdkmanager.bat`" --sdk_root=`"$newSdkRoot`" `"platform-tools`" `"platforms;android-36`" `"build-tools;36.0.0`" < `"$yesFile`""
```

You should see `All SDK package licenses accepted`.

---

## 2. The phone (once): Poco X6 Pro, HyperOS

1. **Developer options**: Settings → About phone → tap **OS version** seven times.
2. In **Settings → Additional settings → Developer options**, turn on:
   - **USB debugging**
   - **Install via USB**
   - **USB debugging (Security settings)**

   Xiaomi splits these into three toggles, and has historically required a signed-in
   Mi account (sometimes with a SIM card inserted) before it lets you enable the last
   two. Without "Install via USB", the first install fails with an install-cancelled
   error even though `adb devices` looks fine. Record the result in
   `phases/phase-00-probe-app/PHASE-00-FINDINGS.md`'s developer-options table.
3. Plug in, accept the "Allow USB debugging?" prompt on the phone (tick "Always allow").
   `adb devices` should list it as `device`, not `unauthorized`.

### Wireless debugging (for every screen-off test)

Doze, the battery mode that matters most to this app, **never happens while the phone
is charging**, and a USB cable charges it. Every screen-off, battery and endurance test
must run unplugged. Wireless debugging keeps `adb` (and the agent's ability to read
logcat) while unplugged:

1. Phone and PC on the same Wi-Fi.
2. Developer options → **Wireless debugging** → on → **Pair device with pairing code**.
   The phone shows an IP:port and a six-digit code.
3. On the PC:
   ```powershell
   adb pair 192.168.1.23:37000      # the pairing IP:port; enter the code when asked
   adb connect 192.168.1.23:41000   # the IP:port shown on the Wireless debugging screen itself
   adb devices
   ```
   Pairing is once; `adb connect` is per session (the port can change after a reboot).

---

## 3. First build onto the phone

From the repo, once Phase 00's task 0.1 has created `src/`:

```powershell
cd src
npm install
npx expo run:android --device
```

The first build takes several minutes (Gradle downloads its dependencies); later
native builds are much faster, and TypeScript changes don't need one at all. `--device`
asks which device if more than one is connected.

After that, the everyday loop is just:

```powershell
npx expo start
```

and open the app on the phone. It finds Metro on the network (same Wi-Fi) or over USB.

**Release build** (for Part H, Phase 12 and Phase 14, and for daily use):

```powershell
npx expo run:android --variant release --device
```

It carries its own JavaScript, so it runs with the PC switched off.

---

## 4. The emulator (optional)

Only worth setting up if you want to look at a screen without the phone nearby.

1. Install an **`x86_64`** system image, not `arm64-v8a`. Even though your phone is
   arm64, this PC's CPU is x86-family, and only the matching architecture gets hardware
   acceleration (WHPX/Hyper-V on Windows). An arm64 image on an x86_64 host runs under
   emulation and is painfully slow.
   ```powershell
   cmd /c "`"$existingSdk\cmdline-tools\latest\bin\sdkmanager.bat`" --sdk_root=`"$newSdkRoot`" `"emulator`" `"system-images;android-36;google_apis;x86_64`" < `"$yesFile`""
   ```
   **Set expectations on time**: the system image alone is roughly 1–1.5 GB. On a slow
   connection this can genuinely take hours; it was still at 12% after 30 minutes the
   one time this was tried on this machine. There's no configuration fix for
   bandwidth. If it's not converging, skip the emulator and use the phone.
2. Create a virtual device:
   ```powershell
   & "$newSdkRoot\cmdline-tools\latest\bin\avdmanager.bat" create avd `
       --name "MyHiTest" `
       --package "system-images;android-36;google_apis;x86_64" `
       --device "pixel_6"
   ```
   (If `avdmanager` isn't under the new root, run the one under `$existingSdk` with
   `ANDROID_SDK_ROOT` set to `$newSdkRoot`.)
3. Start it, wait for it to boot, then build to it exactly like the phone:
   ```powershell
   & "$newSdkRoot\emulator\emulator.exe" -avd MyHiTest
   cd src
   npx expo run:android      # picks the running emulator
   ```

### What to actually check there

- Does the app launch without crashing (services wiring correct, no startup errors)?
- Does every screen navigate correctly?
- Do layouts look right at the emulator's default size, and with the Phase 08
  "Compact" layout forced on? (Real split screen and the 33% case need the phone.)
- Are there any obvious errors in the Metro terminal or `adb logcat -s ReactNativeJS`?

Then always confirm the real thing, anything BLE-related, on the phone.
