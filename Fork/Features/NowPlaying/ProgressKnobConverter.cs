using System;
using System.Globalization;
using System.Windows.Data;

namespace VNotch.Fork.Features.NowPlaying;

// faridfox fork: [progress scale (0..1), bar width, knob size] -> knob X. A MultiBinding keeps the knob on the end of the
// played part every frame, also while upstream animates the bar (seek glide, hover growth).
internal sealed class ProgressKnobConverter : IMultiValueConverter
{
    public static readonly ProgressKnobConverter Instance = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        double Value(int i) => values.Length > i && values[i] is double d ? d : 0;
        return NowPlayingText.KnobLeft(Value(0), Value(1), Value(2));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
