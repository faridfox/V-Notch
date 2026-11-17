using System;
using System.Threading.Tasks;
using Windows.Media;

namespace VNotch.Services;

/// <summary>Repeat as the player understands it: off, the whole list/album/playlist, or the current track.</summary>
internal enum ForkRepeat { Off, All, One }

/// <summary>What the player supports and what is on right now. Unsupported = the player does not take the command.</summary>
internal sealed record ForkPlaybackModes(bool ShuffleSupported, bool Shuffle, bool RepeatSupported, ForkRepeat Repeat)
{
    public static readonly ForkPlaybackModes None = new(false, false, false, ForkRepeat.Off);
}

/// <summary>Shuffle and repeat of the session upstream controls (tests stub it).</summary>
internal interface IForkPlaybackModes
{
    ForkPlaybackModes ForkGetModes();
    Task<bool> ForkSetShuffleAsync(bool on);
    Task<bool> ForkSetRepeatAsync(ForkRepeat mode);
}

// faridfox fork: shuffle and repeat through Windows' media controls (SMTC), on the same session upstream's play, pause,
// next and previous act on (its private GetActiveSession: the pinned or displayed one). Players that support them say
// so through their controls (Spotify does; browsers do not). A fork-owned part of upstream's partial
// MediaDetectionService; the commands run on the thread pool like upstream's transport commands.
public sealed partial class MediaDetectionService : IForkPlaybackModes
{
    ForkPlaybackModes IForkPlaybackModes.ForkGetModes()
    {
        try
        {
            var info = GetActiveSession()?.GetPlaybackInfo();
            if (info == null) return ForkPlaybackModes.None;
            var controls = info.Controls;
            return new ForkPlaybackModes(
                controls?.IsShuffleEnabled == true, info.IsShuffleActive == true,
                controls?.IsRepeatEnabled == true, FromSmtc(info.AutoRepeatMode));
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn("FORK-PLAYBACK-MODES", $"Modes unavailable: {ex.Message}");
            return ForkPlaybackModes.None;
        }
    }

    Task<bool> IForkPlaybackModes.ForkSetShuffleAsync(bool on)
    {
        var session = GetActiveSession();
        if (session == null) return Task.FromResult(false);
        return Task.Run(async () =>
        {
            try { return await session.TryChangeShuffleActiveAsync(on); }
            catch (Exception ex) { RuntimeLog.Warn("FORK-PLAYBACK-MODES", $"Shuffle failed: {ex.Message}"); return false; }
        });
    }

    Task<bool> IForkPlaybackModes.ForkSetRepeatAsync(ForkRepeat mode)
    {
        var session = GetActiveSession();
        if (session == null) return Task.FromResult(false);
        var smtc = mode switch
        {
            ForkRepeat.All => MediaPlaybackAutoRepeatMode.List,
            ForkRepeat.One => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        };
        return Task.Run(async () =>
        {
            try { return await session.TryChangeAutoRepeatModeAsync(smtc); }
            catch (Exception ex) { RuntimeLog.Warn("FORK-PLAYBACK-MODES", $"Repeat failed: {ex.Message}"); return false; }
        });
    }

    private static ForkRepeat FromSmtc(MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        MediaPlaybackAutoRepeatMode.List => ForkRepeat.All,
        MediaPlaybackAutoRepeatMode.Track => ForkRepeat.One,
        _ => ForkRepeat.Off,
    };
}
