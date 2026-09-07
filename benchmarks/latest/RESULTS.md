# Dispatch latency benchmark

- Generated (UTC): 2026-09-07T13:53:32.5655975Z
- Host: Microsoft Windows NT 10.0.26200.0; 16 logical CPUs; .NET 8.0.30
- Samples: 5,000; synthetic arrivals at 4 kHz
- Clock: `Stopwatch` monotonic high-resolution clock

| Strategy | n | p50 (µs) | p95 (µs) | p99 (µs) | max (µs) |
|---|---:|---:|---:|---:|---:|
| Previous 8 ms polling | 5000 | 4198.100 | 7936.600 | 8269.000 | 9092.800 |
| Event signal | 5000 | 7.200 | 10.300 | 29.000 | 534.800 |

**Predeclared check:** event p95 < polling p95 and both paths captured all samples — **PASS**.

This isolates scheduler/dispatch delay; it does not measure Bluetooth radio latency, controller firmware, SDL's HID backend, or a target game's input sampling. Raw paired observations are in `latency-samples.csv`.
