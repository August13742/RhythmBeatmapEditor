using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Models;
using RhythmBeatmapEditor.AudioSystem;
using System.Text.Json; // For simple JSON parsing

namespace RhythmBeatmapEditor.Core.Editor;

/// <summary>
/// Global Context for the Editor.
/// Manages State, Data Loading, and Signals.
/// </summary>
public partial class EditorContext : Node
{
    #region Signals
    [Signal] public delegate void PlaybackTimeUpdatedEventHandler(float time);
    [Signal] public delegate void BeatmapLoadedEventHandler();
    [Signal] public delegate void NoteUpdatedEventHandler(); // Keeping it simple for now (full redraw or smart update)
    #endregion

    #region Dependencies
    public EditorAudioController AudioController { get; private set; }
    #endregion

    #region State
    public BeatmapData CurrentBeatmap { get; private set; } = new();
    
    // Playback state
    public float PlaybackTime { get; private set; } = 0f;
    public bool IsPlaying => AudioController != null && AudioController.IsPlaying;
    
    // Editor settings
    public float SnapInterval { get; set; } = 0.25f; // 1/4 note
    public float ScrollSpeed { get; set; } = 500.0f; // Pixels per second
    
    #endregion

    public override void _Ready()
    {
        // Initialize sub-systems
        AudioController = new EditorAudioController();
        AddChild(AudioController);
    }

    public override void _Process(double delta)
    {
        if (AudioController != null)
        {
            float newTime = AudioController.GetTime();
            if (Mathf.Abs(newTime - PlaybackTime) > 0.0001f)
            {
                PlaybackTime = newTime;
                EmitSignal(SignalName.PlaybackTimeUpdated, PlaybackTime);
            }
        }
    }

    #region API
    
    public void LoadBeatmapJSON(string jsonContent)
    {
        try 
        {
            // Simple deserialization using System.Text.Json
            // Note: Requires properties to be public
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            BeatmapData data = JsonSerializer.Deserialize<BeatmapData>(jsonContent, options);
            
            if (data != null)
            {
                CurrentBeatmap = data;
                CurrentBeatmap.Sort(); // Ensure sorted
                EmitSignal(SignalName.BeatmapLoaded);
                GD.Print($"[EditorContext] Loaded beatmap with {CurrentBeatmap.Notes.Count} notes.");
            }
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[EditorContext] Failed to load beatmap: {e.Message}");
        }
    }

    public void TogglePlay()
    {
        if (AudioController.IsPlaying)
            AudioController.Pause();
        else
            AudioController.Resume();
    }
    
    public void Seek(float time)
    {
        AudioController.Seek(time);
        PlaybackTime = time;
        EmitSignal(SignalName.PlaybackTimeUpdated, PlaybackTime);
    }
    
    /// <summary>
    /// Snaps a time value to the nearest grid interval based on BPM and SnapInterval.
    /// </summary>
    public float SnapTime(float time)
    {
        if (CurrentBeatmap.BPM <= 0) return time;
        
        float secondsPerBeat = 60.0f / CurrentBeatmap.BPM;
        float intervalSeconds = secondsPerBeat * SnapInterval; // e.g. 0.5s * 0.25 = 0.125s
        
        if (intervalSeconds <= 0.001f) return time;

        return Mathf.Round(time / intervalSeconds) * intervalSeconds;
    }

    #endregion
}
