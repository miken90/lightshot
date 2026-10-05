using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Vortice.MediaFoundation;

namespace RecordingProbe;

public record RecoveryDetails(
    bool Success,
    int VideoFrames,
    int MicSamples,
    int LoopbackSamples,
    double DurationSeconds,
    string? ErrorMessage = null,
    string? FallbackTriggered = null
);

public static class Recover
{
    public static RecoveryDetails RecoverAndRemux(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            return new RecoveryDetails(false, 0, 0, 0, 0, $"Input file not found: {inputPath}");
        }

        try
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);

            // Step 1: Sanitize truncated trailing boxes if abruptly killed
            SanitizeFragmentedMp4(inputPath);

            // Step 2: Open input with IMFSourceReader
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(inputPath, null);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, true);

            // Step 3: Setup passthrough IMFSinkWriter for normal MP4
            using var writer = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, null);

            // Source-reader order is not the write order: classify every stream by its major type and copy
            // them all in passthrough, keeping each stream's own index mapping.
            var configErrors = new System.Collections.Generic.List<string>();
            var map = new System.Collections.Generic.Dictionary<int, int>();   // reader index -> writer index
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            var kinds = new System.Collections.Generic.Dictionary<int, string>();
            var streams = TakeAnalyzer.ClassifyStreams(inputPath);
            var audioOrder = 0;
            foreach (int ri in new System.Collections.Generic.List<int>(streams.video).Concat(streams.audio))
            {
                string kind = streams.video.Contains(ri) ? "video" : (audioOrder++ == 0 ? "mic" : "loopback");
                try
                {
                    using var nt = reader.GetNativeMediaType((SourceReaderIndex)ri, 0);
                    reader.SetCurrentMediaType((SourceReaderIndex)ri, nt);
                    int wi = writer.AddStream(nt);
                    writer.SetInputMediaType(wi, nt, null);
                    map[ri] = wi; counts[ri] = 0; kinds[ri] = kind;
                }
                catch (Exception ex) { configErrors.Add($"{kind}: {ex.Message}"); }
            }
            writer.BeginWriting();

            // Feed the stream that is furthest behind in time: the MP4 muxer buffers the leading streams and blocks
            // in WriteSample once the gap to a lagging stream grows (audio runs ahead of video by seconds in a take).
            var done = new System.Collections.Generic.HashSet<int>();
            var lastTs = map.Keys.ToDictionary(k => k, _ => 0L);
            long maxTimestampHns = 0;
            while (done.Count < map.Count)
            {
                int ri = map.Keys.Where(k => !done.Contains(k)).OrderBy(k => lastTs[k]).First();
                var s = reader.ReadSample((SourceReaderIndex)ri, SourceReaderControlFlag.None, out _, out var flags, out long ts);
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) { done.Add(ri); continue; }
                if (s == null) { lastTs[ri] = Math.Max(lastTs[ri], ts); continue; }
                writer.WriteSample(map[ri], s);
                counts[ri]++;
                lastTs[ri] = Math.Max(lastTs[ri], ts);
                if (ts > maxTimestampHns) maxTimestampHns = ts;
                s.Dispose();
            }
            writer.Finalize();

            int vCount = 0, a1Count = 0, a2Count = 0;
            foreach (var ri in map.Keys)
            {
                if (kinds[ri] == "video") vCount += counts[ri];
                else if (kinds[ri] == "mic") a1Count += counts[ri];
                else a2Count += counts[ri];
            }
            double durationSec = maxTimestampHns / 10_000_000.0;
            bool hasVideo = vCount > 0;
            bool hasMic = a1Count > 0;
            bool hasLoopback = a2Count > 0;

            bool success = hasVideo && hasMic && hasLoopback && configErrors.Count == 0;
            return new RecoveryDetails(
                success,
                vCount,
                a1Count,
                a2Count,
                durationSec,
                success ? null : "One or more tracks lacked samples or failed to configure: " + string.Join("; ", configErrors),
                null
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG RecoverAndRemux] Exception: {ex}");
            return new RecoveryDetails(
                false, 0, 0, 0, 0,
                ex.Message,
                "FFmpeg passthrough remux evaluation (LGPL)"
            );
        }
    }

    private static void SanitizeFragmentedMp4(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
            long fileLen = fs.Length;
            byte[] hdr = new byte[8];
            long pos = 0;
            long lastValidPos = 0;

            while (pos + 8 <= fileLen)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                if (fs.Read(hdr, 0, 8) < 8) break;

                uint boxSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
                if (boxSize == 0)
                {
                    lastValidPos = fileLen;
                    break;
                }
                if (boxSize == 1)
                {
                    byte[] ext = new byte[8];
                    if (fs.Read(ext, 0, 8) < 8) break;
                    ulong largeSize = ((ulong)ext[0] << 56) | ((ulong)ext[1] << 48) | ((ulong)ext[2] << 40) | ((ulong)ext[3] << 32) |
                                      ((ulong)ext[4] << 24) | ((ulong)ext[5] << 16) | ((ulong)ext[6] << 8) | ext[7];
                    if (pos + (long)largeSize > fileLen) break;
                    pos += (long)largeSize;
                }
                else
                {
                    if (pos + boxSize > fileLen) break;
                    pos += boxSize;
                }
                lastValidPos = pos;
            }

            if (lastValidPos > 0 && lastValidPos < fileLen)
            {
                fs.SetLength(lastValidPos);
            }
        }
        catch { }
    }

    public static (bool wmpPass, bool mediaElementPass, string? error) MachineCheckPlayback(string filePath)
    {
        bool wmpPass = false;
        bool mediaElementPass = false;
        string? error = null;

        // 1. Windows Media Player / Media Foundation SourceReader full decode to EOF check
        try
        {
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(filePath, null);
            using var vt = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            int frames = 0;
            while (true)
            {
                var s = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
                if (s != null)
                {
                    frames++;
                    s.Dispose();
                }
            }
            wmpPass = frames > 0;
        }
        catch (Exception ex)
        {
            error = "WMP/MF decode error: " + ex.Message;
        }

        // 2. WPF MediaElement machine check
        try
        {
            bool opened = false;
            bool ended = false;
            bool advanced = false;
            string? meError = null;

            var thread = new Thread(() =>
            {
                var win = new Window
                {
                    Width = 200,
                    Height = 200,
                    ShowInTaskbar = false
                };
                var media = new MediaElement
                {
                    LoadedBehavior = MediaState.Play,
                    UnloadedBehavior = MediaState.Manual,
                    Source = new Uri(System.IO.Path.GetFullPath(filePath))
                };
                media.MediaOpened += (s, e) =>
                {
                    opened = true;
                    // Let it really play for 2 s (position must advance), then seek near the end and play to MediaEnded.
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s2, e2) =>
                    {
                        timer.Stop();
                        advanced = media.Position.TotalSeconds > 0.5;
                        if (media.NaturalDuration.HasTimeSpan && media.NaturalDuration.TimeSpan.TotalSeconds > 2.0)
                            media.Position = media.NaturalDuration.TimeSpan - TimeSpan.FromSeconds(0.5);
                    };
                    timer.Start();
                };
                media.MediaEnded += (s, e) =>
                {
                    ended = true;
                    win.Close();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                };
                media.MediaFailed += (s, e) =>
                {
                    meError = e.ErrorException?.Message ?? "MediaFailed";
                    win.Close();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                };
                win.Content = media;
                win.Show();
                System.Windows.Threading.Dispatcher.Run();
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (thread.Join(15000))
            {
                mediaElementPass = opened && advanced && ended && meError == null;
                if (meError != null && error == null) error = "MediaElement error: " + meError;
            }
            else
            {
                if (error == null) error = "MediaElement playback timed out";
            }
        }
        catch (Exception ex)
        {
            if (error == null) error = "MediaElement exception: " + ex.Message;
        }

        return (wmpPass, mediaElementPass, error);
    }
}
