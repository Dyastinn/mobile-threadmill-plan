# Doc links

> Every link here was checked to work in October 2026. Phases take their links from
> this list. Before adding a new one, check it opens.
>
> Expo's library pages use `/versions/latest/`, which always shows the newest Expo. If
> the project stays on an older version, replace `latest` with that version (e.g.
> `v58.0.0`).

## Expo

| Topic | URL |
|---|---|
| Set up your environment (Android device, development build) | https://docs.expo.dev/get-started/set-up-your-environment/ |
| Development builds: introduction | https://docs.expo.dev/develop/development-builds/introduction/ |
| Local app development (`npx expo run:android`) | https://docs.expo.dev/guides/local-app-development/ |
| Continuous Native Generation | https://docs.expo.dev/workflow/continuous-native-generation/ |
| Config plugins: introduction | https://docs.expo.dev/config-plugins/introduction/ |
| `app.json` / app config reference | https://docs.expo.dev/versions/latest/config/app/ |
| Expo Modules API: overview | https://docs.expo.dev/modules/overview/ |
| Expo Modules API: get started (`create-expo-module --local`) | https://docs.expo.dev/modules/get-started/ |
| Expo Modules API: module definition (`Function`, `Events`) | https://docs.expo.dev/modules/module-api/ |
| The New Architecture | https://docs.expo.dev/guides/new-architecture/ |
| Debugging tools | https://docs.expo.dev/debugging/tools/ |
| Unit testing with Jest (`jest-expo`) | https://docs.expo.dev/develop/unit-testing/ |
| ESLint (`npx expo lint`) | https://docs.expo.dev/guides/using-eslint/ |
| Color themes (light/dark) | https://docs.expo.dev/develop/user-interface/color-themes/ |
| App icons | https://docs.expo.dev/develop/user-interface/app-icons/ |

## Expo Router

| Topic | URL |
|---|---|
| Introduction (incl. dynamic routes) | https://docs.expo.dev/router/introduction/ |
| Layouts (`_layout.tsx`, `Stack`) | https://docs.expo.dev/router/basics/layout/ |

## Expo SDK modules used by this project

| Module | URL |
|---|---|
| `expo-sqlite` (incl. `expo-sqlite/kv-store`) | https://docs.expo.dev/versions/latest/sdk/sqlite/ |
| `expo-file-system` | https://docs.expo.dev/versions/latest/sdk/filesystem/ |
| `expo-sharing` | https://docs.expo.dev/versions/latest/sdk/sharing/ |
| `expo-build-properties` | https://docs.expo.dev/versions/latest/sdk/build-properties/ |
| `expo-haptics` | https://docs.expo.dev/versions/latest/sdk/haptics/ |
| `expo-keep-awake` | https://docs.expo.dev/versions/latest/sdk/keep-awake/ |
| `expo-splash-screen` | https://docs.expo.dev/versions/latest/sdk/splash-screen/ |
| `expo-speech` (if voice announcements are ever built) | https://docs.expo.dev/versions/latest/sdk/speech/ |

## React Native

| Topic | URL |
|---|---|
| `PermissionsAndroid` | https://reactnative.dev/docs/permissionsandroid |
| Headless JS (Android) | https://reactnative.dev/docs/headless-js-android |
| `AppState` | https://reactnative.dev/docs/appstate |
| `useWindowDimensions` | https://reactnative.dev/docs/usewindowdimensions |
| `useColorScheme` | https://reactnative.dev/docs/usecolorscheme |
| `Appearance` | https://reactnative.dev/docs/appearance |
| `Alert` | https://reactnative.dev/docs/alert |
| `Switch` | https://reactnative.dev/docs/switch |
| `FlatList` | https://reactnative.dev/docs/flatlist |
| `StyleSheet` | https://reactnative.dev/docs/stylesheet |
| Accessibility | https://reactnative.dev/docs/accessibility |
| Performance overview | https://reactnative.dev/docs/performance |
| React Native DevTools | https://reactnative.dev/docs/react-native-devtools |

## React

| Topic | URL |
|---|---|
| `useState` | https://react.dev/reference/react/useState |
| `useEffect` | https://react.dev/reference/react/useEffect |
| `useRef` | https://react.dev/reference/react/useRef |
| `useContext` | https://react.dev/reference/react/useContext |
| `useMemo` | https://react.dev/reference/react/useMemo |
| `useSyncExternalStore` | https://react.dev/reference/react/useSyncExternalStore |
| Render and commit | https://react.dev/learn/render-and-commit |
| Synchronizing with Effects | https://react.dev/learn/synchronizing-with-effects |
| You Might Not Need an Effect | https://react.dev/learn/you-might-not-need-an-effect |

## TypeScript and JavaScript

| Topic | URL |
|---|---|
| Everyday types | https://www.typescriptlang.org/docs/handbook/2/everyday-types.html |
| Object types (`readonly`, optional properties) | https://www.typescriptlang.org/docs/handbook/2/objects.html |
| Narrowing (discriminated unions, type predicates, exhaustiveness) | https://www.typescriptlang.org/docs/handbook/2/narrowing.html |
| `DataView` | https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/DataView |
| `TextDecoder` | https://developer.mozilla.org/en-US/docs/Web/API/TextDecoder |
| `AbortController` | https://developer.mozilla.org/en-US/docs/Web/API/AbortController |
| `Number.prototype.toFixed` | https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Number/toFixed |
| `Number.prototype.toLocaleString` (what file output must not use) | https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Number/toLocaleString |

## Bluetooth / BLE

| Topic | URL |
|---|---|
| Fitness Machine Service (FTMS) 1.0 spec, Bluetooth SIG | https://www.bluetooth.com/specifications/specs/fitness-machine-service-1-0/ |
| `react-native-ble-manager` repository | https://github.com/innoveit/react-native-ble-manager |
| `react-native-ble-manager` documentation | https://innoveit.github.io/react-native-ble-manager/ |
| Android: Bluetooth permissions | https://developer.android.com/develop/connectivity/bluetooth/bt-permissions |
| Android: communicate in the background over BLE | https://developer.android.com/develop/connectivity/bluetooth/ble/background |

nRF Connect for Mobile (Nordic Semiconductor) is the independent cross-check used in
the Phase 00 runbook; install it from the Play Store.

## Android platform

| Topic | URL |
|---|---|
| Services overview | https://developer.android.com/develop/background-work/services |
| Foreground services overview | https://developer.android.com/develop/background-work/services/fgs |
| Foreground service types (`connectedDevice`, etc.) | https://developer.android.com/develop/background-work/services/fgs/service-types |
| Foreground service types required (Android 14+) | https://developer.android.com/about/versions/14/changes/fgs-types-required |
| Wake locks | https://developer.android.com/develop/background-work/background-tasks/awake/wakelock |
| Doze and App Standby | https://developer.android.com/training/monitoring-device-state/doze-standby |
| Create a notification | https://developer.android.com/develop/ui/views/notifications/build-notification |
| Notification runtime permission | https://developer.android.com/develop/ui/views/notifications/notification-permission |
| `NotificationCompat.Builder` | https://developer.android.com/reference/androidx/core/app/NotificationCompat.Builder |
| `PendingIntent` | https://developer.android.com/reference/android/app/PendingIntent |
| Support multi-window mode | https://developer.android.com/develop/adaptive-apps/guides/support-multi-window-mode |
| Make apps more accessible (48 dp touch targets) | https://developer.android.com/guide/topics/ui/accessibility/apps |
| Test your app's accessibility (TalkBack) | https://developer.android.com/guide/topics/ui/accessibility/testing |
| `adb` | https://developer.android.com/tools/adb |
| `dumpsys` | https://developer.android.com/tools/dumpsys |
| Android Studio Profiler | https://developer.android.com/studio/profile |
| Battery Historian | https://developer.android.com/topic/performance/power/setup-battery-historian |
| Emulator from the command line | https://developer.android.com/studio/run/emulator-commandline |
| Kotlin basic syntax | https://kotlinlang.org/docs/basic-syntax.html |

## Data / SQLite

| Topic | URL |
|---|---|
| Foreign key support | https://sqlite.org/foreignkeys.html |
| Write-Ahead Logging (WAL) | https://www.sqlite.org/wal.html |
| `ON CONFLICT` (`INSERT OR IGNORE` / `OR REPLACE`) | https://sqlite.org/lang_conflict.html |
| `EXPLAIN QUERY PLAN` | https://www.sqlite.org/eqp.html |
| `WITHOUT ROWID` tables | https://www.sqlite.org/withoutrowid.html |
| Date and time functions | https://www.sqlite.org/lang_datefunc.html |
| Node's `node:sqlite` (database tests) | https://nodejs.org/api/sqlite.html |

## Backup and charts

| Topic | URL |
|---|---|
| `fflate` (streaming ZIP) | https://github.com/101arrowz/fflate |
| `react-native-svg` | https://github.com/software-mansion/react-native-svg |

## Testing

| Topic | URL |
|---|---|
| Jest: getting started | https://jestjs.io/docs/getting-started |
| Jest: configuration (`projects`) | https://jestjs.io/docs/configuration |
| Jest: timer mocks (fake timers) | https://jestjs.io/docs/timer-mocks |

## General patterns

| Topic | URL |
|---|---|
| Retry pattern (exponential backoff) | https://learn.microsoft.com/en-us/azure/architecture/patterns/retry |
| expo/expo#51304 (why a headless task that finishes early stops JS timers) | https://github.com/expo/expo/issues/51304 |
