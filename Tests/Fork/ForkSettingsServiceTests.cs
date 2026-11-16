using System;
using System.IO;
using VNotch.Fork;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: the fork's own settings file and its preview / commit / revert model (Fork/Core/ForkSettingsService.cs).
public sealed class ForkSettingsServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vnotch-fork-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void DefaultsToLyricsWithoutWritingAFile()
    {
        var settings = new ForkSettingsService(_folder);
        Assert.True(settings.ShowLyricsInCompactNotch);
        Assert.False(File.Exists(settings.FilePath));
    }

    [Fact]
    public void LivesOutsideUpstreamsSettingsFolder() =>
        Assert.EndsWith(Path.Combine("V-Notch-faridfox"), ForkSettingsService.DefaultFolder(), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void PreviewChangesTheValueAndRevertRestoresIt()
    {
        var settings = new ForkSettingsService(_folder);
        int changes = 0;
        settings.Changed += () => changes++;

        settings.PreviewShowLyricsInCompactNotch(false);
        Assert.False(settings.ShowLyricsInCompactNotch);
        Assert.Equal(1, changes);

        settings.RevertPreview();
        Assert.True(settings.ShowLyricsInCompactNotch);
        Assert.Equal(2, changes);
        Assert.False(File.Exists(settings.FilePath));
    }

    [Fact]
    public void CommitPersistsThePreviewAndARevertAfterwardsKeepsIt()
    {
        var settings = new ForkSettingsService(_folder);
        settings.PreviewShowLyricsInCompactNotch(false);
        settings.CommitPreview();
        settings.RevertPreview();

        Assert.False(settings.ShowLyricsInCompactNotch);
        Assert.False(new ForkSettingsService(_folder).ShowLyricsInCompactNotch);
    }

    [Fact]
    public void CommitWithoutAChangeWritesNothing()
    {
        var settings = new ForkSettingsService(_folder);
        settings.CommitPreview();
        settings.PreviewShowLyricsInCompactNotch(true);
        settings.CommitPreview();
        Assert.False(File.Exists(settings.FilePath));
    }

    [Fact]
    public void AnUnreadableFileFallsBackToDefaultsAndIsLeftAlone()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ not json");

        var settings = new ForkSettingsService(_folder);
        Assert.True(settings.ShowLyricsInCompactNotch);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), """{ "ShowLyricsInCompactNotch": false, "SomethingLater": 3 }""");
        Assert.False(new ForkSettingsService(_folder).ShowLyricsInCompactNotch);
    }
}
