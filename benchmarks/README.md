# Latency evidence

Run the falsifiable A/B benchmark from a published build:

```powershell
RetroPadMapper.exe --benchmark benchmarks/latest
```

It feeds the same 5,000 timestamped synthetic arrivals to two dispatch paths: the former fixed 8 ms polling loop (control) and an event signal (candidate). The command fails unless both paths capture every sample and the candidate's p95 wake-up latency is lower. It writes a report plus paired raw CSV data.

This deliberately makes a narrow claim. It tests dispatch architecture and Windows scheduling on the executing host, not end-to-end Bluetooth-to-game latency. The app separately displays SDL event-to-processing and output-call percentiles while a real controller is used.
