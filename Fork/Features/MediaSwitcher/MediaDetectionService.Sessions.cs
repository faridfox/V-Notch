using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace VNotch.Services;

/// <summary>One media session Windows knows about: an app or browser tab that plays or holds media.</summary>
internal sealed record ForkMediaSession(string Key, string AppId, bool IsPlaying, string Title, string Artist, string Album);

/// <summary>The fork's view of all media sessions (tests stub it).</summary>
internal interface IForkMediaSessions
{
    Task<IReadOnlyList<ForkMediaSession>> ForkListSessionsAsync();
}

// faridfox fork: lists the media sessions for the source switcher (Fork/Features/MediaSwitcher). A fork-owned part of
// upstream's partial MediaDetectionService, because only its own session manager yields session keys that its pin
// (ToggleSessionPin) accepts: a key contains the session object's COM identity. Read-only: it never touches the
// service's state, so it is safe to call from the thread pool (the WinRT session objects are agile).
public sealed partial class MediaDetectionService : IForkMediaSessions
{
    async Task<IReadOnlyList<ForkMediaSession>> IForkMediaSessions.ForkListSessionsAsync()
    {
        var manager = _sessionManager;
        if (manager == null) return [];
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = manager.GetSessions(); }
        catch (Exception ex)
        {
            RuntimeLog.Warn("FORK-MEDIA-SWITCHER", $"Sessions unavailable: {ex.Message}");
            return [];
        }

        var result = new List<ForkMediaSession>(sessions.Count);
        foreach (var session in sessions)
        {
            try
            {
                string appId = session.SourceAppUserModelId ?? "";
                if (appId.Length == 0 || IsIgnoredSourceApp(appId)) continue;
                bool playing = IsSessionPlayingStatus(session.GetPlaybackInfo().PlaybackStatus);
                var properties = await session.TryGetMediaPropertiesAsync();
                string title = properties?.Title ?? "";
                if (string.IsNullOrWhiteSpace(title)) continue;   // nothing to show for it
                result.Add(new ForkMediaSession(BuildSessionInstanceKey(session), appId, playing, title,
                    properties?.Artist ?? "", properties?.AlbumTitle ?? ""));
            }
            catch (Exception ex)
            {
                // A session can close while it is being read; skip it.
                RuntimeLog.Warn("FORK-MEDIA-SWITCHER", $"Session skipped: {ex.Message}");
            }
        }
        return result;
    }
}
