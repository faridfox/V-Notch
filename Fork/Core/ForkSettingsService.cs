using System;
using System.IO;
using System.Text.Json;
using VNotch.Services;

namespace VNotch.Fork;

/// <summary>Where the expanded notch's page tabs sit: inside it (upstream), or in a pill below or beside it.</summary>
internal enum ForkTabsPosition { Top, Bottom, Left, Right }

/// <summary>How large the tabs pill is drawn.</summary>
internal enum ForkTabsSize { Default, Large, Larger }

// faridfox fork: the fork's own settings, in %APPDATA%\V-Notch-faridfox\settings.json. They never go into upstream's
// NotchSettings: official builds delete unknown properties from the shared %APPDATA%\V-Notch\settings.json.
//
// Mirrors the settings window's model: a change is a live preview, Save/Apply commits it, closing without saving
// reverts it. Thread-safe: upstream raises SettingsApplied off the UI thread. Changed is raised on the caller's thread.
internal sealed class ForkSettingsService
{
    internal sealed class Data
    {
        public bool ShowLyricsInCompactNotch { get; set; } = true;
        public ForkTabsPosition TabsPosition { get; set; } = ForkTabsPosition.Top;
        public ForkTabsSize TabsSize { get; set; } = ForkTabsSize.Default;

        public Data Clone() => (Data)MemberwiseClone();

        public bool SameAs(Data other) =>
            ShowLyricsInCompactNotch == other.ShowLyricsInCompactNotch &&
            TabsPosition == other.TabsPosition && TabsSize == other.TabsSize;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private Data? _saved;
    private Data? _preview;

    public event Action? Changed;

    public ForkSettingsService() : this(DefaultFolder()) { }

    internal ForkSettingsService(string folder) => _path = Path.Combine(folder, "settings.json");

    internal static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V-Notch-faridfox");

    internal string FilePath => _path;

    /// <summary>The values in effect right now (a live preview while the settings window is open).</summary>
    public Data Current
    {
        get { lock (_gate) return (_preview ?? Saved).Clone(); }
    }

    public bool ShowLyricsInCompactNotch => Current.ShowLyricsInCompactNotch;
    public ForkTabsPosition TabsPosition => Current.TabsPosition;
    public ForkTabsSize TabsSize => Current.TabsSize;

    public void PreviewShowLyricsInCompactNotch(bool value) => Preview(d => d.ShowLyricsInCompactNotch = value);
    public void PreviewTabs(ForkTabsPosition position, ForkTabsSize size) => Preview(d =>
    {
        d.TabsPosition = position;
        d.TabsSize = size;
    });

    /// <summary>Change the previewed values; Changed fires when what is in effect changed.</summary>
    public void Preview(Action<Data> change)
    {
        bool changed;
        lock (_gate)
        {
            var before = _preview ?? Saved;
            var next = before.Clone();
            change(next);
            _preview = next;
            changed = !next.SameAs(before);
        }
        if (changed) Changed?.Invoke();
    }

    public void CommitPreview()
    {
        lock (_gate)
        {
            if (_preview is not { } preview) return;
            _preview = null;
            if (preview.SameAs(Saved)) return;
            _saved = preview;
            Save(preview);
        }
    }

    public void RevertPreview()
    {
        bool changed;
        lock (_gate)
        {
            if (_preview is not { } preview) return;
            _preview = null;
            changed = !preview.SameAs(Saved);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
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
