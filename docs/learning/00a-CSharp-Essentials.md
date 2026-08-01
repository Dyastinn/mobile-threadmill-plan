# C# essentials, for someone who already programs

> Written for: you're comfortable with general programming — variables, functions,
> loops, probably classes — in some other language, but C#/.NET itself is new.
> This is not a full C# tutorial. It's the specific vocabulary this project uses
> constantly, explained once, so Phase 01b doesn't stop you cold on syntax instead
> of the actual BLE/state-machine concepts it's trying to teach.
>
> Every example below is real code from this repo — `ITreadmillService.cs` (repo
> root) and `TreadmillConnection.cs`
> (`src/MyHi.Companion/Features/Bluetooth/`) — open them side by side.
>
> Read this before [`00-What-Is-Maui.md`](00-What-Is-Maui.md), which assumes
> everything here.

---

## Static typing, and `var`

C# is statically typed: every variable's type is fixed at compile time, and the
compiler checks it — unlike Python or JavaScript, where a variable can hold
anything and mistakes show up at runtime instead.

```csharp
double kph = 6.5;          // explicit type
var kph = 6.5;              // same thing — the compiler infers `double` from the
                             // right-hand side. `var` is not "any type" (that's
                             // not really a thing in C#); it's just "don't make me
                             // type the type twice."
```

Use `var` when the type is obvious from the right side (`var list = new List<int>();`);
use the explicit type when it isn't (a method return value where the name doesn't
make the type obvious).

---

## Classes vs. `record` — the distinction this codebase leans on hard

A `class` is the classic OOP thing you already know: a blueprint for objects with
identity (two instances with identical data are still *different objects* unless
you write your own equality logic).

A `record` is a C# feature for **data that's compared by value**: two records with
the same field values are `==` to each other automatically, and you get a free,
readable `ToString()`. This project uses records constantly for anything that's
"just a bag of data flowing through the system," which is most of it:

```csharp
// ITreadmillService.cs
public sealed record MachineEvent(MachineEventKind Kind, double? Value = null);
```

This one line generates: a constructor, two read-only properties (`Kind`,
`Value`), value-based `Equals`/`GetHashCode`, and `ToString()`. Writing the
equivalent by hand as a `class` would be 15-20 lines you'd have to get right and
then never look at again — which is exactly the kind of boilerplate a language
feature should absorb.

**`readonly record struct`** — one step further, seen here:

```csharp
public readonly record struct TreadmillSample
{
    public DateTimeOffset TimestampUtc { get; init; }
    public double? SpeedKph { get; init; }
    // ...
}
```

`struct` (vs `class`/`record`) means this value lives inline wherever it's used,
not as a separate heap object — no garbage-collector pressure. The comment right
above it in the real file explains why: `TreadmillSample` gets created roughly
once a second forever the app is connected, so avoiding a heap allocation on that
"hot path" actually matters. You don't need to deeply understand GC pressure to
work in this codebase — just recognize `struct` as "the lightweight one, used
because something creates a lot of these, fast."

**Rule of thumb for this project:** if you're modeling "a value that flows through
the system and gets compared/logged," reach for `record` (or `record struct` if
it's created very frequently). If you're modeling "a thing with behavior and
identity that changes over time" (a connection, a service), that's a `class`.
`TreadmillConnection` — the thing that owns a live Bluetooth link — is a `class`;
`TreadmillSample` — one reading from it — is a `record struct`.

---

## Properties: `{ get; set; }`, `{ get; init; }`, `{ get; private set; }`

A property looks like a field but is actually a pair of methods (getter/setter)
wearing field syntax. This matters because C# uses properties *everywhere* instead
of Java/Python-style `getX()`/`setX()` methods:

```csharp
public ConnectionState State { get; private set; }
```

- `get;` — anyone can read `connection.State`.
- `private set;` — only code *inside* the class can assign it
  (`State = ConnectionState.Ready;`). Outside code can look but not touch — this is
  how a class protects its own invariants without hiding the value entirely.
- `init;` (seen on `TreadmillSample`'s fields above) — can only be set during
  object construction, then it's frozen forever. Stricter than `private set`: not
  even the class itself can change it after creation. Used on records specifically
  because records are meant to be immutable snapshots.

---

## Nullable reference types: `?`, `?.`, `??`

C# lets you mark exactly which references are allowed to be `null`, and the
compiler warns you if you use one without checking:

```csharp
public double? SpeedKph { get; init; }        // this field CAN be absent
public TreadmillCapabilities? Capabilities { get; }  // this CAN be null (before discovery finishes)
```

The `?` after a type (`double?`, `TreadmillCapabilities?`) means "this might not
have a value." Without the `?`, the compiler treats absence as a bug you need to
prevent, not something to handle.

Why this project uses `?` on almost every `TreadmillSample` field: the real
comment in `ITreadmillService.cs` explains it directly — *"Field presence is
per-packet in FTMS, not per-device"* — meaning the treadmill might include speed
in one packet and omit it in the next, so "do we have a speed reading right now"
is a real, constantly-changing question, not a one-time device-capability check.

Two operators you'll use constantly when working with nullable values:

- **`?.`** (null-conditional) — `_gattGate?.Release();` means "call `.Release()`
  only if `_gattGate` isn't null; otherwise do nothing and don't throw."
- **`??`** (null-coalescing) — `capabilities ?? DefaultCapabilities` means "use
  `capabilities`, or this fallback if `capabilities` is null."

---

## Interfaces: the contract, not the implementation

```csharp
public interface ITreadmillService
{
    ConnectionState State { get; }
    Task ConnectAsync(string deviceId, CancellationToken ct = default);
    // ...
}
```

An interface lists *what* a type must be able to do, with zero implementation.
`FakeTreadmillService : ITreadmillService` (Phase 01b) and a future real
`TreadmillService : ITreadmillService` both promise to have a `State` property and
a `ConnectAsync` method — anything holding a reference typed as `ITreadmillService`
doesn't know or care which one it's actually talking to. This is *the* mechanism
behind this whole project's "fake service now, real one later, nothing else
changes" pattern — see `docs/learning/02-Glossary.md`'s "Seam / Fake pattern"
entry.

---

## `async`/`await`, `Task`, `CancellationToken`

Bluetooth operations (connect, write, wait for a response) take real time and
must not freeze the UI while waiting. C#'s answer is `async`/`await`:

```csharp
public async Task ConnectAsync(IDevice device, CancellationToken ct = default)
{
    await _adapter.ConnectToDeviceAsync(device, new ConnectParameters(autoConnect: false), ct);
    // execution "pauses" here without blocking the calling thread, and resumes
    // once the connect attempt finishes
}
```

- **`Task`** is C#'s "a value that will exist later" type — like a `Promise` in
  JavaScript or a `Future` in Python/Java. `Task<T>` is the same thing but the
  eventual result has a value of type `T` (e.g. `Task<ControlResult>`).
- **`await`** unwraps a `Task`, pausing this method (not the thread) until it
  completes. A method containing `await` must be marked `async` and its own return
  type wrapped in `Task`/`Task<T>`.
- **`CancellationToken ct = default`** — a standard, passed-everywhere way to say
  "let the caller cancel this operation partway through" (e.g. the user backs out
  of a connect attempt). `= default` means "optional — if the caller doesn't pass
  one, use a token that never cancels." You'll write `ct` as a parameter on nearly
  every async method in this codebase, and pass it straight through to whatever you
  `await` inside.

**The one rule that matters most in a UI app:** never *block* on async work (no
`.Result`, no `.Wait()`) — that freezes the screen. Always `await` it.

---

## Events and delegates: `event EventHandler<T>?`

```csharp
public event EventHandler<ConnectionState>? StateChanged;

private void SetState(ConnectionState state)
{
    State = state;
    StateChanged?.Invoke(this, state);   // "if anyone's listening, tell them"
}
```

An **event** is a broadcast mechanism: this class doesn't know or care who's
listening, it just announces "state changed" and anyone who subscribed gets
notified. Outside code subscribes with `+=`:

```csharp
connection.StateChanged += (sender, newState) => { /* react here */ };
```

and unsubscribes with `-=` (important — forgetting to unsubscribe is a classic
memory leak, since the publisher keeps a live reference to every subscriber).

`EventHandler<T>` is the standard .NET shape for an event: `(object? sender, T args) => void`.
The `?` after `EventHandler<ConnectionState>` means the event itself can have zero
subscribers, which is why `StateChanged?.Invoke(...)` uses `?.` — "only invoke if
at least one thing is listening."

`ITreadmillService.cs`'s own top-of-file comment explains *why* this project uses
plain events instead of a fancier reactive-streams library
(`IObservable<T>`/System.Reactive): three event streams doesn't justify the extra
dependency and learning curve. Worth reading as a small example of the "does this
complexity earn its keep" judgment this project tries to apply everywhere — see
[`02-Technology-Stack.md`](../../02-Technology-Stack.md) for more of that same
reasoning applied to bigger decisions.

---

## LINQ: `.Select()`, `.Where()`, `.All()`

LINQ (Language Integrated Query) is C#'s built-in way to transform collections
without hand-written loops — closely analogous to `.map()`/`.filter()` in
JavaScript or list comprehensions in Python:

```csharp
var charInfos = characteristics
    .Select(c => new GattCharacteristicInfo(c.Id, BleUuidHelpers.ToShortForm(c.Id), ...))
    .ToList();

if (Services.All(s => s.ShortUuid != "1826"))
{
    _logger.LogWarning("0x1826 Fitness Machine service was not found on this device");
}
```

- **`.Select(x => ...)`** — transform each element into something else (like `.map`).
- **`.Where(x => ...)`** — keep only elements matching a condition (like `.filter`).
- **`.All(x => ...)`** / **`.Any(x => ...)`** — "do all/any elements satisfy this?"
- **`c => new GattCharacteristicInfo(...)`** is a **lambda** — an inline anonymous
  function. `c` is the parameter name (whatever you call it), `=>` separates
  parameters from the body. Same concept as an arrow function in JS or a `lambda`
  in Python, just C# syntax.

---

## Generics: `List<T>`, `IReadOnlyList<T>`, `Dictionary<K,V>`

`<T>` means "this type works with any type, filled in later":

```csharp
public IReadOnlyList<GattServiceInfo> Services { get; private set; } = [];
```

`List<GattServiceInfo>` is a growable list specifically of `GattServiceInfo`
objects — not "a list of anything," which is what makes the compiler able to catch
`Services.Add("oops, a string")` as an error before you ever run the app.
`IReadOnlyList<T>` is the same idea but exposed as read-only to callers outside the
class — a common pattern in this codebase: hold a mutable `List<T>` privately,
expose it as `IReadOnlyList<T>` publicly so nobody outside can `.Add()` to your
internal state by accident.

`[]` at the end is **collection expression syntax** (a newer C# feature) — shorthand
for "start as an empty list."

---

## Pattern matching: `is`, `switch` expressions

```csharp
if (Device is { } device)
{
    // `device` is now a non-null local variable, usable inside this block
}
```

`x is { } y` means "if `x` is not null, bind it to a new non-null variable `y`."
This is C#'s answer to "check for null and use the value" in one step, instead of
a separate null-check followed by a cast/assignment.

`switch` **expressions** (not the classic `switch` **statement** with `case:`/`break;`
you may already know) show up for mapping enums to values concisely:

```csharp
var message = code switch
{
    FtmsResultCode.Success => "OK",
    FtmsResultCode.ControlNotPermitted => "The treadmill refused control.",
    FtmsResultCode.Timeout => "No response from the treadmill.",
    _ => "Unknown error"
};
```

Each `case => value` arm is evaluated top to bottom; `_` is the catch-all
("default"). This is an expression (produces a value), not a statement — you're
assigning the result directly to `message`.

---

## Attributes: `[ObservableProperty]`, `[RelayCommand]`

Square brackets before a member are an **attribute** — metadata attached to code
that some other tool (the compiler, a source generator, a testing framework) reads
and acts on. `[ObservableProperty]`/`[RelayCommand]` (from `CommunityToolkit.Mvvm`)
are covered in full in `00-What-Is-Maui.md`'s MVVM section — the only thing to
know here is that `[SomeAttribute]` is C#'s general mechanism for "annotate this
code so something else can process it," and `[Fact]`/`[Theory]` (xUnit test
attributes you'll write starting Phase 01b) are the exact same mechanism used for
a different purpose: telling the test runner "this method is a test."

---

## `enum`

```csharp
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Discovering,
    Ready
}
```

A named set of fixed possible values — same concept as an enum in most languages
you've likely used. C# enums are a real, checkable type (`ConnectionState.Ready`),
not just string/int constants, which is why `switch` over an enum can warn you at
compile time if you forgot a case.

---

## Where to go next

- [`00-What-Is-Maui.md`](00-What-Is-Maui.md) — MAUI/XAML/MVVM specifics, now that
  the C# underneath it isn't also new.
- [`02-Glossary.md`](02-Glossary.md) — add a term here the moment it comes up and
  isn't covered by this doc or `00-What-Is-Maui.md`. This primer is deliberately
  not exhaustive — it's what you need for Phase 01b specifically, not all of C#.
- `phases/phase-01-protocol-decode/README.md`, Track 01b — this is what all of the
  above was for. Start there next.
