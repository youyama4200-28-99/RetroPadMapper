# Dispatch latency benchmark

- Generated (UTC): 2026-09-07T15:22:39.7908792Z
- Host: Microsoft Windows NT 10.0.26200.0; 16 logical CPUs; .NET 10.0.11
- Samples: 5,000; synthetic arrivals at 4 kHz
- Clock: `Stopwatch` monotonic high-resolution clock

| Strategy | n | p50 (µs) | p95 (µs) | p99 (µs) | max (µs) |
|---|---:|---:|---:|---:|---:|
| Previous 8 ms polling | 5000 | 4072.200 | 7821.800 | 8156.400 | 8658.100 |
| 1 ms high-resolution polling | 5000 | 516.200 | 989.500 | 1125.700 | 3063.000 |

**Predeclared check:** 1 ms high-resolution polling p95 < 8 ms polling p95 and both paths captured all samples — **PASS**.

This isolates scheduler/dispatch delay; it does not measure Bluetooth radio latency, controller firmware, SDL's HID backend, or a target game's input sampling. Raw paired observations are in `latency-samples.csv`.
