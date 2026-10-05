using System;
using System.IO;
using System.Threading;

namespace Lightshot.Platform.Windows.Recording;

public record FinalizeResult(
    bool Success,
    string? DeliveredFilePath = null,
    string? RecoverableTakePath = null,
    string? ErrorMessage = null,
    int Attempts = 1
);

/// <summary>
/// Finalizes a fragmented recording take into a progressive MP4.
/// Implements validation and retry-once logic; keeps the take on permanent failure
/// and never delivers an unverified or fragmented file.
/// </summary>
public sealed class TakeFinalizer
{
    private readonly IRemuxer _remuxer;
    private readonly Action<TimeSpan> _delay;
    private readonly Func<string, string, bool> _validator;

    public TakeFinalizer(
        IRemuxer remuxer,
        Action<TimeSpan>? delay = null,
        Func<string, string, bool>? validator = null)
    {
        _remuxer = remuxer ?? throw new ArgumentNullException(nameof(remuxer));
        _delay = delay ?? (ts => Thread.Sleep(ts));
        _validator = validator ?? ((take, remux) => Mp4Remuxer.ValidateRemux(take, remux));
    }

    public FinalizeResult FinalizeTake(string takePath, string destinationMp4Path)
    {
        if (!File.Exists(takePath))
        {
            return new FinalizeResult(false, ErrorMessage: $"Take file does not exist: {takePath}", Attempts: 0);
        }

        string partialPath = destinationMp4Path + ".partial";
        string? destDir = Path.GetDirectoryName(destinationMp4Path);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }

            var remuxResult = _remuxer.Remux(takePath, partialPath);
            bool isValid = remuxResult.Success && File.Exists(partialPath) && _validator(takePath, partialPath);

            if (isValid)
            {
                try
                {
                    if (File.Exists(destinationMp4Path))
                    {
                        File.Delete(destinationMp4Path);
                    }
                    File.Move(partialPath, destinationMp4Path);

                    // Successfully finalized and verified -> delete source take
                    try { File.Delete(takePath); } catch { }

                    return new FinalizeResult(true, DeliveredFilePath: destinationMp4Path, Attempts: attempt);
                }
                catch (Exception ex)
                {
                    if (File.Exists(partialPath))
                    {
                        try { File.Delete(partialPath); } catch { }
                    }

                    if (attempt == 1)
                    {
                        _delay(TimeSpan.FromSeconds(2));
                        continue;
                    }

                    return new FinalizeResult(
                        false,
                        RecoverableTakePath: takePath,
                        ErrorMessage: $"Recording saved but could not be finalised: {ex.Message}",
                        Attempts: 2);
                }
            }

            // Remux or validation failed: clean up partial file
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }

            if (attempt == 1)
            {
                _delay(TimeSpan.FromSeconds(2));
            }
        }

        // On the second failure: take is preserved, partial deleted, nothing delivered
        return new FinalizeResult(
            false,
            RecoverableTakePath: takePath,
            ErrorMessage: "Recording saved but could not be finalised",
            Attempts: 2);
    }
}
