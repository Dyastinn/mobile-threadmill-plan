# Dart and Flutter essentials

> Written for: you already know C# (from the original MAUI-track docs at
> `../../../docs/learning/`), but have never touched Dart or Flutter. Where a
> Dart/Flutter concept has a close C# equivalent, this doc says so directly
> instead of re-teaching the idea from zero — you already know what a
> null-safe type or an `async` method is *for*, you just need the Dart
> spelling. Open real files from `flutter/packages/myhi_companion_core/lib/`
> alongside this as they exist.

---

## The one-sentence version

**Flutter is Google's UI toolkit for building natively-compiled apps from one
Dart codebase.** Like MAUI, it is **not** a WebView wrapper — every widget you
write compiles down to real drawing commands on Android's canvas via Flutter's
own rendering engine (Skia/Impeller), not a browser. This project targets
Android only, same as the original MAUI plan, so "cross-platform" is not a
concept you need to think about day-to-day here either.

**Dart** is the language: C-family syntax, sound null safety, `async`/`await`
that work almost identically to C#'s (Dart borrowed the keywords, and the
mental model transfers directly — an `await`ed `Future<T>` behaves like an
`await`ed `Task<T>`).

---

## Widgets: everything is a widget, described in Dart, not markup

MAUI separates layout (XAML) from logic (code-behind, `.xaml.cs`). Flutter
doesn't: layout is Dart code too. Where XAML declared a `Grid` with
`RowDefinitions`, Flutter nests widget constructors:

```dart
class DashboardCard extends StatelessWidget {
  const DashboardCard({super.key, required this.value, required this.unit});

  final String value;
  final String unit;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(value, style: Theme.of(context).textTheme.headlineMedium),
            Text(unit, style: Theme.of(context).textTheme.bodySmall),
          ],
        ),
      ),
    );
  }
}
```

No separate markup file, no `x:DataType`, no `{Binding}` syntax. A widget's
`build` method just returns whatever the current state says it should return;
Flutter re-runs it when that state changes and diffs the result efficiently
(this is the "widgets are cheap, immutable descriptions" model — a new
`DashboardCard` instance is built on every rebuild, but the underlying render
objects are reused, not recreated).

**Layout widgets worth knowing** (rough MAUI equivalents in parentheses):

- `Column` / `Row` (`VerticalStackLayout` / `HorizontalStackLayout`): stack
  children along one axis.
- `Grid` and its own `Grid` widget class (MAUI's `Grid`): rows and columns.
  Less central in Flutter than in MAUI — `Column`/`Row`/`Expanded` cover most
  layout that would reach for a `Grid` in XAML.
- `SingleChildScrollView` (`ScrollView`): makes its child scrollable.
- `ListView.builder` / `GridView.builder` (`CollectionView`): virtualized,
  scrollable, built from a data source. `.builder` constructors only build
  the items currently visible, same reason `CollectionView` exists rather
  than materializing every row up front.

## `StatelessWidget` vs `StatefulWidget`: the closest thing to "does this need a ViewModel"

A `StatelessWidget` has no mutable state of its own — it's a pure function of
its constructor parameters, closest to a MAUI `DataTemplate` bound to an
already-existing ViewModel property. A `StatefulWidget` pairs with a `State`
object that can call `setState(() {...})` to trigger a rebuild — this is
Flutter's *lowest-level* state primitive, and this project does not use it
directly for anything beyond trivial local UI state (a text field's focus,
an animation controller). Anything resembling "ViewModel state" — treadmill
samples, connection status, workout state — goes through **Riverpod**
instead, described next, for the same reason the original project didn't
hand-roll `INotifyPropertyChanged`: mechanical, repeated boilerplate with a
silent failure mode if done by hand.

## Riverpod: this project's MVVM equivalent

Riverpod is a state-management and dependency-injection library. It replaces
two things the MAUI track used separately: `CommunityToolkit.Mvvm`'s
`[ObservableProperty]`/`[RelayCommand]`, and MAUI's built-in DI container
(`builder.Services.AddSingleton<T>()`).

A `Provider` is roughly a DI registration. A `Notifier` (or `StateNotifier`)
is roughly a ViewModel: it holds state and exposes methods that change it.

```dart
// Roughly: builder.Services.AddSingleton<ITreadmillService, FakeTreadmillService>();
final treadmillServiceProvider = Provider<TreadmillService>((ref) {
  return FakeTreadmillService();
});

// Roughly: a ViewModel with an [ObservableProperty] and a method that
// updates it, minus the source-generator attributes — Riverpod's own
// codegen is opt-in (this project skips it, see flutter/README.md) so this
// is hand-written, which is genuinely more visible code than
// [ObservableProperty], not less.
class DashboardState {
  const DashboardState({this.speedKph, this.connectionState = ConnectionState.disconnected});
  final double? speedKph;
  final ConnectionState connectionState;

  DashboardState copyWith({double? speedKph, ConnectionState? connectionState}) =>
      DashboardState(
        speedKph: speedKph ?? this.speedKph,
        connectionState: connectionState ?? this.connectionState,
      );
}

class DashboardNotifier extends Notifier<DashboardState> {
  @override
  DashboardState build() {
    final treadmill = ref.watch(treadmillServiceProvider);
    treadmill.samples.listen((sample) {
      state = state.copyWith(speedKph: sample.speedKph);
    });
    return const DashboardState();
  }
}

final dashboardProvider = NotifierProvider<DashboardNotifier, DashboardState>(
  DashboardNotifier.new,
);
```

A widget reads it with `ref.watch(dashboardProvider)` inside a
`ConsumerWidget`'s `build` method, and Flutter rebuilds exactly that widget
when the state it watched changes — the same "only redraw what actually
changed" outcome `{Binding}` + `PropertyChanged` gave you in XAML, via a
different mechanism (explicit `ref.watch` calls instead of implicit binding
resolution).

**Why this instead of `[ObservableProperty]`-style codegen** (`riverpod_generator`
exists and would give you closer-to-MAUI ergonomics): see the "Why plain
Riverpod" note in `../../README.md`. Short version: codegen means a
`build_runner` watcher process in the edit loop, and this project already
left MAUI specifically to reduce edit-loop friction on Linux. Plain
`Notifier` classes are slightly more typing, zero extra tooling.

## Streams: this project's C# events equivalent

`ITreadmillService`'s C# events (`event EventHandler<TreadmillSample>?
SampleReceived`) become a `Stream<TreadmillSample> get samples` in
`treadmill_service.dart`. Subscribing looks like:

```dart
final subscription = treadmill.samples.listen((sample) {
  // handle it
});

// later, matching C#'s -= to avoid a leak:
subscription.cancel();
```

A `Stream` is Dart's built-in asynchronous sequence type — closer to
`IAsyncEnumerable<T>` than to a C# `event` mechanically, but used here the
same way the original project used events: a service pushes values, zero or
more subscribers react. `StreamController.broadcast()` is what an
implementation (like `FakeTreadmillService`) uses internally to create the
stream it exposes.

## Records: this project's `record struct` equivalent

Dart 3 added record types: anonymous, structurally-typed, immutable value
types, exactly the properties that motivated `readonly record struct
TreadmillSample` in the original interface.

```dart
typedef TreadmillSample = ({DateTime timestampUtc, double? speedKph});

TreadmillSample sample = (timestampUtc: DateTime.now(), speedKph: 6.5);
print(sample.speedKph); // field access, like a C# record's auto-property
```

Where a type needs its own methods (like `SpeedRange.clamp` in
`treadmill_service.dart`), a small `final class` is used instead — Dart
records can't carry custom methods, only C#'s record *classes* can do both
at once. That's a real, if minor, expressiveness gap versus C#; the project's
seam file (`treadmill_service.dart`) picks records where pure data is enough
and a `final class` where behavior is attached, and says so in the file
itself.

## Null safety: same idea, similar spelling

Dart's sound null safety and C#'s nullable reference types solve the same
problem the same way: `double? speedKph` means exactly what `double?
SpeedKph` means in the interface — this field may be absent, check before
using. `late` is Dart's equivalent of a field you promise to initialize
before first use (closest to C#'s `required` keyword in spirit, though the
mechanics differ — `late` defers a null check to first access rather than
enforcing initialization at the constructor call site).

## `async`/`await`: nearly identical to C#

```dart
Future<ControlResult> setSpeed(double kph) async {
  final result = await _controlPoint.write(FtmsCommands.setTargetSpeed(kph));
  return result;
}
```

`Future<T>` is `Task<T>`. `async`/`await` are the same keywords doing the
same job. `Future.delayed(Duration(seconds: 1))` is `Task.Delay`.
`CancellationToken` has no exact Dart built-in equivalent — cancellation is
usually expressed by cancelling a `StreamSubscription`, completing a
`Completer` early, or checking a plain `bool` flag; this comes up concretely
once Phase 02 (connection hardening, not yet written for this track) builds
the reconnect backoff loop.

## `pubspec.yaml`: this project's `.csproj`/NuGet equivalent

```yaml
name: myhi_companion_core
environment:
  sdk: ^3.5.0
dependencies:
  # (none yet — Phase 00 hasn't run `flutter create` / `dart create` yet)
dev_dependencies:
  test: ^1.25.0
```

`flutter pub get` is `dotnet restore`. Adding a package is
`flutter pub add <package>`, which writes the `pubspec.yaml` entry for you —
closer to `dotnet add package` than to hand-editing a `.csproj`.

## Hot reload: why this project switched frameworks at all

`flutter run` keeps a running session; saving a file pushes the changed code
into the running app in under a second, preserving state (scroll position,
form input, the current screen) — this works identically on Linux, macOS,
and Windows, because it's implemented in the Flutter engine, not in an IDE.
See `../../README.md` and the technology decision record in
`../../phases/phase-00-probe-app/README.md` for why this specific property
is what actually drove the framework switch.

## Where to go next

- [dart.dev language tour](https://dart.dev/language) if any Dart syntax
  above didn't fully click.
- [flutter.dev's widget catalog](https://docs.flutter.dev/ui/widgets) once
  Phase 00's screens start needing widgets not covered above.
- [riverpod.dev](https://riverpod.dev) before writing your first
  `Notifier` in Phase 00 task 0.3.
- `../../phases/phase-00-probe-app/README.md` for what actually gets built
  first.
