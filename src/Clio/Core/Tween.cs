using System.Diagnostics;
using System.Windows.Media;

namespace Clio.Core;

/// <summary>
/// A frame-driven animation that exists only while it runs. Hooked to
/// CompositionTarget.Rendering and unhooked at the end, so an idle Clio has
/// no timer, no storyboard and no per-frame callback alive at all.
/// </summary>
internal static class Tween
{
    public static void Run(TimeSpan duration, Action<double> step, Action? done = null)
    {
        var clock = Stopwatch.StartNew();
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var p = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            try
            {
                step(p);
            }
            catch (Exception ex)
            {
                Log.Error("animation step failed", ex);
                p = 1.0;
            }

            if (p < 1.0) return;
            CompositionTarget.Rendering -= tick;
            done?.Invoke();
        };
        CompositionTarget.Rendering += tick;
    }

    public static double EaseIn(double p) => p * p * p;
    public static double EaseOut(double p) => 1 - Math.Pow(1 - p, 3);
    public static double EaseInOut(double p) => p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2;
}
