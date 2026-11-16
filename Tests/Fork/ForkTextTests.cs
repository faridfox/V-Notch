using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VNotch.Fork;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests.Fork;

// faridfox fork: fork texts (Fork/Locales + Fork/Core/ForkText.cs).
[Collection("Localization")]
public sealed class ForkTextTests
{
    private static readonly string[] UsedKeys = ["compactLyrics.setting", "compactLyrics.hint", "compactLyrics.keywords"];

    [Fact]
    public void EveryUsedKeyHasAnEnglishTextInTheEmbeddedTable()
    {
        var english = ForkText.Table("en");
        Assert.All(UsedKeys, key => Assert.False(string.IsNullOrWhiteSpace(english.GetValueOrDefault(key)), key));
    }

    [Fact]
    public void ForkResourcesStayOutOfUpstreamsLocaleNamespace()
    {
        var names = typeof(MainWindow).Assembly.GetManifestResourceNames();
        Assert.Contains(ForkText.ResourcePrefix + "en.json", names);
        Assert.DoesNotContain(names, name => name.StartsWith("VNotch.Locales.", StringComparison.Ordinal) &&
                                            name.Contains("compactLyrics", StringComparison.Ordinal));
        Assert.False(ForkText.ResourcePrefix.StartsWith("VNotch.Locales.", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryForkLanguageFileHasTheEnglishKeys()
    {
        string folder = Path.Combine(FindRepositoryRoot(), "Fork", "Locales");
        var englishKeys = Keys(Path.Combine(folder, "en.json"));
        foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
            Assert.Equal(englishKeys, Keys(file));
    }

    [Fact]
    public void UnknownLanguagesFallBackToEnglishAndUnknownKeysToTheKey()
    {
        string previous = Loc.CurrentLanguage;
        try
        {
            Loc.SetLanguage("vi");
            Assert.Equal(ForkText.Table("en")["compactLyrics.setting"], ForkText.T("compactLyrics.setting"));
            Assert.Equal("no.such.key", ForkText.T("no.such.key"));
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    private static SortedSet<string> Keys(string file) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!.Keys, StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "V-Notch.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("V-Notch.csproj not found above the test output folder.");
    }
}
