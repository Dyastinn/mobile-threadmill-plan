# Glossary

> Growing list. Add a term here the first time it comes up in a phase, so it's never
> just "explained once in chat and gone."

### General .NET / C#

- **`record`**: a C# type built for immutable data holders with free structural
  equality (`a == b` compares values, not references) and a free `ToString()`.
  Used everywhere for models (`NavDestination`, `DiscoveredDevice`). Prefer a
  `record` over a `class` when a type is "just data."
- **`async` / `await` / `Task`**: .NET's way of writing non-blocking code. A method
  returning `Task` (or `Task<T>`) can be `await`ed by its caller, which frees the
  calling thread while the work happens (e.g. a BLE read, a file write) instead of
  blocking it. On UI threads, blocking instead of awaiting freezes the whole app.
- **Dependency Injection (DI) / `IServiceCollection` / `IServiceProvider`**: a
  pattern where a class declares what it needs via constructor parameters instead of
  constructing them itself, and a central container supplies them. See
  `00-What-Is-Maui.md`'s DI section.
- **`IDisposable` / `using`**: the interface for "this object holds a resource
  (file handle, connection) that must be explicitly released." `using var x = ...`
  guarantees `x.Dispose()` runs even if an exception is thrown.

### MAUI / XAML

- **XAML**: the XML-based markup language for describing UI declaratively. See
  `00-What-Is-Maui.md`.
- **`ContentPage`**: the base class for a single full screen (e.g. `HomePage`,
  `ScanPage`).
- **`Shell` / `AppShell`**: MAUI's navigation host. Owns the back stack and
  platform back-button behaviour.
- **`Routing.RegisterRoute("name", typeof(SomePage))`**: registers a string route
  so `Shell.Current.GoToAsync("name")` knows what page to construct (via DI).
- **`BindingContext`**: the object a page/view's `{Binding ...}` expressions
  resolve against. Usually a ViewModel, set once in the page's constructor.
- **`x:DataType`**: tells the XAML compiler the *type* of the current
  `BindingContext` (or, inside a `DataTemplate`, the type of each list item), so
  bindings are checked and compiled at build time instead of resolved by string
  lookup at runtime. Always set this. It turns typos in binding paths into build
  errors instead of silent runtime failures.
- **`IValueConverter`**: a small class that transforms a bound value for display,
  e.g. `bool` → `"Yes"/"No"` (see `Features/Shared/Converters.cs`). Registered as a
  resource (`App.xaml`) and referenced via `{StaticResource SomeConverter}`.
- **`CollectionView`**: a virtualized, scrollable, data-bound list. On Android it's
  backed by a native `RecyclerView`.
- **`DataTemplate`**: the per-item layout a `CollectionView`/`BindableLayout` uses
  to render each element of its `ItemsSource`.
- **Gesture recognizers inside `CollectionView` items are unreliable on Android.**
  A `TapGestureRecognizer` nested in a `DataTemplate` can get its touch events eaten
  by the list's own recycling/selection handling. Prefer
  `SelectionMode="Single"` + `SelectedItem="{Binding ...}"` for "tap a row to do
  something" instead. (Learned the hard way on the Home screen, see
  `HomeViewModel.cs`'s `OnSelectedDestinationChanged`.)

### MVVM / CommunityToolkit.Mvvm

- **MVVM (Model-View-ViewModel)**: the architecture pattern separating UI (View)
  from state/logic (ViewModel) from plain data (Model). See `00-What-Is-Maui.md`.
- **`ObservableObject`**: the base class (from `CommunityToolkit.Mvvm`) that
  implements `INotifyPropertyChanged`, the interface `{Binding}` relies on to know a
  property changed.
- **`[ObservableProperty]`**: attribute on a private field that generates a public
  property with change notification. See `00-What-Is-Maui.md` for exactly what it
  generates.
- **`[RelayCommand]`**: attribute on a method that generates a public `ICommand`
  property (named `<MethodName minus "Async">Command`), so it can be bound to a
  `Button.Command` or similar. Works on both sync and `async Task` methods.
- **`ICommand`**: the standard .NET interface for "an invokable action that can
  report whether it's currently allowed to run." What `Button.Command="{Binding X}"`
  expects on the other end of the binding.
- **`partial` class/method**: required for source-generator attributes
  (`[ObservableProperty]`, `[RelayCommand]`) to add members to your class from a
  separate generated file. `partial void OnXChanged(T value)` is a generator-provided
  hook that runs automatically right after `X`'s setter runs.

### Bluetooth / Plugin.BLE

- **GATT** — Generic Attribute Profile, the data model BLE devices expose:
  services, each containing characteristics, each optionally having descriptors.
- **Service / Characteristic / Descriptor**: see `05-FTMS-Protocol.md` §0 for the
  read/write/notify/indicate operations available on each.
- **`IDevice` / `IService` / `ICharacteristic`**: Plugin.BLE's abstractions over a
  platform's native BLE APIs. `IDevice.NativeDevice` gets you the underlying
  `Android.Bluetooth.BluetoothDevice` when you need something Plugin.BLE doesn't
  expose (MAC address, address type; see `BleScanner.cs`).
- **Indication vs. Notification**: both are "the device pushes data without being
  asked"; an indication is acknowledged by the receiver, a notification isn't. The
  control point (`0x2AD9`) uses indications specifically so the app can be sure a
  command was received.

### This project's architecture

- **`Core` vs. the app project**: `MyHi.Companion.Core` is a plain `net10.0`
  library with zero MAUI references, holding anything that should be unit-testable
  without an Android target. See `00-What-Is-Maui.md`.
- **Seam / Fake pattern** — an interface (e.g. `ITreadmillService`) with a real
  implementation and a `Fake...` implementation that simulates realistic behaviour.
  Lets you build and test everything above the interface without the real hardware
  in the loop. Used for `ITreadmillService`/`FakeTreadmillService` (Phase 01b) and
  `IWorkoutHistoryProvider`/`FakeWorkoutHistoryProvider` (Phase 03).
