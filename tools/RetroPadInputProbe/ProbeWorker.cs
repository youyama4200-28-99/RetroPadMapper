using System.Diagnostics;
using System.Text.Json;

namespace RetroPadMapper;

internal static class ProbeWorker
{
    private sealed record Device(uint Id, string Name, string Path, ushort Vendor, ushort Product);
    private static void Report(string kind, object? data = null)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { time = DateTimeOffset.Now, mono = Stopwatch.GetTimestamp(), kind, data }));
        Console.Out.Flush();
    }

    internal static unsafe int Run(string backend, int seconds)
    {
        if (backend is not ("windows" or "hidapi") || seconds is < 1 or > 180) return 2;
        nint joystick = 0;
        uint instance = 0;
        bool initialized = false;
        try
        {
            Report("start", new { backend, seconds, sdl = SdlNative.Utf8(SdlNative.GetRevision()), output = "none", settings = "not_loaded" });
            foreach (var (key, value) in new (string, string)[] {
                ("SDL_JOYSTICK_HIDAPI", backend == "hidapi" ? "1" : "0"),
                ("SDL_JOYSTICK_HIDAPI_NINTENDO_CLASSIC", backend == "hidapi" ? "1" : "0"),
                ("SDL_JOYSTICK_HIDAPI_JOY_CONS", backend == "hidapi" ? "1" : "0"),
                ("SDL_JOYSTICK_HIDAPI_SWITCH", backend == "hidapi" ? "1" : "0"),
                ("SDL_JOYSTICK_DIRECTINPUT", "1"), ("SDL_JOYSTICK_WGI", "0"),
                ("SDL_JOYSTICK_RAWINPUT", "0"), ("SDL_JOYSTICK_XINPUT", "0"),
                ("SDL_JOYSTICK_GAMEINPUT", "0"), ("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1") })
            {
                if (!SdlNative.SetHintWithPriority(key, value, 2)) throw new InvalidOperationException($"Hint rejected: {key}");
                Report("hint", new { key, value });
            }
            Report("initializing");
            if (!SdlNative.Init(0x200)) { Report("init_failed", SdlNative.Utf8(SdlNative.GetError())); return 3; }
            initialized = true;
            var deadline = Environment.TickCount64 + seconds * 1000L;
            long nextScan = 0, nextHeartbeat = 0;
            var previousInventory = "";
            ulong previousButtons = 0;
            byte previousHat = 0;
            var observed = false;
            var everOpened = false;
            var attempted = new HashSet<uint>();
            while (Environment.TickCount64 < deadline)
            {
                // Only this child thread enters SDL. A hung native call cannot block the parent UI.
                while (SdlNative.PollEvent(out _)) { }
                SdlNative.UpdateJoysticks();
                var now = Environment.TickCount64;
                if (joystick != 0 && !SdlNative.JoystickConnected(joystick))
                {
                    Report("disconnected", new { instance });
                    SdlNative.CloseJoystick(joystick); joystick = 0; instance = 0;
                }
                if (now >= nextScan)
                {
                    var ids = SdlNative.GetJoysticks(out var count);
                    if (ids == 0) Report("enumeration_failed", SdlNative.Utf8(SdlNative.GetError()));
                    else
                    {
                        try
                        {
                            var devices = new List<Device>();
                            for (var i = 0; i < count; i++)
                            {
                                var id = ((uint*)ids)[i];
                                devices.Add(new(id, SdlNative.Utf8(SdlNative.GetJoystickNameForId(id)),
                                    SdlNative.Utf8(SdlNative.GetJoystickPathForId(id)),
                                    SdlNative.GetJoystickVendorForId(id), SdlNative.GetJoystickProductForId(id)));
                            }
                            var signature = JsonSerializer.Serialize(devices);
                            if (signature != previousInventory) { Report("inventory", devices); previousInventory = signature; }
                            // Never open arbitrary/Xbox devices, and never guess between multiple HVC/Joy-Con candidates.
                            var candidates = devices.Where(x => IsCandidate(x.Vendor, x.Product)).ToArray();
                            if (joystick == 0 && candidates.Length == 1 && attempted.Add(candidates[0].Id))
                            {
                                var selected = candidates[0];
                                Report("open_begin", selected);
                                var started = Stopwatch.GetTimestamp();
                                joystick = SdlNative.OpenJoystick(selected.Id);
                                var error = joystick == 0 ? SdlNative.Utf8(SdlNative.GetError()) : "";
                                Report("open_end", new { selected.Id, success = joystick != 0, ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds, error });
                                if (joystick != 0)
                                {
                                    instance = selected.Id; previousButtons = 0; previousHat = 0;
                                    everOpened = true;
                                    Report("capabilities", new { instance, buttons = SdlNative.GetNumJoystickButtons(joystick), hats = SdlNative.GetNumJoystickHats(joystick) });
                                }
                            }
                        }
                        finally { SdlNative.Free(ids); }
                    }
                    nextScan = now + 500;
                }
                if (joystick != 0)
                {
                    ulong buttons = 0;
                    var count = Math.Clamp(SdlNative.GetNumJoystickButtons(joystick), 0, 64);
                    for (var i = 0; i < count; i++)
                        if (SdlNative.GetJoystickButton(joystick, i)) buttons |= 1UL << i;
                    var hat = SdlNative.GetNumJoystickHats(joystick) > 0 ? SdlNative.GetJoystickHat(joystick, 0) : (byte)0;
                    if (buttons != previousButtons || hat != previousHat)
                    {
                        observed = true;
                        Report("input_change", new { instance, buttons = buttons.ToString("X16"), hat });
                        previousButtons = buttons; previousHat = hat;
                    }
                }
                if (now >= nextHeartbeat)
                {
                    Report("heartbeat", new { instance, observed, remaining_seconds = Math.Max(0, (deadline - now) / 1000) });
                    nextHeartbeat = now + 1000;
                }
                Thread.Sleep(8); // Observation tool only; not a mapper or latency benchmark.
            }
            Report("result", new { everOpened, observed, success_requires_manual_button_correlation = true });
            return observed ? 0 : everOpened ? 11 : 10;
        }
        catch (Exception ex) { Report("error", ex.ToString()); return 4; }
        finally
        {
            Report("cleanup");
            if (joystick != 0) SdlNative.CloseJoystick(joystick);
            if (initialized) SdlNative.Quit();
        }
    }

    internal static bool IsCandidate(ushort vendor, ushort product) => vendor == 0x057e && product is 0x2006 or 0x2007;
}
