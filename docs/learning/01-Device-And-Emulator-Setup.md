# Setup: PC, phone, and (optional) emulator

> Everything you need, in order. Checked on this PC on 2026-10-18; ✅ means it was
> already there. The full list of what's needed and when is in the
> [README](../../README.md#what-you-need).

## First: the emulator can't do Bluetooth

Android emulators have no real Bluetooth, and there's no way around it. So **the phone is
where you develop**, and the emulator is only for looking at screens when the phone
isn't handy.

Expo's own guide is [Set up your environment](https://docs.expo.dev/get-started/set-up-your-environment/)
(choose: Android device, development build, build locally). This doc adds what that
guide can't know about this PC and this phone.

---

## 1. The PC

| Thing | Status | Check with |
|---|---|---|
| Node.js 24 LTS | ✅ v24.17.0 | `node --version` |
| JDK 17 | ✅ 17.0.20, `JAVA_HOME` set | `java -version` |
| Android SDK (platform 36, build-tools 36, platform-tools) | ✅ in `%LOCALAPPDATA%\Android\Sdk` | — |
| `ANDROID_HOME` and `adb` on `PATH` | ⚠️ **not set yet** | `adb --version` |
| Git | ✅ | `git --version` |
| VS Code | ✅ | — |

### Set `ANDROID_HOME` and put `adb` on the `PATH` (the one thing missing)

Run this once in PowerShell, then **close and reopen the terminal** (and VS Code):

```powershell
[Environment]::SetEnvironmentVariable('ANDROID_HOME', "$env:LOCALAPPDATA\Android\Sdk", 'User')
[Environment]::SetEnvironmentVariable('Path', [Environment]::GetEnvironmentVariable('Path','User') + ";$env:LOCALAPPDATA\Android\Sdk\platform-tools", 'User')
```

Check: `adb --version` prints a version.

### If you ever need to install or reinstall SDK parts

The SDK at `%LOCALAPPDATA%\Android\Sdk` is the right one: it's in your user folder, so
you can write to it. There's also an old one at `C:\Program Files (x86)\Android\android-sdk`
that **you can't write to** without admin rights. Installing into it fails halfway with
confusing errors (`Failed to read or create install properties file`). Don't use it.

The easiest way to manage the SDK is Android Studio's SDK Manager (optional). From the
command line, use `sdkmanager` with `--sdk_root` pointing at your user-folder SDK.
**Accepting licenses from PowerShell is broken** (`"y" | sdkmanager ...` doesn't work),
so feed the answers from a file through `cmd.exe`:

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$sdkmanager = "$sdk\cmdline-tools\latest\bin\sdkmanager.bat"
$yesFile = "$env:TEMP\sdk_yes.txt"
(1..80 | ForEach-Object { "y" }) -join "`r`n" | Set-Content -Path $yesFile -Encoding ascii

cmd /c "`"$sdkmanager`" --sdk_root=`"$sdk`" --licenses < `"$yesFile`""
cmd /c "`"$sdkmanager`" --sdk_root=`"$sdk`" `"platform-tools`" `"platforms;android-36`" `"build-tools;36.0.0`" < `"$yesFile`""
```

(If `cmdline-tools` isn't in your user SDK, use the one under
`C:\Program Files (x86)\Android\android-sdk\cmdline-tools\latest\bin\` with the same
`--sdk_root`. Reading from there is fine; writing isn't.)

### Optional, for later

| Thing | For |
|---|---|
| "DB Browser for SQLite" (free) | Opening the app's database file (Phases 06, 12) |
| Android Studio | The SDK manager, the emulator, the profiler (Phase 12, optional) |
| VS Code extensions: ESLint, Expo Tools | Seeing lint errors and `app.json` help in the editor |

---

## 2. The phone (Poco X6 Pro, HyperOS)

### Turn on developer options

1. Settings → About phone → tap **OS version** seven times.
2. Go to **Settings → Additional settings → Developer options** and turn on:
   - **USB debugging**
   - **Install via USB**
   - **USB debugging (Security settings)**

Xiaomi needs all three. It may ask you to sign in to a Mi account, and sometimes to have
a SIM card in, before it lets you turn on the last two. Without "Install via USB", the
first install fails with a "cancelled" error even though `adb devices` looks fine.
Record the result in `PHASE-00-FINDINGS.md`'s developer options table.

3. Plug in with a USB cable that carries data (some are charge-only). Accept "Allow USB
   debugging?" on the phone and tick "Always allow". `adb devices` should list the phone
   as `device`, not `unauthorized`.

### Wireless debugging (needed for every screen-off test)

The phone doesn't go into its deep sleep mode while it's charging, and a USB cable
charges it. So screen-off, battery and endurance tests must run **unplugged**. Wireless
debugging keeps `adb` (and the agent's view of the logs) working without the cable.

1. Phone and PC on the same Wi-Fi.
2. Developer options → **Wireless debugging** → on → **Pair device with pairing code**.
   The phone shows an IP address with a port, and a six-digit code.
3. On the PC:
   ```powershell
   adb pair 192.168.1.23:37000      # the address shown under the pairing code; type the code when asked
   adb connect 192.168.1.23:41000   # the address shown on the main Wireless debugging screen
   adb devices
   ```
   You only pair once. `adb connect` is needed again after a restart (the port can
   change).

### Apps and settings on the phone

| Thing | When |
|---|---|
| **nRF Connect** (free, by Nordic, from the Play Store) | Phase 00 runbook |
| HyperOS: **Autostart** on for the app | Before runbook Part H |
| HyperOS: **Battery saver → No restrictions** for the app | Before runbook Part H |
| HyperOS: app **locked in Recents** (padlock) | Before runbook Part H |
| Android battery optimisation off for the app | ✅ already done |
| Google Drive or email | Phase 09 (somewhere to send backups) |

The HyperOS settings can only be set once the app is installed. Some HyperOS versions
reset them after a reinstall, so check again after reinstalling.

---

## 3. First build onto the phone

Once Phase 00's task 0.1 has created `src/`:

```powershell
cd src
npm install
npx expo run:android --device
```

The first build takes several minutes (it downloads Gradle's parts). After that, native
builds are faster, and code changes don't need a build at all. `--device` asks which
device if more than one is connected.

Then, day to day:

```powershell
npx expo start
```

and open the app on the phone. It finds Metro over Wi-Fi (same network) or USB.

**Release build** (for Part H, Phase 12, Phase 14, and daily use):

```powershell
npx expo run:android --variant release --device
```

It includes your code, so it runs with the PC switched off.

### If something goes wrong

| Problem | Likely cause |
|---|---|
| `adb` not found | `ANDROID_HOME`/`PATH` not set, or the terminal wasn't reopened |
| `adb devices` shows `unauthorized` | Accept the prompt on the phone; unplug and plug in again |
| Install cancelled / `INSTALL_FAILED_USER_RESTRICTED` | "Install via USB" is off (needs the Mi account) |
| Gradle complains about the Java version | `JAVA_HOME` isn't JDK 17 |
| "SDK location not found" | `ANDROID_HOME` isn't set |
| The app opens but shows "Unable to load script" | Metro isn't running, or the phone isn't on the same Wi-Fi |

---

## 4. The emulator (optional)

Only worth it if you want to look at screens without the phone.

1. Install an **`x86_64`** system image, not `arm64-v8a`. Your phone is arm64, but this
   PC's processor is x86, and only the matching type runs fast. An arm64 image here is
   painfully slow.
   ```powershell
   cmd /c "`"$sdkmanager`" --sdk_root=`"$sdk`" `"emulator`" `"system-images;android-36;google_apis;x86_64`" < `"$yesFile`""
   ```
   **It's big**: the image alone is 1–1.5 GB. On a slow connection it can take hours (it
   was at 12% after 30 minutes the one time it was tried here). If it's crawling, skip
   the emulator and use the phone.
2. Create a virtual phone:
   ```powershell
   & "$sdk\cmdline-tools\latest\bin\avdmanager.bat" create avd `
       --name "MyHiTest" `
       --package "system-images;android-36;google_apis;x86_64" `
       --device "pixel_6"
   ```
3. Start it, wait for it to boot, then build to it like the phone:
   ```powershell
   & "$sdk\emulator\emulator.exe" -avd MyHiTest
   cd src
   npx expo run:android      # picks the running emulator
   ```

### What it's good for

- Does the app start without crashing?
- Does every screen open?
- Do layouts look right, including with the Phase 08 "Compact" layout on? (Real split
  screen needs the phone.)
- Any errors in the Metro terminal or `adb logcat -s ReactNativeJS`?

Anything involving Bluetooth: always check on the phone.
