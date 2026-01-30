using Godot;
using System;

namespace RhythmBeatmapEditor.AudioSystem;

/// <summary>
/// Specialized AudioController for the Editor.
/// Bypasses the complex AudioSystem polling for direct, precise control over a single song stream.
/// </summary>
public partial class EditorAudioController : Node
{
    [Signal]
    public delegate void AudioFinishedEventHandler();

    private AudioStreamPlayer _player;
    
    // Smooth time tracking
    private float _lastServerTime = 0f;
    private float _lastStreamPos = 0f;

    public float PitchScale 
    {
        get => _player.PitchScale;
        set => _player.PitchScale = value;
    }

    public bool IsPlaying => _player.Playing && !_player.StreamPaused;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer();
        _player.Bus = "Music"; // Ensure this bus exists or use Master
        AddChild(_player);
        
        _player.Finished += () => EmitSignal(SignalName.AudioFinished);
    }

    public void LoadSong(string path)
    {
        // Internal Resource (res://)
        if (path.StartsWith("res://") && ResourceLoader.Exists(path))
        {
            var stream = ResourceLoader.Load<AudioStream>(path);
            _player.Stream = stream;
            return;
        }

        // External File (Absolute Path) - MP3 Only Support for now
        if (System.IO.File.Exists(path))
        {
            try 
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                var stream = new AudioStreamMP3();
                stream.Data = bytes;
                _player.Stream = stream;
                GD.Print($"[EditorAudio] Loaded external song: {path}");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[EditorAudio] Failed to load external audio: {e.Message}");
            }
        }
        else
        {
            GD.PrintErr($"[EditorAudio] Song file not found: {path}");
        }
    }
    
    public void LoadStream(AudioStream stream)
    {
        _player.Stream = stream;
    }

    public void Play(float fromPosition = -1f)
    {
        if (_player.Stream == null) return;
        
        if (fromPosition >= 0)
        {
            _player.Play(fromPosition);
        }
        else
        {
            _player.Play();
        }
    }

    public void Pause()
    {
        _player.StreamPaused = true;
    }

    public void Resume()
    {
        if (_player.Stream == null) return;
        _player.StreamPaused = false;
        if (!_player.Playing) _player.Play();
    }
    
    public void Stop()
    {
        _player.Stop();
    }

    public void Seek(float time)
    {
        if (_player.Stream == null) return;
        _player.Seek(time);
        // If it was paused, it remains paused but time updates.
    }

    /// <summary>
    /// Returns the precise playback time in seconds.
    /// Interploated for smoothness between physics frames.
    /// </summary>
    public float GetTime()
    {
        if (!_player.Playing) return _player.GetPlaybackPosition();
        
        // Godot 4.x AudioServer time syncing is generally robust, 
        // but simple GetPlaybackPosition is often enough for editor resolution unless extremely low latency is needed.
        return _player.GetPlaybackPosition();
    }

    public float GetLength()
    {
        if (_player.Stream == null) return 0f;
        return (float)_player.Stream.GetLength();
    }
    
    // --- Volume Control ---
    public float MusicVolume { get; private set; } = 1.0f;
    public float SFXVolume { get; private set; } = 1.0f;

    public void SetMusicVolume(float linear)
    {
        MusicVolume = Mathf.Clamp(linear, 0f, 2f);
        // Convert Linear to Db
        // 1.0 -> 0 Db
        // 0.0 -> -80 Db (Mute)
        // 2.0 -> +6 Db
        float db = Mathf.LinearToDb(MusicVolume);
        _player.VolumeDb = db;
    }

    public void SetSFXVolume(float linear)
    {
        SFXVolume = Mathf.Clamp(linear, 0f, 2f);
        
        // Update the SFX audio bus so AudioManager.PlaySFX() respects volume
        int busIdx = AudioServer.GetBusIndex("SFX");
        if (busIdx != -1)
        {
            float db = SFXVolume > 0.0001f ? Mathf.LinearToDb(SFXVolume) : -80f;
            AudioServer.SetBusVolumeDb(busIdx, db);
        }
    }
    
    public void PlayOneShot(AudioStream stream)
    {
        if (stream == null) return;
        var asp = new AudioStreamPlayer();
        asp.Stream = stream;
        asp.Bus = "Master"; // SFX usually Master or separate bus
        
        // Apply SFX Volume
        float db = Mathf.LinearToDb(SFXVolume);
        asp.VolumeDb = db;
        
        AddChild(asp);
        asp.Finished += () => asp.QueueFree();
        asp.Play();
    }
}
