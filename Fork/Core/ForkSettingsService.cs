using System;
using System.IO;
using System.Text.Json;
using VNotch.Services;

namespace VNotch.Fork;

// faridfox fork: the fork's own settings, in %APPDATA%\V-Notch-faridfox\settings.json. They never go into upstream's
// NotchSettings: official builds delete unknown properties from the shared %APPDATA%\V-Notch\settings.json.
//
// Mirrors the settings window's model: a toggle is a live preview, Save/Apply commits it, closing without saving
// reverts it. Thread-safe: upstream raises SettingsApplied off the UI thread. Changed is raised on the caller's thread.
internal sealed class ForkSettingsService
{
    internal sealed class Data
    {
        public bool ShowLyricsInCompactNotch { get; set; } = true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _path;
    private readonly object _gate = new();
    private Data? _saved;
    private bool? _preview;

    public event Action? Changed;

    public ForkSettingsService() : this(DefaultFolder()) { }

    internal ForkSettingsService(string folder) => _path = Path.Combine(folder, "settings.json");

    internal static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V-Notch-faridfox");

    internal string FilePath => _path;

    /// <summary>The value in effect right now (a live preview while the settings window is open).</summary>
    public bool ShowLyricsInCompactNotch
    {
        get { lock (_gate) return _preview ?? Saved.ShowLyricsInCompactNotch; }
    }

    public void PreviewShowLyricsInCompactNotch(bool value)
    {
        bool changed;
        lock (_gate)
        {
            bool before = _preview ?? Saved.ShowLyricsInCompactNotch;
            _preview = value;
            changed = before != value;
        }
        if (changed) Changed?.Invoke();
    }

    public void CommitPreview()
    {
        lock (_gate)
        {
            if (_preview is not bool value) return;
            _preview = null;
            if (value == Saved.ShowLyricsInCompactNotch) return;
            Saved.ShowLyricsInCompactNotch = value;
            Save(Saved);
        }
    }

    public void RevertPreview()
    {
        bool changed;
        lock (_gate)
        {
            if (_preview is not bool value) return;
            _preview = null;
            changed = value != Saved.ShowLyricsInCompactNotch;
        }
        if (changed) Changed?.Invoke();
    }

    private Data Saved => _saved ??= Load();

    private Data Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path), JsonOptions) ?? new Data();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable file: run with defaults and leave the file alone until the next explicit save.
            RuntimeLog.Error("FORK-SETTINGS", ex, "Fork settings could not be read; using defaults.");
        }
        return new Data();
    }

    private void Save(Data data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RuntimeLog.Error("FORK-SETTINGS", ex, "Fork settings could not be saved.");
        }
    }
}
