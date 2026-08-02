# Technology Stack: decision record

> This project has one developer, indefinitely. Every dependency here has to be
> justified on that basis, not on theoretical best-practice: maintainability over
> purity, no abstraction or pattern without a concrete payoff, and every library
> weighed against "is what it saves worth what it costs to understand and keep
> working." Where a choice is genuinely close, that's said plainly rather than
> dressed up as obviously correct.
>
> Verified against current (August 2026) package status where that status matters
> to the decision. Flagged inline where research changed the picture from what
> `00-Project-Plan.md` originally assumed.

## Quick-scan summary

| Technology | Verdict | Risk |
|---|---|---|
| .NET MAUI (Android-only) | Keep | Low — already built, matches existing C#/.NET skill |
| `Plugin.BLE` | Keep | Low — working since Phase 00, small swappable surface |
| `Microsoft.Data.Sqlite` | Keep | Low — Microsoft-maintained, right tool for the buffered-write pattern |
| `CommunityToolkit.Mvvm` | Keep | Low — official, source-generated, zero runtime cost |
| `CommunityToolkit.Maui` (`FileSaver` only) | Keep | Low — narrow usage, actively maintained |
| MAUI Shell (navigation) | Keep | Low — part of the SDK, not a dependency |
| **`LiveCharts2`** | **Watch — has a real gap** | **Medium — see below, not yet at 1.0 after years in RC** |

Everything below is "keep" on solid ground except the chart library, which is
worth reading even if you don't act on it yet.

---

## .NET MAUI (Android-only)

### What problem does it solve?
The app needs a native Android UI, background BLE handling that survives screen
lock, and local SQLite storage, all written by someone who already knows
C#/.NET (per `00-Project-Plan.md`'s own framing) rather than starting a second
language from zero.

### Why are we using it?
It's the only option in the comparison below that doesn't require learning a new
language *and* a new UI toolkit *and* a new BLE story simultaneously. Phase 00
already proved it end to end (BLE scan/connect/GATT, background-safe patterns,
SQLite, a working installable APK), so re-litigating the framework now would mean
throwing that away.

### Alternatives

1. **Native Android (Kotlin + Jetpack Compose)**
   - Pros: zero abstraction between you and the platform, every Android API
     available on day one, the largest body of BLE-on-Android tutorials/Stack
     Overflow answers to draw from.
   - Cons: an entirely new language and UI framework to learn at the same time as
     BLE/FTMS, which is already the hard part of this project.
   - Learning curve: steep if Kotlin/Compose is new.
   - When better: if Android-specific APIs turn out to be needed constantly. This
     project's Android-specific needs (foreground service types, HyperOS
     battery quirks) are real but narrow, not pervasive.
2. **Flutter (Dart)**
   - Pros: strong BLE plugin ecosystem (`flutter_blue_plus`), good performance,
     large community.
   - Cons: a new language (Dart) *and* new BLE APIs *and* no code-reuse from any
     existing C# knowledge, strictly more to learn than MAUI for this developer.
   - Learning curve: steep (new language).
   - When better: if the target were cross-platform (iOS too) and Dart were
     already familiar. Neither applies here.
3. **React Native**
   - Pros: JavaScript/TypeScript, huge ecosystem.
   - Cons: BLE support is entirely third-party and historically inconsistent
     across Android versions; same "new language for no reason" problem as
     Flutter for a C#-fluent, Android-only developer.
   - Learning curve: moderate if JS is already known, steep otherwise.
   - When better: existing JS/React team building a cross-platform app, not this
     project.

### Why not the alternatives?
All three cost a new language for a single-developer, Android-only, already-C#
project with zero payoff. Cross-platform reach (Flutter/RN's main selling point)
is explicitly a non-goal (`00-Project-Plan.md` rules out iOS). Native Kotlin is the
only one with a real argument (maximum platform control), but this project's
Android-specific surface (foreground services, BLE, HyperOS battery settings) is
narrow enough that MAUI's platform-specific escape hatches (`Platforms/Android/`)
already cover it without giving up C#.

### Long-term considerations
Standard, Microsoft-maintained, tracks .NET's own release cadence. Not easily
"replaceable" in the sense of swapping a library. A framework choice is closer to
a foundation than a dependency. Performance is adequate for this app's actual
workload (a dashboard updating at ≤4 Hz, not a game). Maintenance cost is the
lowest of the three alternatives given existing C# fluency.

### Practical example
`src/MyHi.Companion/`: the whole app. Already built and running (Phase 00).

---

## `Plugin.BLE` (`dotnet-bluetooth-le`)

### What problem does it solve?
Raw Android BLE (`BluetoothGatt` and its callback interface) is verbose,
callback-heavy, and the single biggest source of undocumented device-specific
quirks (GATT error 133, connect/discover timing) in any BLE app. Something has to
own that callback plumbing.

### Why are we using it?
It's already the foundation of Phase 00's working probe app: `TreadmillConnection`,
`BleScanner`, `ControlPointClient` are all built on `Plugin.BLE`'s `IAdapter`/
`IDevice`/`ICharacteristic`. It already has this specific treadmill's GATT 133
mitigations (autoConnect:false, close-before-reconnect, the 200ms discovery delay)
validated against real hardware.

### Alternatives

1. **Direct Android bindings** (`Android.Bluetooth.*` in `Platforms/Android/`)
   - Pros: full control, every Android BLE quirk directly addressable, no
     abstraction layer between you and the bug.
   - Cons: several hundred lines of callback plumbing to write and maintain
     yourself: threading, state machine, MTU negotiation, all from scratch.
   - Learning curve: steep, raw Android callback/threading model.
   - When better: if `Plugin.BLE`'s own abstraction ever actively blocks a fix
     `05-FTMS-Protocol.md` needs. The original plan already names this as the
     specific escape hatch, not a general preference.
2. **Shiny.BluetoothLE**
   - Pros: actively maintained (v4.0.1, updated March 2026), modern reactive
     (`IObservable`-based) API, built specifically to fill the gap left by BLE
     never landing in MAUI's own roadmap, generally cleaner reconnect/observability
     story than `Plugin.BLE`.
   - Cons: a second async paradigm (Rx observables) layered on top of the
     `async`/`await` used everywhere else in this codebase; zero code has been
     written against it in this project, versus `Plugin.BLE`'s validated,
     working Phase 00; switching now means re-deriving the GATT 133 mitigations
     against a different API shape with no guarantee they translate cleanly.
   - Learning curve: moderate-to-steep if Rx/observables are unfamiliar.
   - When better: a greenfield BLE project, or if `Plugin.BLE`'s maintenance
     visibly stalls in the future (worth a re-check then, not now).
3. **Hand-rolled minimal wrapper** (just the handful of GATT operations this app
   actually needs: scan, connect, discover, read a few characteristics, subscribe,
   write control point)
   - Pros: zero dependency, smallest possible surface. This app's BLE needs
     genuinely are narrow and fixed, unlike a general-purpose BLE library's scope.
   - Cons: reinvents exactly the boilerplate `Plugin.BLE` already solved, with none
     of Phase 00's validation carried over.
   - Learning curve: same as direct bindings, without prior art to lean on.
   - When better: only if the fixed, small feature set turns out to be genuinely
     stable *and* `Plugin.BLE` becomes a liability. Speculative today.

### Why not the alternatives?
Direct bindings rejected because Phase 00 is done, working, and already paid the
callback-plumbing cost via `Plugin.BLE`. Repaying it now for a single-platform app
buys nothing. Shiny rejected on switching cost, not quality: its reactive model may
well be better in the abstract, but this project already has hardware-validated
mitigations against *this specific treadmill's* quirks sitting in `Plugin.BLE`
code, and that validation is the expensive part, not the library choice. A
hand-rolled wrapper rejected as strictly more code for equivalent behavior with
nothing written yet.

### Long-term considerations
**Worth naming honestly:** `Plugin.BLE` is swappable in principle (MIT, ~900+
GitHub stars, broadly referenced in MAUI BLE tutorials) but *not* currently
isolated behind a clean seam. `TreadmillConnection.GattCharacteristicInfo.Native`
exposes raw `Plugin.BLE` `ICharacteristic` objects, and the diagnostic screens
(hex dump, GATT tree) deliberately reach through to them. That's a reasonable
trade for a diagnostic tool that needs raw access, but it means `TreadmillConnection`
itself isn't the real isolation boundary. **The actual seam is `ITreadmillService`**
(Phase 01b onward): nothing above that interface touches `Plugin.BLE` types at
all, which is where a future swap would actually happen if it ever needs to.
Maintenance cadence on the package itself is moderate-confidence from available
data, worth a quick check before Phase 02 if reconnect behavior turns out flaky.

### Practical example
`src/MyHi.Companion/Features/Bluetooth/TreadmillConnection.cs` (existing),
extended with reconnect/backoff in Phase 02.

---

## `Microsoft.Data.Sqlite`

### What problem does it solve?
Workout history and per-workout telemetry need durable local storage. The
telemetry table specifically writes in bursts (buffered ~30-60s, then one
transaction, per `14-Database.md`) rather than one row at a time. That write
pattern is the crux of this decision, not "which SQLite wrapper is nicest."

### Why are we using it?
Explicit control over `SqliteTransaction`/`SqliteCommand` reuse matters for the
buffered-flush pattern (720 samples/hour, batched), the developer is already
ADO.NET-fluent, and it's maintained by the same Microsoft team that ships EF Core's
SQLite provider, so its lifecycle tracks .NET's own, not a solo maintainer's spare
time.

### Alternatives

1. **`sqlite-net-pcl`**, the one Microsoft's *own MAUI documentation* actually
   recommends for MAUI apps.
   - Pros: attribute-mapped POCOs, minimal code for simple CRUD
     (`conn.Table<Workout>().Where(...)`), officially the MAUI-docs default, lowest
     boilerplate of any option here.
   - Cons: it's a thin reflection-based ORM. Bulk-insert/transaction control is
     less explicit than raw ADO.NET. Looping `Insert()` through the ORM for a
     buffered batch of dozens of samples is exactly the pattern that favors raw
     `SqliteCommand` reuse inside one transaction instead.
   - Learning curve: lowest of all options here.
   - When better: apps that are pure CRUD with no hot-path bulk-insert
     requirement, which is most MAUI apps, just not this one's sample table.
2. **Raw `System.Data.SQLite`** (the older, non-Microsoft ADO.NET provider)
   - Pros: none over `Microsoft.Data.Sqlite` for a new project.
   - Cons: not Microsoft-maintained, documented mobile/MAUI packaging friction
     (e.g. current reports of `e_Sqlite3` exceptions on iOS, a live issue, not
     historical). No reason to choose it today over `Microsoft.Data.Sqlite`.
   - Learning curve: same shape as `Microsoft.Data.Sqlite` (both ADO.NET).
   - When better: legacy codebases already built on it. Not a fresh project.
3. **LiteDB** (embedded document/NoSQL store)
   - Pros: zero-schema, native C# object serialization, no SQL to write, single
     file like SQLite.
   - Cons: this app's data is genuinely relational. One workout has many samples,
     `14-Database.md`'s indexed `Workout(StartedAtUtc)` aggregate queries (Phase
     10's daily/weekly/monthly sums) are exactly where SQL's `GROUP BY` beats a
     document store. Smaller, single-maintainer project with far less
     battle-testing than SQLite itself.
   - Learning curve: low to start, but "fast indexed aggregate" pushes you into
     LiteDB's own query API instead of transferable SQL.
   - When better: apps storing loosely-structured documents, not tabular
     time-series data with real aggregate-query needs.

### Why not the alternatives?
`sqlite-net-pcl` is the closest real alternative and this is a genuine judgement
call, not an obvious win. Everywhere in this app *except* the sample-table writes,
`sqlite-net-pcl` would arguably be simpler. It loses specifically because of Phase
6/7's buffered-transaction write pattern, which is the one place raw
transaction/command control earns its extra verbosity. Raw `System.Data.SQLite`
rejected for having no upside and a live mobile-packaging issue. LiteDB rejected
because the domain is relational and Phase 10's aggregates need SQL, not a
document query language.

### Long-term considerations
Microsoft-maintained, tracks .NET's release cadence. Lowest abandonment risk of
the four. Swapping to `sqlite-net-pcl` later is moderate cost (repository classes
rewritten, but the schema in `14-Database.md` stays valid either way, since it's
plain SQLite under both). Lowest per-row overhead of the options, which matters at
720 rows/hour compounding over years. The cost paid is more typing per query (raw
SQL vs. ORM), but that SQL is also directly runnable in any SQLite browser to
inspect the `.db` file by hand, which the project needs anyway for debugging and
the endurance-testing phase.

### Practical example
`14-Database.md` (schema), Phase 06's `WorkoutSampleBuffer` (buffered
single-transaction flush), Phase 10's aggregate queries.

---

## `CommunityToolkit.Mvvm`

### What problem does it solve?
MVVM requires two flavors of pure-boilerplate code repeated across every
ViewModel: `INotifyPropertyChanged` wiring for every bindable property, and
`ICommand` wrapper classes for every button/action. Both are mechanical and both
have a real, silent bug mode (forget to raise `PropertyChanged` and the UI just...
doesn't update, no exception, no warning).

### Why are we using it?
It's Microsoft's own official MVVM library, generates the boilerplate at
**compile time** via source generators (inspectable: go-to-definition on a
`[ObservableProperty]` field takes you to real generated C#, not a black box), has
zero runtime reflection cost, and is already referenced throughout the phase docs
(`[ObservableProperty]`/`[RelayCommand]` in Phase 03/08's ViewModel skeletons).

### Alternatives

1. **Hand-write `INotifyPropertyChanged` yourself**
   - Pros: zero dependency, full transparency, no generator to learn.
   - Cons: across a dozen-plus ViewModels over 15 phases, this is a lot of
     repeated `SetProperty`/backing-field ceremony with a real copy-paste bug
     surface.
   - Learning curve: lowest to start (it's C# you already know), but the
     *ongoing* cost is higher since every new property is manual, forever.
   - When better: a tiny app with 1-2 ViewModels where the ceremony never repeats
     enough to matter, not this project's scope.
2. **Fody.PropertyChanged** (IL-weaving, auto-implements
   `INotifyPropertyChanged` for every property in a marked class)
   - Pros: even less visible code than `[ObservableProperty]`, no per-property
     attribute needed.
   - Cons: IL weaving happens at build time with no generated source file to
     inspect, harder to debug when something's wrong than a source generator's
     "go look at the generated file." Historically more build-tooling friction
     across SDK upgrades than source-generator approaches. Smaller long-term
     certainty than a Microsoft-owned package.
   - Learning curve: low to use, higher to debug when it misbehaves.
   - When better: a stylistic preference for even less attribute noise, traded
     against inspectability. Not a clear win here.
3. **ReactiveUI**
   - Pros: far more powerful: composable observables, `WhenAnyValue`, built for
     genuinely complex reactive pipelines.
   - Cons: an entirely different mental model (Rx) on top of MVVM. This app's
     actual complexity (a handful of properties reacting to BLE events on a
     timer) doesn't need composable reactive chains; it needs "property changed,
     command executed," which `CommunityToolkit.Mvvm` already does with far less
     to learn.
   - Learning curve: steep.
   - When better: apps with genuinely complex cross-property reactive logic
     (e.g. financial/trading UIs), not a single-treadmill dashboard.

### Why not the alternatives?
Hand-writing rejected purely on repetition-across-a-dozen-ViewModels grounds.
It's exactly the kind of mechanical, low-judgement code a source generator should own,
so review effort goes to the logic that actually matters (BLE parsing, state
machines), not `PropertyChanged` plumbing. Fody rejected because a project meant
to be understood by one person over years benefits more from inspectable generated
code than from marginally less attribute noise. ReactiveUI rejected as solving a
problem this app doesn't have, at a learning-curve cost it can't justify.

### Long-term considerations
Officially Microsoft-owned, ships alongside .NET's release cadence, about as
"standard" as this ecosystem gets. Zero runtime cost (compile-time generation).
Replacing it later means regenerating every ViewModel's boilerplate by hand or via
Fody. Moderate cost, low probability of ever needing to.

### Practical example
Phase 08's `SettingsViewModel`, Phase 03's dashboard ViewModel.

---

## `CommunityToolkit.Maui` (used narrowly, for `FileSaver`)

### What problem does it solve?
MAUI's built-in APIs cover sharing a file to another app (`Share.RequestAsync`)
but not "let the user pick a folder and save a file there directly." Phase 09's
backup export wants the second option as an optional, secondary path alongside the
share sheet.

### Why are we using it?
It's the community-standard companion package for exactly this kind of MAUI gap,
actively maintained (v15.0.0 as of July 2026), and importantly, this project's
actual usage is narrow: just `FileSaver`, not the whole toolkit's grab-bag of
behaviors/converters/popups.

### Alternatives

1. **`Share.Default.RequestAsync`ONLY** (built into MAUI Essentials, no extra
   package)
   - Pros: zero dependency, already Phase 09's primary export path (share sheet →
     Drive/Gmail/Nearby Share).
   - Cons: hands the file off to another app; doesn't give a direct "save to a
     folder I pick on this device" flow.
   - Learning curve: none, already in use.
   - When better: as the *primary* mechanism, which is already the plan. The
     question here is only whether a secondary path is worth a dependency.
2. **`CommunityToolkit.Maui`'s `FileSaver`** (the actual choice, as a secondary
   button)
   - Pros: genuine "Save As" for users who want an on-device file, not a
     hand-off; actively maintained.
   - Cons: documented history of permission failures on API 33+ and unresolvable
     `content://` paths from some cloud-picked destinations. A real,
     already-acknowledged weakness, which is exactly why Phase 09's docs already
     demote it to secondary/optional rather than the primary path.
   - Learning curve: low, one method call.
   - When better: exactly as scoped, optional but not depended on.
3. **Hand-rolled Android SAF intent** (`ACTION_CREATE_DOCUMENT` directly)
   - Pros: no dependency, single-platform app so no cross-platform abstraction
     wasted.
   - Cons: doesn't remove the underlying Android storage-permission mess.
     `FileSaver`'s bugs *are* Android SAF's quirks, not something the package
     introduced. Writing it yourself means owning that workaround code personally
     for a secondary feature.
   - Learning curve: moderate (raw Android intents from platform code).
   - When better: only if `FileSaver`'s specific bugs actually block a real use
     case. Worth revisiting then, not preemptively.

### Why not the alternatives?
`Share`-only rejected as the *sole* mechanism because "save a file to a folder I
choose" is a real, if secondary, backup use case. Hand-rolled SAF rejected because
it wouldn't actually dodge the underlying platform mess. It would just mean
maintaining that workaround personally, for a feature that's explicitly optional.

### Long-term considerations
Narrow usage (one call site) means low blast radius if the package were ever
abandoned, cheap to replace later, unlike a project leaning on the toolkit's
behaviors/converters/popups throughout. Actively maintained mid-2026. Standard
enough to appear in most MAUI file-handling tutorials.

### Practical example
Phase 09's `BackupPage`, "Save to device" secondary button next to the primary
share-sheet export.

---

## LiveCharts2: the one worth re-checking

### What problem does it solve?
Phase 10 needs two chart shapes: cross-workout trend lines (distance/duration/
speed over time) and a per-workout speed+heart-rate curve over a single workout's
samples, with visible gaps where the connection dropped (a `null` sample, not an
interpolated line).

### Why are we using it (as originally planned)?
MIT-licensed, SkiaSharp-rendered, targets MAUI directly with native-feeling
controls, and supports the dual-axis + gap-as-null rendering Phase 10's
speed/heart-rate overlay needs, out of the box.

### ⚠ What changed since the original plan
Checking current status for this response surfaced something `00-Project-Plan.md`
didn't account for: **LiveCharts2 has been in beta/RC status for multiple years
without reaching a stable 1.0 release**, and 2026 community discussions show real
maintenance-concern reports, including users who've moved to other libraries. For
a project maintained by one person over years, "the library might never reach 1.0,
or might ship a breaking change with no real deprecation runway" is a legitimate
risk this app's risk register (`00-Project-Plan.md`) should actually carry. It
currently doesn't mention this at all.

### Alternatives

1. **Microcharts** (+ `Microcharts.Maui`)
   - Pros: SkiaSharp-based like LiveCharts2, genuinely simple (bar/line/point/
     radar/donut/gauge), small surface matching this app's actual needs
     reasonably well, easiest to learn of the three.
   - Cons: no built-in dual-Y-axis support. The speed/heart-rate overlay would
     need a workaround (normalize both to 0-1 and lose real axis labels, or split
     into two stacked charts instead of one overlay). Less flexible gap-in-line
     support.
   - Learning curve: lowest.
   - When better: if the heart-rate overlay requirement goes away. Worth
     revisiting once Phase 00's HR-usability finding (V3) is confirmed. If HR
     gets cut from the dashboard entirely (already a live possibility per the
     Phase 00 findings), Microcharts' single-axis simplicity stops being a
     limitation and becomes the obviously simpler choice.
2. **OxyPlot** (+ `oxyplot-maui`)
   - Pros: broadest .NET platform coverage of any free chart library, MIT,
     no commercial restrictions, **longer track record than LiveCharts2** (predates
     it by years, without the "still not 1.0" issue), supports dual axes and
     gap rendering.
   - Cons: MAUI bindings are a thinner, less MAUI-native integration than
     LiveCharts2's purpose-built MAUI controls. Expect more manual styling to
     match the monochrome theme; the API reads more "scientific plotting library"
     than "mobile dashboard," so idioms feel less native to a phone screen.
   - Learning curve: moderate, more configuration surface than Microcharts, a
     different API shape than LiveCharts2.
   - When better: exactly the situation here if the dual-axis requirement stays.
     Trades LiveCharts2's prettier defaults for a library with an actual stable
     track record.
3. **Hand-rolled SkiaSharp canvas**
   - Pros: total control, no charting-library dependency. The actual
     requirement here (a scrollable line, a few dozen to a couple thousand
     downsampled points, no zoom/pan/legend interactivity needed) is simple
     enough that this isn't as extreme as it sounds. `SkiaSharp` itself is what
     all three library options render through anyway.
   - Cons: real code to write and own: axis ticks, label layout, the
     downsampling logic Phase 10 already needs regardless. Realistically
     200-400 lines for what's needed here, versus near-zero with a library.
   - Learning curve: steepest (raw `SKCanvas` drawing), but the most transferable
     skill and the smallest, most inspectable dependency footprint.
   - When better: if the maintenance-risk concern above is a genuine dealbreaker
     and the chart requirements stay this narrow.

### Why not the alternatives (tentative: this is the one real open decision)
This isn't a closed case the way the others are. **Microcharts** is the strongest
alternative *if* the heart-rate overlay gets cut. Worth deciding after Phase 00's
V3 finding is confirmed, not before. **OxyPlot** is the safer swap if dual-axis
stays a requirement. More manual styling work, but a materially safer long-term
bet than a library that hasn't reached 1.0 in years. Hand-rolled SkiaSharp wasn't
the starting recommendation only because it's more total first-pass code. It's
the fallback if maintenance risk turns out to matter more than development speed.

### Long-term considerations
Not a standard/stable technology by its own project's admission. Pre-1.0 for
years is a real signal, not pedantry. Community support exists but with visible
2026 maintenance-concern discussion. Performance is fine (SkiaSharp-backed, same
rendering approach as every alternative here). **The one genuine mitigating
factor:** chart configuration is isolated to Phase 10's ViewModel/code-behind, not
spread through the app. A future swap wouldn't ripple into other phases, which
de-risks starting with LiveCharts2 now and revisiting later if it doesn't pan out.

### Practical example
Phase 10's cross-workout trend chart and per-workout speed/HR curve.

**Recommendation: don't block on this today.** Phase 10 is late in the plan.
Phases 02-09 ship first, which buys months to watch LiveCharts2's trajectory. But
this is the one dependency in the current stack that doesn't yet clearly satisfy
"every technology must justify its existence," and it's worth re-running this
comparison (specifically: has LiveCharts2 reached 1.0? have the 2026 maintenance
concerns resolved or worsened?) when Phase 10 actually starts, rather than
assuming the original plan's choice still holds.

---

## MAUI Shell (navigation)

### What problem does it solve?
The app needs to move between screens (scan → dashboard → diagnostics → settings
→ history → backup) with back-stack behavior, and construct each page with its
ViewModel via DI rather than `new PageX()` scattered across the codebase.

### Why are we using it?
It's not a third-party dependency. It ships as part of the MAUI SDK itself, is
Microsoft's own default-recommended navigation system, is already wired up
(`AppShell.xaml`, `Routing.RegisterRoute` from Phase 00), and this app's
navigation shape (a fairly flat set of screens, no deep multi-level flows) is
exactly Shell's sweet spot.

### Alternatives

1. **Plain `NavigationPage` + manual `PushAsync`/`PopAsync`** (the pre-Shell way)
   - Pros: simpler mental model for a small app: no route strings, no
     `Routing.RegisterRoute` indirection, more directly traceable ("this button
     pushes this page").
   - Cons: no built-in flyout/tabs without extra wiring; DI-construction of pages
     needs its own plumbing either way; loses Shell's URI-style navigation, which
     could matter if a future feature (e.g. a notification tap) needs to jump
     straight to a specific screen.
   - Learning curve: lower than Shell.
   - When better: a very small app (2-4 screens) with no future flyout/tab
     needs. Arguably close to this app's current size, worth naming honestly
     rather than assuming Shell was obviously correct.
2. **Hand-rolled navigation service** (custom `INavigationService` wrapping page
   construction + a stack)
   - Pros: complete control, easy to unit-test in isolation.
   - Cons: Shell already *is* this, maintained by the MAUI team, DI-integrated
     out of the box. Re-solves a problem the SDK ships a solution for, for no
     clear benefit here.
   - Learning curve: moderate to build, though conceptually simple.
   - When better: only if Shell's route-string model becomes a genuine obstacle.
     Hasn't happened.
3. **`TabbedPage`/`FlyoutPage` without Shell**
   - Pros: none of real substance. These are the older building blocks Shell was
     built to unify.
   - Cons: more manual wiring than Shell for equivalent behavior.
   - Learning curve: comparable to Shell, worse ergonomics.
   - When better: a legacy Xamarin.Forms codebase already structured this way,
     not a fresh MAUI project.

### Why not the alternatives?
Plain `NavigationPage` is the only genuinely competitive option, and it was
implicitly decided already. Phase 00 built `AppShell.xaml` with route
registration, and Shell's overhead over `NavigationPage` for an app this size is
small enough that reverting now for marginal simplicity isn't worth undoing
working code. Hand-rolled and pre-Shell `TabbedPage`/`FlyoutPage` rejected because
they reimplement what Shell already provides for free, as part of the SDK, with
zero independent-abandonment risk (it can't be "abandoned" separately from MAUI
itself).

### Long-term considerations
About as safe as a choice gets. Not a dependency, part of the MAUI SDK, tracking
.NET/MAUI's own release cadence exactly. No independent community-support risk
beyond "is MAUI itself still developed" (yes). Performance is a non-issue at this
app's screen count. Not "replaceable" in the swap-a-library sense. A future
change here would be a MAUI-version concern, not a dependency-choice one.

### Practical example
`AppShell.xaml`; Phase 03 making the dashboard the new Shell landing page; Phase
08/09's settings/backup routes.
