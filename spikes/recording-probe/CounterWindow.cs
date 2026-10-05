using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace RecordingProbe;

/// <summary>
/// Capture source: a borderless topmost window at the desktop origin showing a Gray-coded frame counter as
/// binary blocks (counter = floor(seconds since start x 60)) and a panel that turns white for 100 ms at each
/// clap time. Everything is decoded back from pixels of the recorded file.
/// </summary>
public sealed class CounterWindow
{
    public const int Bits = 20;
    // Layout in DIPs; all values are multiples of 4 so they land on whole pixels at 125% scale.
    public const double BitSize = 16, BitPitch = 20, BitX0 = 8, BitY0 = 8;
    public const double FlashX = 8, FlashY = 40, FlashW = 384, FlashH = 150;
    public const double WindowW = 400, WindowH = 200;
    public const int FlashMs = 100;

    private readonly long _startQpc;
    private readonly long[] _clapQpc;
    private readonly object _lock = new();
    private readonly HashSet<int> _rendered = new();
    private readonly long[] _flashCommitQpc;
    private readonly bool[] _fired;
    private Thread? _thread;
    private Window? _window;
    private Visual? _visual;
    private long _flashUntilQpc;
    private int _counter;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private long _renderCallbacks;

    public double DpiScale { get; private set; } = 1.0;

    public CounterWindow(long startQpc, long[] clapQpc)
    {
        _startQpc = startQpc;
        _clapQpc = clapQpc;
        _flashCommitQpc = new long[clapQpc.Length];
        _fired = new bool[clapQpc.Length];
    }

    public long RenderCallbacks => Interlocked.Read(ref _renderCallbacks);

    /// <summary>Counter values the window actually set in a render callback (the presented source frames).</summary>
    public HashSet<int> RenderedCounters
    {
        get { lock (_lock) return new HashSet<int>(_rendered); }
    }

    /// <summary>QPC at which the render callback committed the white panel for each clap (0 if never).</summary>
    public long[] FlashCommitQpc => (long[])_flashCommitQpc.Clone();

    public static int ToGray(int v) => v ^ (v >> 1);

    public static int FromGray(int g)
    {
        int v = 0;
        for (; g != 0; g >>= 1) v ^= g;
        return v;
    }

    public void Start()
    {
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            var element = new SourceElement(this) { Width = WindowW, Height = WindowH };
            _visual = element;
            _window = new Window
            {
                Title = "Lightshot Recording Probe Source",
                Width = WindowW,
                Height = WindowH,
                Left = 0,
                Top = 0,
                Topmost = true,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Black,
                ShowInTaskbar = false,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                Content = element
            };
            _window.Show();
            DpiScale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
            CompositionTarget.Rendering += OnRendering;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
        if (!ready.Wait(10_000)) throw new TimeoutException("Counter window did not start");
    }

    public void Close()
    {
        var w = _window;
        if (w == null) return;
        w.Dispatcher.Invoke(() =>
        {
            CompositionTarget.Rendering -= OnRendering;
            w.Close();
        });
        w.Dispatcher.InvokeShutdown();
        _thread?.Join(5000);
        _window = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _renderCallbacks);
        double elapsed = Math.Max(0.0, (now - _startQpc) / (double)Stopwatch.Frequency);
        // One increment per composed frame: WPF may raise Rendering several times with the same RenderingTime, and a
        // purely time-derived counter would skip and repeat values at 60 Hz even with no capture loss.
        var renderingTime = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        bool newFrame = renderingTime != _lastRenderingTime;
        _lastRenderingTime = renderingTime;
        int target = (int)(elapsed * 60.0);
        if (newFrame && target > _counter) _counter++;
        lock (_lock) _rendered.Add(_counter);

        for (int k = 0; k < _clapQpc.Length; k++)
        {
            if (!_fired[k] && now >= _clapQpc[k])
            {
                _fired[k] = true;
                _flashCommitQpc[k] = now;
                _flashUntilQpc = now + Stopwatch.Frequency * FlashMs / 1000;
            }
        }
        (_visual as SourceElement)?.InvalidateVisual();
    }

    private bool Flashing => Stopwatch.GetTimestamp() < _flashUntilQpc;

    private sealed class SourceElement : FrameworkElement
    {
        private readonly CounterWindow _owner;
        public SourceElement(CounterWindow owner) { _owner = owner; }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, WindowW, WindowH));
            int gray = ToGray(_owner._counter);
            for (int i = 0; i < Bits; i++)
            {
                bool one = ((gray >> i) & 1) != 0;
                dc.DrawRectangle(one ? Brushes.White : Brushes.Black, null,
                    new Rect(BitX0 + i * BitPitch, BitY0, BitSize, BitSize));
            }
            dc.DrawRectangle(_owner.Flashing ? Brushes.White : Brushes.Black, null,
                new Rect(FlashX, FlashY, FlashW, FlashH));
        }
    }
}
