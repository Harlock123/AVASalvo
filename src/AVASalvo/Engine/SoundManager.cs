using AVASalvo.Audio;

namespace AVASalvo.Engine;

/// <summary>
/// Plays the game's sounds through OpenAL Soft, which mixes as many at once as the game needs. The title tune plays
/// on the music channel. In a virtual machine a larger mixing buffer keeps the sound from breaking up.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly string _folder;

    public SoundManager(bool silent = false)
    {
        _folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        _engine = silent
            ? new NullAudioEngine("silent")
            : OpenAlAudioEngine.CreateOrSilent(AudioBuffer.PeriodFrames(AudioBuffer.Automatic, VirtualMachine.Detect()));
        _engine.SetVolumes(0.9f, 0.8f, 0.7f);
    }

    public bool IsAvailable => _engine.IsAvailable;

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) _engine.StopMusic();
        }
    }
    private bool _muted;

    private string File(Sfx s) => Path.Combine(_folder, s + ".wav");

    public void Play(Sfx sfx)
    {
        if (_muted) return;
        switch (sfx)
        {
            case Sfx.TitleTune: _engine.PlayMusic(File(sfx), loop: true); return;
            case Sfx.MatchOver: _engine.PlayMusic(File(sfx), loop: false); return;
            case Sfx.Turn or Sfx.Fire or Sfx.FireHeavy or Sfx.Split or Sfx.Dig: _engine.StopMusic(); break;
        }
        _engine.PlayEffect(File(sfx), sfx switch { Sfx.Select or Sfx.Weapon => 0.4f, Sfx.Hit => 0.7f, _ => 1f });
    }

    public void Dispose() => _engine.Dispose();
}
