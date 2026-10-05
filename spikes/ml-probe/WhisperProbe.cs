using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using Whisper.net;

namespace MlProbe;

public record TtsWord(string Word, double TimestampSec);
public record WhisperWord(string Word, double StartSec, double EndSec);

public class WhisperProbeResult
{
    public bool Pass { get; set; }
    public double AudioDurationSec { get; set; }
    public double ProcessingTimeSec { get; set; }
    public double RealTimeFactor { get; set; }
    public int TtsWordCount { get; set; }
    public int MatchedWordCount { get; set; }
    public double MedianErrorMs { get; set; }
    public double P95ErrorMs { get; set; }
    public double MaxErrorMs { get; set; }
    public string DeviceUsed { get; set; } = "CPU";
    public string? FallbackTriggered { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class WhisperProbe
{
    public static async Task<WhisperProbeResult> RunAsync(string modelPath, string artifactsDir)
    {
        var result = new WhisperProbeResult();
        string wavPath = Path.Combine(artifactsDir, "whisper_narration_5min.wav");

        try
        {
            Console.WriteLine("[WhisperProbe] 1. Synthesizing ~5-minute narration using Windows TTS...");
            var ttsWords = new List<TtsWord>();

            string narration = Generate5MinuteNarration();

            using (var synth = new SpeechSynthesizer())
            {
                synth.Rate = 0; // Normal conversational speed (~140-150 wpm)
                synth.SetOutputToWaveFile(wavPath, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

                synth.SpeakProgress += (s, e) =>
                {
                    string w = CleanWord(e.Text);
                    if (!string.IsNullOrEmpty(w))
                    {
                        ttsWords.Add(new TtsWord(w, e.AudioPosition.TotalSeconds));
                    }
                };

                synth.Speak(narration);
            }

            var fileInfo = new FileInfo(wavPath);
            // 16kHz, 16-bit mono = 32,000 bytes per second
            double audioDuration = (fileInfo.Length - 44) / 32000.0;
            result.AudioDurationSec = audioDuration;
            result.TtsWordCount = ttsWords.Count;
            Console.WriteLine($"[WhisperProbe] Synthesized {audioDuration:F1}s of audio ({ttsWords.Count} words, {fileInfo.Length / 1048576.0:F1} MB).");

            Console.WriteLine($"[WhisperProbe] 2. Running Whisper.net with ggml-base q5 on CPU...");
            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory.CreateBuilder()
                .WithLanguage("en")
                .WithTokenTimestamps()
                .Build();

            var whisperWords = new List<WhisperWord>();
            var sw = Stopwatch.StartNew();

            using (var audioStream = File.OpenRead(wavPath))
            {
                await foreach (var seg in processor.ProcessAsync(audioStream))
                {
                    if (seg.Tokens != null)
                    {
                        foreach (var tok in seg.Tokens)
                        {
                            string t = CleanWord(tok.Text);
                            if (!string.IsNullOrEmpty(t) && !t.StartsWith("["))
                            {
                                double startSec = tok.Start / 100.0;
                                double endSec = tok.End / 100.0;
                                whisperWords.Add(new WhisperWord(t, startSec, endSec));
                            }
                        }
                    }
                }
            }

            sw.Stop();
            result.ProcessingTimeSec = sw.Elapsed.TotalSeconds;
            result.RealTimeFactor = result.ProcessingTimeSec / Math.Max(1.0, result.AudioDurationSec);
            Console.WriteLine($"[WhisperProbe] Whisper finished in {result.ProcessingTimeSec:F2}s (RTF: {result.RealTimeFactor:F3}). Words recognized: {whisperWords.Count}");

            // 3. Align ground truth TTS words with Whisper words to measure timing error
            Console.WriteLine("[WhisperProbe] 3. Aligning words and computing word-timing errors...");
            var errors = new List<double>();
            int wIdx = 0;

            foreach (var tw in ttsWords)
            {
                // Find matching word in Whisper outputs around current position
                int bestMatch = -1;
                double minTimeDelta = 2.0; // max 2.0s search window

                for (int j = Math.Max(0, wIdx - 5); j < Math.Min(whisperWords.Count, wIdx + 15); j++)
                {
                    if (string.Equals(tw.Word, whisperWords[j].Word, StringComparison.OrdinalIgnoreCase))
                    {
                        double delta = Math.Abs(whisperWords[j].StartSec - tw.TimestampSec);
                        if (delta < minTimeDelta)
                        {
                            minTimeDelta = delta;
                            bestMatch = j;
                        }
                    }
                }

                if (bestMatch >= 0)
                {
                    errors.Add(minTimeDelta * 1000.0); // convert to ms
                    wIdx = bestMatch + 1;
                }
            }

            result.MatchedWordCount = errors.Count;
            if (errors.Count > 0)
            {
                errors.Sort();
                result.MedianErrorMs = errors[errors.Count / 2];
                result.P95ErrorMs = errors[(int)(errors.Count * 0.95)];
                result.MaxErrorMs = errors[errors.Count - 1];
            }

            Console.WriteLine($"[WhisperProbe] Matched words: {result.MatchedWordCount}/{result.TtsWordCount} ({(double)result.MatchedWordCount / result.TtsWordCount * 100.0:F1}%)");
            Console.WriteLine($"[WhisperProbe] Word-timing error: Median={result.MedianErrorMs:F1} ms, P95={result.P95ErrorMs:F1} ms, Max={result.MaxErrorMs:F1} ms");

            // Pass criteria per spec: within 300 ms word-timing error (median) and faster than real time on CPU (RTF < 1.0)
            bool timingPass = result.MedianErrorMs <= 300.0;
            bool rtfPass = result.RealTimeFactor < 1.0;
            result.Pass = timingPass && rtfPass;

            if (!result.Pass)
            {
                result.FallbackTriggered = result.RealTimeFactor >= 1.0 
                    ? "Whisper base too slow on CPU -> ship tiny q5 as default and base as optional import"
                    : "Word timing error exceeded 300 ms";
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Pass = false;
            result.ErrorMessage = ex.Message;
            result.FallbackTriggered = "Whisper failed: " + ex.Message;
            return result;
        }
        finally
        {
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
        }
    }

    private static string CleanWord(string? w)
    {
        if (string.IsNullOrWhiteSpace(w)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in w)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string Generate5MinuteNarration()
    {
        // 5 minutes narration text at ~140 words per minute = ~700 words.
        // Structured technical narration about Lightshot architecture and computer vision.
        return @"Welcome to the Lightshot architecture and performance demonstration.
Today we are testing the modern Windows port of Lightshot, built on top of dot net ten and high performance native APIs.
In modern screen capture applications, speed and reliability are essential requirements for desktop users.
When a user presses the Print Screen key on their keyboard, the application must immediately capture the full desktop screen without introducing visual artifacts.
On modern systems with multiple monitors, each display may operate at different refresh rates, distinct DPI scaling factors, and wide color gamuts.
Windows eleven introduced enhanced display duplication APIs and modern graphic capture interfaces that deliver low latency video frames directly from the graphics processing unit.
The desktop duplication API allows the application to capture the active display surface into a DirectX eleven texture in less than twenty milliseconds.
By coordinating with the Desktop Window Manager, the overlay window appears instantly without visible flickering or latency spikes.
Furthermore, the application incorporates a flexible media recording pipeline capable of encoding high resolution video at sixty frames per second.
Media Foundation hardware transforms utilize dedicated silicon on modern graphics cards, such as NVIDIA NVENC and Intel QuickSync, ensuring minimal CPU utilization during active screen recording sessions.
In addition to audio and video recording, intelligent productivity tools demand robust local machine learning capabilities.
Traditional applications often transmit sensitive screenshots to remote cloud servers for analysis, which introduces serious privacy vulnerabilities and network latency bottlenecks.
Lightshot takes a fundamentally different architectural path by executing all artificial intelligence workloads directly on the local machine.
Local execution guarantees absolute privacy for confidential enterprise documents, personal chat messages, financial credentials, and proprietary code.
Optical character recognition is performed on device using hardware accelerated platform APIs, achieving rapid text recognition across various typefaces and font sizes.
Automatic redaction features scan captured screenshots for sensitive patterns such as credit card numbers, passwords, API tokens, email addresses, and server hostnames.
When detected, sensitive regions can be blurred or blacked out with a single click before the screenshot is copied to the clipboard or shared with colleagues.
To protect personal privacy in public environments, the integrated face detection pipeline identifies human faces in captured stills using lightweight neural networks.
The neural network evaluates multi scale feature representations in only a few milliseconds, ensuring the user interface remains completely responsive throughout the workflow.
Voice narration and captioning features leverage compact quantized transformer models that transcribe spoken audio with precise millisecond timestamps.
This allows content creators, software engineers, and remote teams to record instructional walk-throughs and generate synchronized captions entirely offline without subscription fees.
Translation models based on Marian architectures provide accurate bilingual translations between English and Vietnamese, enabling effortless cross language communication.
Every component has been engineered to minimize memory consumption, preserve battery life on laptop computers, and eliminate external dependencies.
Thank you for participating in this evaluation of high performance desktop technology and on device intelligence.";
    }
}
