using System;
using System.Collections.Generic;
using System.Linq;
using VNotch.Models;
using VNotch.Services;

namespace VNotch.Fork.Features.MediaSwitcher;

// faridfox fork: rules of the media source switcher. Pure, unit-tested in Tests/Fork.
internal static class MediaSwitcherLogic
{
    /// <summary>A stable order to cycle through (Windows returns sessions in no guaranteed order).</summary>
    public static List<ForkMediaSession> Order(IEnumerable<ForkMediaSession> sessions) =>
        sessions.OrderBy(s => s.AppId, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Key, StringComparer.Ordinal).ToList();

    /// <summary>The session after (direction +1) or before (-1) the shown one, wrapping around; null if there is no other.</summary>
    public static ForkMediaSession? Step(IReadOnlyList<ForkMediaSession> ordered, string? shownKey, int direction)
    {
        if (ordered.Count < 2) return null;
        int index = -1;
        for (int i = 0; i < ordered.Count; i++)
            if (ordered[i].Key == shownKey) { index = i; break; }
        if (index < 0) return direction >= 0 ? ordered[0] : ordered[^1];
        int next = ((index + Math.Sign(direction == 0 ? 1 : direction)) % ordered.Count + ordered.Count) % ordered.Count;
        return ordered[next];
    }

    /// <summary>
    /// The platform label the pin will keep for its whole life (upstream shows it and switches Spotify/YouTube features
    /// on it): what upstream itself displayed for that session if known and specific, else what the app id and metadata say.
    /// </summary>
    public static string Label(string? remembered, string classified)
    {
        string browser = MediaPlatform.Browser.ToDisplayString();
        if (!string.IsNullOrWhiteSpace(remembered) && remembered != browser) return remembered;
        return string.IsNullOrWhiteSpace(classified) ? browser : classified;
    }

    /// <summary>Upstream's classification of a session from its app id and metadata.</summary>
    public static string Classify(ForkMediaSession session)
    {
        var probe = new MediaInfo();
        MediaSourceClassifier.ApplyFromAppId(probe, session.AppId);
        MediaSourceClassifier.RefineFromMetadata(probe, session.Title.ToLowerInvariant(),
            session.Artist.ToLowerInvariant(), session.Album.ToLowerInvariant());
        return probe.MediaSource;
    }
}
