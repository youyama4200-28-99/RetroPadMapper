# Diagnostic validation — 0.4.2-diagnostics.1

## Scope

Diagnostic instrumentation only. No reconnection, virtual-device filtering, selection-policy,
Bluetooth repair policy, or indicator-layout fix is claimed.

## Automated checks

- All four editions: Release builds and existing --self-test passed (exit 0).
- All four editions: --diagnostic-self-test passed (exit 0).
- Published output: all four --self-test runs passed (exit 0).
- Fault injection covers a blocked writer/full bounded queue, disk exception and recovery,
  rejection after shutdown, concurrent producers during shutdown, and idempotent shutdown.
- Event age tests cover valid, zero and future timestamps; unknown is not interpreted as zero.
- Selection diagnostic tests distinguish exact match, fallback, no candidates and missing preference.
- Shared AppLog, ConnectionDiagnostics, DiagnosticLogSink and DiagnosticSelfTest are distributed
  identically in all editions. No new application dependency or license is introduced.

## Instrumentation microbenchmark

Command: dotnet run --project tests/DiagnosticsHarness/DiagnosticsHarness.csproj -c Release

The harness links the production ConnectionDiagnostics implementation, with a no-op logging
environment and minimal model stubs. It excludes SDL, locking by other threads, OS input,
virtual drivers, file writing, the UI and the target game. This is not end-to-end latency.

Warm-up: 20,000 calls per condition. Measurement: 500,000 calls × 7 rounds per condition.
Values below are medians and slowest round of per-round mean cost, NOT event-latency percentiles.

| Debug | Median µs/call | Slowest round µs/call | Maximum allocated bytes/round |
|---|---:|---:|---:|
| Off | 0.006697 | 0.006708 | 0 |
| On | 0.047470 | 0.047801 | 0 |

The first nonzero input snapshot is stored as numeric evidence and formatted during a later scan.
The warm steady-state allocation assertion does not cover connection changes, scans, or log summaries.
No claim of zero real-world latency impact is made.

## Still requiring physical testing

- HVC sleep/wake and reconnect without deleting its registration.
- App closed vs Keyboard & Mouse vs XInput vs DirectInput.
- Identity correlation for app-generated virtual controllers.
- Native event age vs UI delay and device disappearance in Windows.
- Actual input/output latency and missing inputs with logging on/off and hotplug activity.

See DIAGNOSTICS.md for the procedure. Do not publish raw device logs without redaction.
