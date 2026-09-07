# Dispatch latency benchmark

- Generated (UTC): 2026-09-07T14:49:30.8272765Z
- Host: Microsoft Windows NT 10.0.26200.0; 16 logical CPUs; .NET 8.0.30
- Samples: 5,000; synthetic arrivals at 4 kHz
- Clock: `Stopwatch` monotonic high-resolution clock

| Strategy | n | p50 (µs) | p95 (µs) | p99 (µs) | max (µs) |
|---|---:|---:|---:|---:|---:|
| Previous 8 ms polling | 5000 | 4153.000 | 7871.900 | 8210.100 | 8891.900 |
| Event signal | 5000 | 6.900 | 9.900 | 14.300 | 562.800 |

**Predeclared check:** event p95 < polling p95 and both paths captured all samples — **PASS**.

This isolates scheduler/dispatch delay; it does not measure Bluetooth radio latency, controller firmware, SDL's HID backend, or a target game's input sampling. Raw paired observations are in `latency-samples.csv`.
