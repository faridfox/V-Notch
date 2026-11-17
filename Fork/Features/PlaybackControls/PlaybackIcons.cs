using System.Windows.Media;
using VNotch.Services;

namespace VNotch.Fork.Features.PlaybackControls;

// faridfox fork: transport icons in the style of Spotify's controls (drawn for the fork, not copied): solid skip
// triangles with a bar, a dark play/pause glyph on a light round button, shuffle as two crossing arrows, repeat as a
// loop (with a "1" for repeat-one). Geometries are frozen and shared.
internal static class PlaybackIcons
{
    // In upstream's 512 x 512 skip-icon canvas (its skip animation slides the paths inside it).
    public static readonly Geometry Next = Frozen("M 112,96 L 336,236 C 352,246 352,266 336,276 L 112,416 C 96,426 80,418 80,400 L 80,112 C 80,94 96,86 112,96 Z M 384,92 L 416,92 C 427,92 436,101 436,112 L 436,400 C 436,411 427,420 416,420 L 384,420 C 373,420 364,411 364,400 L 364,112 C 364,101 373,92 384,92 Z");
    public static readonly Geometry Previous = Frozen("M 400,96 L 176,236 C 160,246 160,266 176,276 L 400,416 C 416,426 432,418 432,400 L 432,112 C 432,94 416,86 400,96 Z M 128,92 L 96,92 C 85,92 76,101 76,112 L 76,400 C 76,411 85,420 96,420 L 128,420 C 139,420 148,411 148,400 L 148,112 C 148,101 139,92 128,92 Z");

    // In upstream's 46 x 46 play/pause canvases.
    public static readonly Geometry Play = Frozen("M 15,9.5 C 15,7.6 17,6.5 18.6,7.5 L 37,20.2 C 38.4,21.2 38.4,23.3 37,24.3 L 18.6,37 C 17,38 15,36.9 15,35 Z");
    public static readonly Geometry Pause = Frozen("M 12,8 L 18,8 C 19.1,8 20,8.9 20,10 L 20,36 C 20,37.1 19.1,38 18,38 L 12,38 C 10.9,38 10,37.1 10,36 L 10,10 C 10,8.9 10.9,8 12,8 Z M 28,8 L 34,8 C 35.1,8 36,8.9 36,10 L 36,36 C 36,37.1 35.1,38 34,38 L 28,38 C 26.9,38 26,37.1 26,36 L 26,10 C 26,8.9 26.9,8 28,8 Z");

    // In a 16 x 16 box, drawn as strokes.
    public static readonly Geometry Shuffle = Frozen("M 1.5,4.5 L 3.6,4.5 C 6.2,4.5 7.4,11.5 10.4,11.5 L 13.5,11.5 M 1.5,11.5 L 3.6,11.5 C 4.8,11.5 5.6,10.4 6.3,9.1 M 9.7,6.9 C 10.4,5.6 11.2,4.5 12.4,4.5 L 13.5,4.5 M 11.5,2.5 L 13.8,4.5 L 11.5,6.5 M 11.5,9.5 L 13.8,11.5 L 11.5,13.5");
    public static readonly Geometry Repeat = Frozen("M 4.2,12 L 3.8,12 C 2.5,12 1.5,11 1.5,9.7 L 1.5,6.3 C 1.5,5 2.5,4 3.8,4 L 12.2,4 C 13.5,4 14.5,5 14.5,6.3 L 14.5,9.7 C 14.5,11 13.5,12 12.2,12 L 7.5,12 M 9.3,10 L 7.3,12 L 9.3,14");
    /// <summary>The "1" of repeat-one, drawn small in the loop's opening.</summary>
    public static readonly Geometry RepeatOneMark = Frozen("M 7.4,7.3 L 8.4,6.4 L 8.4,9.9");

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}

// faridfox fork: rules of the shuffle/repeat buttons. Pure, unit-tested in Tests/Fork.
internal static class PlaybackModesLogic
{
    /// <summary>Spotify's order: off, repeat all, repeat one, off.</summary>
    public static ForkRepeat NextRepeat(ForkRepeat current) => current switch
    {
        ForkRepeat.Off => ForkRepeat.All,
        ForkRepeat.All => ForkRepeat.One,
        _ => ForkRepeat.Off,
    };
}
