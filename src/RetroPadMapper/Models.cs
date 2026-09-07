using System.Text.Json.Serialization;

namespace RetroPadMapper;

internal enum PadButton
{
    A = 0, B = 1, X = 2, Y = 3, Select = 4, Home = 5, Start = 6,
    L = 9, R = 10, Up = 11, Down = 12, Left = 13, Right = 14
}

internal enum OutputKind { None, Key, MouseButton, MouseWheel }

internal sealed record OutputBinding(OutputKind Kind, int Code, string Label)
{
    public static readonly OutputBinding None = new(OutputKind.None, 0, "なし");
    public override string ToString() => Label;
}

internal sealed record ControllerOption(string Id, uint InstanceId, string Name)
{
    public override string ToString() => Name;
}

internal sealed class AppSettings
{
    public bool MappingEnabled { get; set; } = true;
    public bool StartMinimized { get; set; }
    public string PreferredController { get; set; } = "";
    public bool ShowIndicator { get; set; } = true;
    public bool IndicatorTopMost { get; set; }
    public int IndicatorX { get; set; } = -1;
    public int IndicatorY { get; set; } = -1;
    public Dictionary<PadButton, OutputBinding> Bindings { get; set; } = Defaults();

    public static Dictionary<PadButton, OutputBinding> Defaults() => new()
    {
        [PadButton.Up] = Key(Keys.Up), [PadButton.Down] = Key(Keys.Down),
        [PadButton.Left] = Key(Keys.Left), [PadButton.Right] = Key(Keys.Right),
        [PadButton.A] = Key(Keys.Z), [PadButton.B] = Key(Keys.X),
        [PadButton.X] = Key(Keys.A), [PadButton.Y] = Key(Keys.S),
        [PadButton.Start] = Key(Keys.Enter), [PadButton.Select] = Key(Keys.Space),
        [PadButton.L] = Key(Keys.Q), [PadButton.R] = Key(Keys.E)
    };

    private static OutputBinding Key(Keys key) => new(OutputKind.Key, (int)key, $"キー: {key}");
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext { }
