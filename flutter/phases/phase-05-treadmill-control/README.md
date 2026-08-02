# Phase 05 — Treadmill Control (Flutter track)

> **This phase may not exist.** Phase 00 verdict V2 decides. If the control
> point doesn't honour commands, skip to Phase 06.

**Hardware:** required · **Size:** M · **Blocked by:** Phase 04 · **Gated on:** V2

## Goal

Change treadmill speed from the app, reliably — turning what Phase 00's
control console already proved by hand into product UI.

## The concept

`0x2ACC`'s "Speed Target Setting" bit is the same self-description already
caught lying elsewhere (it claims incline support on a machine with no
incline). So controls aren't enabled because the bit says so — they're
enabled only after the actual handshake succeeds: `requestControl()`
returning success, then a real `setSpeed()` returning success. Trust
behaviour, not the label.

Stop gets a confirmation dialog; +/− don't. A Bluetooth command over a radio
link is not the physical safety key, and Stop has no correctable undo the
way a wrong +/− tap does (one more tap fixes it). The dialog is this
project's only way to add friction for a specific, irreversible action —
there's no red to lean on in a monochrome theme.

## Control point reference

Identical wire protocol to the original track — see
[`../../../phases/phase-05-treadmill-control/README.md`](../../../phases/phase-05-treadmill-control/README.md)
for the full command table, response format, and result codes. Nothing here
changes with the framework; `treadmill_service.dart`'s `FtmsResultCode`
already encodes the same values.

## `TreadmillControlNotifier`

```dart
class TreadmillControlState {
  const TreadmillControlState({
    this.targetSpeedKph = 0,
    this.canControl = false,
    this.presetSpeeds = const [],
    this.statusMessage,
  });
  final double targetSpeedKph;
  final bool canControl;
  final List<double> presetSpeeds;
  final String? statusMessage;

  TreadmillControlState copyWith({...}) => TreadmillControlState(...);
}

class TreadmillControlNotifier extends Notifier<TreadmillControlState> {
  int _debounceGeneration = 0; // same cancel-via-generation pattern as Phase 02/04

  @override
  TreadmillControlState build() {
    final treadmill = ref.watch(treadmillServiceProvider);
    treadmill.connectionStateChanges.listen(_onConnectionStateChanged);
    treadmill.machineEvents.listen(_onMachineEvent);
    return const TreadmillControlState();
  }

  void increaseSpeed() {
    final range = ref.read(treadmillServiceProvider).speedRange!;
    state = state.copyWith(targetSpeedKph: range.clamp(state.targetSpeedKph + range.incrementKph));
    _debounceSend();
  }

  void decreaseSpeed() {
    final range = ref.read(treadmillServiceProvider).speedRange!;
    state = state.copyWith(targetSpeedKph: range.clamp(state.targetSpeedKph - range.incrementKph));
    _debounceSend();
  }

  void setPresetSpeed(double presetKph) {
    state = state.copyWith(targetSpeedKph: presetKph);
    _debounceSend();
  }

  Future<void> _debounceSend() async {
    final myGeneration = ++_debounceGeneration;
    await Future.delayed(const Duration(milliseconds: 300));
    if (myGeneration != _debounceGeneration) return; // superseded by a later tap
    final result = await ref.read(treadmillServiceProvider).setSpeed(state.targetSpeedKph);
    state = state.copyWith(statusMessage: _describeResult(result.code));
  }

  Future<void> stop() async {
    final result = await ref.read(treadmillServiceProvider).stop();
    state = state.copyWith(statusMessage: _describeResult(result.code));
  }

  Future<void> _evaluateControlAvailability() async {
    final result = await ref.read(treadmillServiceProvider).requestControl();
    state = state.copyWith(canControl: result.success, statusMessage: _describeResult(result.code));
  }

  void _onConnectionStateChanged(ConnectionStateChange change) {
    if (change.state == ConnectionState.ready) {
      final range = ref.read(treadmillServiceProvider).speedRange!;
      state = state.copyWith(presetSpeeds: _generatePresets(range));
      _evaluateControlAvailability();
    } else if (change.state == ConnectionState.disconnected) {
      state = state.copyWith(canControl: false);
    }
  }

  void _onMachineEvent(MachineEvent event) {
    if (event.kind == MachineEventKind.controlPermissionLost) {
      state = state.copyWith(canControl: false);
      _evaluateControlAvailability();
    }
  }

  static List<double> _generatePresets(SpeedRange range, {int count = 6}) {
    return List.generate(count, (i) {
      final raw = range.minKph + (range.maxKph - range.minKph) * (i + 1) / (count + 1);
      return range.clamp(raw);
    });
  }

  static String _describeResult(FtmsResultCode code) => switch (code) {
    FtmsResultCode.success => 'OK',
    FtmsResultCode.opCodeNotSupported => "This treadmill doesn't support that command.",
    FtmsResultCode.invalidParameter => "That speed isn't valid for this treadmill.",
    FtmsResultCode.operationFailed => 'The treadmill rejected that. Try again.',
    FtmsResultCode.controlNotPermitted => 'Control was lost — reconnecting control...',
    FtmsResultCode.timeout => "The treadmill didn't respond in time.",
    FtmsResultCode.notConnected => 'Not connected to the treadmill.',
    FtmsResultCode.notSupported => "Speed control isn't available on this device.",
  };
}
```

Same debounce-and-coalesce requirement as the original: rapid +/− taps
produce **one** write of the final target. The generation counter is this
track's `CancellationTokenSource`-free way to say "only the most recent
debounce timer actually fires."

## Control widget

```dart
class TreadmillControlPanel extends ConsumerWidget {
  const TreadmillControlPanel({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = ref.watch(treadmillControlProvider);
    final notifier = ref.read(treadmillControlProvider.notifier);

    return IgnorePointer(
      ignoring: !s.canControl,
      child: Opacity(
        opacity: s.canControl ? 1 : 0.5,
        child: Column(children: [
          Text('${s.targetSpeedKph.toStringAsFixed(1)} km/h target'),
          Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            IconButton(icon: const Icon(Icons.remove), onPressed: notifier.decreaseSpeed),
            IconButton(icon: const Icon(Icons.add), onPressed: notifier.increaseSpeed),
          ]),
          Wrap(spacing: 8, children: s.presetSpeeds.map((p) => OutlinedButton(
            onPressed: () => notifier.setPresetSpeed(p),
            child: Text(p.toStringAsFixed(0)),
          )).toList()),
          const Divider(),
          FilledButton(
            onPressed: () async {
              final confirmed = await showDialog<bool>(
                context: context,
                builder: (context) => AlertDialog(
                  title: const Text('Stop the belt?'),
                  content: const Text(
                    'This sends a Bluetooth stop command. It is not the emergency '
                    'stop — pull the physical safety key if you need the belt to '
                    'stop immediately.'),
                  actions: [
                    TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
                    FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Stop')),
                  ],
                ),
              );
              if (confirmed == true) await notifier.stop();
            },
            child: const Text('Stop'),
          ),
          Text(
            'The physical safety key is the emergency stop. This button sends a '
            'Bluetooth command and may not respond instantly.',
            style: Theme.of(context).textTheme.bodySmall,
            textAlign: TextAlign.center,
          ),
        ]),
      ),
    );
  }
}
```

`IgnorePointer` + `Opacity` bound to `canControl` is this track's
`IsEnabled="{Binding CanControl}"` — Flutter has no built-in "disabled"
visual state on a plain `Column`, so both are set explicitly together.

## Safety — not pedantry, same rule as the original

- Label it **"Stop"**, never "Emergency Stop."
- State in the UI that the safety key is the emergency stop.
- Never call `start()` without a deliberate on-screen user action — not from
  a notification action, not from restored state, not from auto-reconnect.
  (`treadmill_service.dart`'s doc comment on `start()` already says so.)

## Tests

Same manual matrix as the original (increase/decrease, rapid 10× tap
produces one write, clamping at range limits, Stop confirmation, control
after reconnect, result-code 0x05 handling, indication timeout), all
`[HUMAN]` except the automated ones: using `FakeTreadmillService`, ten rapid
`increaseSpeed()` calls should reach the fake exactly once; `_generatePresets`
against several `SpeedRange` values should always land in-range and on a
valid increment; `_describeResult` should have one assertion per
`FtmsResultCode` value so an unhandled future value fails the test instead
of silently falling through.

## Acceptance

- [ ] 20 consecutive speed changes succeed
- [ ] No command ever sent outside the device's range
- [ ] Rapid tapping produces one write
- [ ] Nothing in the app can start the belt without a deliberate on-screen tap
- [ ] Stop requires confirmation and is never labelled "Emergency Stop"
