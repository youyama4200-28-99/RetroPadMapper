using System.Text.Json;

namespace RetroPadMapper;

internal sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RetroPadMapper", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(_path), SettingsJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(temp, _path, true);
    }

    public string ImportIndicatorImage(string sourcePath)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"))
            throw new InvalidOperationException("PNG、JPEG、BMP、GIF画像を選択してください。");
        var destination = Path.Combine(directory, "indicator-custom" + extension);
        File.Copy(sourcePath, destination, true);
        return destination;
    }
}
