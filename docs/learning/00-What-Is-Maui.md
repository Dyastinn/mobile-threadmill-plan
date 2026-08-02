# What is .NET MAUI?

> Written for: you know C# already (if you don't yet, but know programming in
> general, just not C# specifically: read
> [`00a-CSharp-Essentials.md`](00a-CSharp-Essentials.md) first, it's short and
> exactly closes that gap), but have never touched MAUI, XAML, or mobile app
> patterns like MVVM. This doc uses real files from `src/MyHi.Companion/` as
> examples. Open them side by side as you read.

---

## The one-sentence version

**.NET MAUI (Multi-platform App UI) is Microsoft's framework for writing one C#/XAML
codebase that compiles to native apps on Android, iOS, macOS, and Windows.** This
project only targets Android (see `MyHi.Companion.csproj`'s single
`<TargetFramework>net10.0-android</TargetFramework>` line), so in practice you can
think of MAUI here as just "the UI framework," without worrying about the
multi-platform part.

It's the successor to **Xamarin.Forms** (same idea, same company, newer and better
integrated into plain .NET; you don't need to know Xamarin, it's just useful context
if you see it mentioned in older tutorials/StackOverflow answers).

MAUI is **not** a wrapper around a web view (like React Native or Ionic can be, in
some setups). Every button, list, and layout you write compiles down to a real native
Android `View`. When you eventually poke around `Platforms/Android/`, you're seeing
the thin native shell MAUI needs per-platform; almost everything else you write is
shared.

---

## XAML: UI described as markup, not built line-by-line in code

Open `Features/Shared/HomePage.xaml`. XAML (pronounced "zammel") is an XML dialect for
describing UI declaratively:

```xml
<ContentPage ...>
    <Grid Padding="16" RowDefinitions="Auto,*">
        <Label Grid.Row="0" Text="Every screen here is a lab instrument..." />
        <CollectionView Grid.Row="1" ItemsSource="{Binding Destinations}">
            ...
        </CollectionView>
    </Grid>
</ContentPage>
```

You *could* build this same UI by writing `new Grid { ... }` in C#, but XAML reads
better for layout-heavy UI, and MAUI compiles it to real object construction at build
time (via a "XAML compiler," `XamlC`), so there's no meaningful runtime cost to using
it. Every `.xaml` file in this project has a matching `.xaml.cs` "code-behind" file
(`HomePage.xaml.cs`) for the C# that isn't pure layout, usually just the
constructor.

**Layout controls worth knowing** (all used already in `src/`):
- `Grid`: rows and columns, like a table. `RowDefinitions="Auto,*"` means "first row
  sized to its content, second row takes all remaining space."
- `VerticalStackLayout` / `HorizontalStackLayout`: simple stacking, like flexbox
  with one axis.
- `ScrollView`: makes its single child scrollable (see `ControlConsolePage.xaml`).
- `CollectionView`: a virtualized, scrollable list bound to a collection (see
  `ScanPage.xaml`'s device list). It's the MAUI equivalent of Android's `RecyclerView` or
  a web `<ul>` with a loop.

---

## Data binding: `{Binding PropertyName}`

This is the part that feels like magic until it clicks. In `HomePage.xaml`:

```xml
<Label Text="{Binding StatusMessage}" />
```

`{Binding X}` means: "look at whatever object is set as this page's `BindingContext`,
read its `X` property, and put the value here." When `X` changes, the label updates
itself. **You never write `label.Text = "..."` by hand.**

The "whatever object" is set in the code-behind constructor:

```csharp
// HomePage.xaml.cs
public HomePage(HomeViewModel viewModel)
{
    InitializeComponent();
    BindingContext = viewModel;   // <-- this line is why {Binding} works
}
```

That `viewModel` is a **ViewModel**; see the next section.

---

## MVVM: Model, View, ViewModel

This is the dominant UI architecture pattern in MAUI (and WPF, and Xamarin before
it). The point is to keep UI code (XAML + code-behind) completely free of business
logic, so the logic can be tested without spinning up a UI.

| Layer | What it is in this project | Example |
|-------|----------------------------|---------|
| **Model** | Plain data. Often just a `record`. | `NavDestination`, `DiscoveredDevice` |
| **View** | The `.xaml` + `.xaml.cs` pair. Only layout + wiring `BindingContext`. | `HomePage.xaml` |
| **ViewModel** | The class the View binds to. Holds state, exposes commands, talks to services. | `HomeViewModel.cs` |

A ViewModel never references a `Page` or a `Button` directly. It exposes properties
and commands, and the View decides how to display them. This is what makes
`MyHi.Companion.Core` possible: it's pure logic with zero MAUI references, fully
testable with plain xUnit (see `MyHi.Companion.Tests`).

### `[ObservableProperty]` and `[RelayCommand]`: what they actually generate

Open `HomeViewModel.cs`:

```csharp
public sealed partial class HomeViewModel : BaseViewModel
{
    [ObservableProperty]
    private NavDestination? selectedDestination;

    [RelayCommand]
    private static async Task NavigateAsync(string route) => await Shell.Current.GoToAsync(route);
}
```

These attributes come from the `CommunityToolkit.Mvvm` NuGet package and are
**source generators**: code that runs at compile time and writes extra C# into your
class, in a hidden generated file you never edit. Concretely:

- `[ObservableProperty] private NavDestination? selectedDestination;` generates a
  public `SelectedDestination` property with a getter/setter that raises
  `PropertyChanged`, the event that makes `{Binding SelectedDestination}` actually
  update the UI when the value changes. Without the attribute, you'd hand-write:
  ```csharp
  private NavDestination? _selectedDestination;
  public NavDestination? SelectedDestination
  {
      get => _selectedDestination;
      set { _selectedDestination = value; OnPropertyChanged(); }
  }
  ```
  every single time. The attribute exists purely to stop you writing that
  boilerplate by hand, dozens of times per ViewModel.

- `[RelayCommand] private static async Task NavigateAsync(string route) => ...`
  generates a public `ICommand NavigateCommand` property. `ICommand` is what
  `Button.Command="{Binding SomeCommand}"` or a `CollectionView`'s `SelectedItem`
  handler (see `partial void OnSelectedDestinationChanged`) expects. It's the
  standard .NET interface for "a thing that can be invoked, and knows if it currently
  can be." The naming convention is fixed: a method named `NavigateAsync` (or just
  `Navigate`) produces a command property named `NavigateCommand`.

- The class must be declared `partial`; that's the syntax that lets the source
  generator's separate file contribute members to the *same* class.

Every ViewModel in this project inherits from `BaseViewModel`
(`Features/Shared/BaseViewModel.cs`), which itself just has two `[ObservableProperty]`
fields (`IsBusy`, `StatusMessage`) every screen needs.

---

## Dependency Injection: why `MauiProgram.cs` looks the way it does

Open `MauiProgram.cs`. Near the bottom:

```csharp
builder.Services.AddSingleton<TreadmillConnection>();
builder.Services.AddTransient<ScanViewModel>();
builder.Services.AddTransient<ScanPage>();
```

This is **dependency injection (DI)**: instead of a class constructing the things it
depends on itself (`new TreadmillConnection()`), it declares what it needs as
constructor parameters, and a central container (`IServiceCollection`/`IServiceProvider`)
is responsible for constructing and supplying them:

```csharp
// ScanViewModel.cs
public ScanViewModel(BleScanner scanner, BluetoothReadinessService readiness, ...)
{
    _scanner = scanner;
    ...
}
```

When Shell needs a `ScanPage`, the container sees `ScanPage`'s constructor wants a
`ScanViewModel`, sees *that* constructor wants a `BleScanner`, a
`BluetoothReadinessService`, etc., and builds the whole chain automatically. You
never call `new ScanViewModel(...)` anywhere.

**`AddSingleton` vs. `AddTransient`**: the two you'll actually use in this project:
- `AddSingleton<T>()`: one instance for the entire app's lifetime, shared everywhere.
  Used for things that represent real, ongoing state: `TreadmillConnection` (there's
  only one BLE connection), `CaptureSessionManager` (one capture session at a time).
- `AddTransient<T>()`: a new instance every time one is requested. Used for
  ViewModels and Pages; you don't want the Scan screen's state to leak into a second
  visit to the Scan screen.

---

## Shell and navigation

`AppShell.xaml` + `AppShell.xaml.cs` define the app's navigation structure. This
project uses one `ShellContent` (the Home page) plus a set of routes registered in
code:

```csharp
// AppShell.xaml.cs
Routing.RegisterRoute("scan", typeof(ScanPage));
```

Navigating is then just:
```csharp
await Shell.Current.GoToAsync("scan");
```
Shell handles the back stack, the platform back button, and constructing the target
page (with DI) for you.

---

## The project's two-project split, and why

`src/` has three projects:

- **`MyHi.Companion.Core`**: plain `net10.0` class library. Zero MAUI references.
  Hex helpers, FTMS command encoding, the capture file format, SQLite plumbing.
- **`MyHi.Companion`**: the actual MAUI Android app (`net10.0-android`). XAML,
  ViewModels, BLE plumbing (Plugin.BLE only makes sense on a real platform).
- **`MyHi.Companion.Tests`**: xUnit tests, referencing only `Core`.

The reason for the split: **you cannot run a normal xUnit test suite against a
`net10.0-android` project** without an emulator or device; there's no desktop
runtime for it. Anything you want unit-tested (parsers, encoders, pure logic) has to
live somewhere a plain `dotnet test` can reach, which means `Core`. This is also just
good MVVM discipline: if a piece of logic is hard to put in `Core` because it "needs"
a `Page` or a MAUI type, that's usually a sign it's mixing UI and logic and could be
split further.

---

## Where to go next

- `docs/learning/01-Emulator-Setup.md` if you want to smoke-test UI without your
  phone (with the caveat that it can't test real Bluetooth).
- `docs/learning/02-Glossary.md` any time you hit a term mid-phase you don't
  recognize; add to it as we go.
- `phases/README.md` for how phases work now that you're writing the code.
