using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VNotch;

// faridfox fork: softer notch ears, after Grump (Fork/README.md ledger). The ears are the small concave curves that join
// the top corners of the notch to the top edge of the screen. Upstream draws them 11 x 10 DIPs; a wider, softer curve
// reads as one piece with the screen edge. Same curve as upstream's, scaled up, on the notch and on its shadow layer.
// Upstream only sets the ears' Visibility and Opacity (UpdateEarVisibility and the island/notch mode change), and
// nothing measures their size, so the notch stays centred (both ears grow alike).
// Fork-owned part of upstream's partial MainWindow; attached by Fork/Core/ForkUi.cs.
public partial class MainWindow
{
    internal const double ForkEarWidth = 16;
    internal const double ForkEarHeight = 14;

    internal void ForkAttachNotchShape()
    {
        var left = ForkEarGeometry(left: true);
        var right = ForkEarGeometry(left: false);
        foreach (var ear in new[] { LeftEar, LeftShadowEar }) ForkShapeEar(ear, left);
        foreach (var ear in new[] { RightEar, RightShadowEar }) ForkShapeEar(ear, right);
    }

    private static void ForkShapeEar(Path? ear, Geometry shape)
    {
        if (ear == null) return;
        ear.Data = shape;
        ear.Width = ForkEarWidth;
        ear.Height = ForkEarHeight;
    }

    /// <summary>
    /// Upstream's ear, scaled: a straight top edge and a curve from the notch's side up to the screen edge.
    /// Left: "M 0,0 L 11,0 L 11,10 C 11,4.4 6.2,0 0,0 Z"; the right ear is its mirror image.
    /// </summary>
    internal static Geometry ForkEarGeometry(bool left)
    {
        double w = ForkEarWidth, h = ForkEarHeight;
        double cy = h * 4.4 / 10;
        double cx = w * 6.2 / 11;
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string data = left
            ? $"M 0,0 L {F(w)},0 L {F(w)},{F(h)} C {F(w)},{F(cy)} {F(cx)},0 0,0 Z"
            : $"M {F(w)},0 L 0,0 L 0,{F(h)} C 0,{F(cy)} {F(w - cx)},0 {F(w)},0 Z";
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
