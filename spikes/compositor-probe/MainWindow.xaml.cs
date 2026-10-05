using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CompositorProbe;

public partial class MainWindow : Window
{
    public IPresentHost Host { get; }
    public int DpiChangedMessages;
    public int OtherDpiMessages;
    public volatile bool CountMessages;
    public System.Collections.Concurrent.ConcurrentDictionary<int, int> Messages { get; } = new();
    public int GetDpiScaledSizeMessages;
    public int WpfDpiChangedEvents;
    public IntPtr Hwnd { get; private set; }

    public MainWindow(IPresentHost host)
    {
        InitializeComponent();
        Host = host;
        HostGrid.Children.Add((UIElement)host);
        DpiChanged += (_, _) => Interlocked.Increment(ref WpfDpiChangedEvents);
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(Hwnd)!.AddHook(Hook);
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }

    // The default maximum tracking size is about one screen; lift it so a window can hold a 2560x1440 client on a 1080p panel.
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (CountMessages) Messages.AddOrUpdate(msg, 1, (_, n) => n + 1);
        if (msg == 0x0024)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.MaxTrack = new POINT { X = 8000, Y = 6000 };
            mmi.MaxSize = new POINT { X = 8000, Y = 6000 };
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        else if (msg == (int)Native.WM_DPICHANGED) Interlocked.Increment(ref DpiChangedMessages);
        else if (msg == 0x02E4) Interlocked.Increment(ref GetDpiScaledSizeMessages);
        else if (msg >= 0x02E1 && msg <= 0x02E3) Interlocked.Increment(ref OtherDpiMessages);
        return IntPtr.Zero;
    }
}
