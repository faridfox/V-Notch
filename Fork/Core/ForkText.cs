using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using VNotch.Services;

namespace VNotch.Fork;

// faridfox fork: texts for fork UI. Read from Fork/Locales/<language>.json (embedded by Fork/msbuild/Fork.targets
// as "VNotchFork.Locales.<language>.json", deliberately outside upstream's "VNotch.Locales." names), following the
// app language and falling back to English, then to the key. Never pass a fork key to upstream's Loc.Get: upstream's
// LocalizationTests scan every .cs/.xaml file (comments too) for literal Loc.Get keys and require each one in all of
// upstream's locale files.
internal static class ForkText
{
    internal const string ResourcePrefix = "VNotchFork.Locales.";
    private const string Fallback = "en";

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase);

    public static string T(string key)
    {
        if (Table(Loc.CurrentLanguage).TryGetValue(key, out var text)) return text;
        if (Table(Fallback).TryGetValue(key, out text)) return text;
        return key;
    }

    internal static IReadOnlyDictionary<string, string> Table(string? language) =>
        Tables.GetOrAdd(string.IsNullOrWhiteSpace(language) ? Fallback : language, Load);

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + language + ".json");
        if (stream == null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }
}
