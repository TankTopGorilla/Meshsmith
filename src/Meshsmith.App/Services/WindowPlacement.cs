using System.Runtime.InteropServices;
using System.Windows;

namespace Meshsmith.App.Services;

/// <summary>The window rectangle, kept somewhere the user can actually reach it.
///
/// <para>Restoring a saved rectangle is not enough on its own: a monitor can be unplugged, a
/// laptop docked, the resolution changed between sessions. The clamp is pure arithmetic so it can be
/// tested without a window, and the work area comes from the monitor the rectangle lands on —
/// <c>SystemParameters.WorkArea</c> only ever describes the primary one.</para></summary>
public sealed partial record WindowPlacement
{
    /// <summary>Fits <paramref name="desired"/> into <paramref name="work"/>: shrunk if it is
    /// larger, centred if it is nowhere near, otherwise shifted wholly inside. An empty work area
    /// (Windows gave us nothing) means "leave it alone".</summary>
    public static Rect Clamp(Rect desired, Rect work)
    {
        if (work.IsEmpty) return desired;

        double width = Math.Min(desired.Width, work.Width);
        double height = Math.Min(desired.Height, work.Height);

        // An explicit intersection test, not a failed P/Invoke: MonitorFromRect snaps to the nearest
        // monitor and never fails, so "off every screen" can only be decided by comparing rectangles.
        var sized = new Rect(desired.X, desired.Y, width, height);
        if (!sized.IntersectsWith(work))
        {
            return new Rect(work.Left + (work.Width - width) / 2,
                            work.Top + (work.Height - height) / 2,
                            width, height);
        }

        double x = Math.Clamp(desired.X, work.Left, work.Right - width);
        double y = Math.Clamp(desired.Y, work.Top, work.Bottom - height);
        return new Rect(x, y, width, height);
    }

    /// <summary>Rounds the live window rectangle (or its restore bounds when maximized) into whole
    /// device-independent pixels, which is what the settings file stores.</summary>
    public static WindowPlacement Capture(double left, double top, double width, double height,
                                          bool maximized) =>
        new(Round(left), Round(top), Round(width), Round(height), maximized);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>The usable area of the monitor a rectangle lands on.
///
/// <para><c>GetMonitorInfoW</c> is six lines and was proven on this machine; the alternative,
/// <c>UseWindowsForms</c> plus <c>Screen.FromPoint</c>, flips the whole app to a WinForms-referencing
/// WPF build for one rectangle. The fallbacks are ordered: that monitor's work area, the primary
/// work area, then the union of every screen.</para></summary>
internal static class ScreenWorkArea
{
    private const int MonitorDefaultToNearest = 2;

    public static Rect ForRect(Rect rect)
    {
        try
        {
            var native = new NativeRect
            {
                Left = (int)Math.Floor(rect.Left),
                Top = (int)Math.Floor(rect.Top),
                Right = (int)Math.Ceiling(rect.Right),
                Bottom = (int)Math.Ceiling(rect.Bottom)
            };

            var monitor = MonitorFromRect(ref native, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfoW(monitor, ref info))
                {
                    var work = info.Work;
                    return new Rect(work.Left, work.Top,
                                    work.Right - work.Left, work.Bottom - work.Top);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
                                   or MarshalDirectiveException)
        {
            // A Win32 shape we cannot call is not a reason to fail startup.
        }

        var primary = SystemParameters.WorkArea;
        if (!primary.IsEmpty && primary.Width > 0 && primary.Height > 0) return primary;

        return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}
