using System.IO;
using System.Media;
using NAudio.Vorbis;
using NAudio.Wave;

namespace RebootWhy.App.Platform;

/// <summary>Sound signal: a custom mp3/wav/ogg file or the Windows system sound. A custom file can be stopped.</summary>
public sealed class AlertSound : IDisposable
{
    public static string FileFilter => Properties.Strings.Sound_FileFilter;

    private WaveOut? _output;
    private WaveStream? _reader;

    /// <summary>The sound started or stopped playing. Raised on the thread that started it (the UI thread).</summary>
    public event Action? StateChanged;

    public bool IsPlaying => _output is { PlaybackState: PlaybackState.Playing };

    /// <returns>The error text if the file could not be played (the system sound plays instead), otherwise null.</returns>
    public string? Play(string? file)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(file))
        {
            SystemSounds.Exclamation.Play();
            return null;
        }

        try
        {
            _reader = Path.GetExtension(file).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                ? new VorbisWaveReader(file)
                : new AudioFileReader(file);
            var output = new WaveOut();
            output.Init(_reader);
            output.PlaybackStopped += (_, _) =>
            {
                // Finished on its own: release the file and report that the "Mute" button can be hidden.
                if (ReferenceEquals(output, _output))
                {
                    Release();
                    StateChanged?.Invoke();
                }
            };
            _output = output;
            _output.Play();
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            // Decoders throw all kinds of exceptions on broken or unsupported files; the user must hear a signal anyway.
            Release();
            SystemSounds.Exclamation.Play();
            return ex.Message;
        }
    }

    public void Stop()
    {
        var wasPlaying = _output is not null;
        Release();
        if (wasPlaying)
        {
            StateChanged?.Invoke();
        }
    }

    public void Dispose() => Release();

    private void Release()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        output?.Dispose();
        reader?.Dispose();
    }
}
