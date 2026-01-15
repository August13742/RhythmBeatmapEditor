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
    [Signal] public delegate void NoteUpdatedEventHandler(); 
    
    #endregion

    #region Dependencies
    public EditorAudioController AudioController { get; private set; }
    #endregion

    #region State
    public BeatmapData CurrentBeatmap { get; private set; } = new();
    
    // Playback state
    public float PlaybackTime { get; private set; } = 0f;
    public bool IsPlaying => AudioController != null && AudioController.IsPlaying;
    public bool IsEditMode => !IsPlaying; // Edit enabled when paused
    
    // Editor settings
    [Export] public float SnapInterval { get; set; } = 0.25f; // 1/4 note
    [Export] public float ScrollSpeed { get; set; } = 500.0f; // Pixels per second
    [Export] public float SnapPrecision { get; set; } = 0.05f; // User defined precision
    
    // Selection
    private HashSet<NoteEvent> _selectedNotes = new();
    public IReadOnlyCollection<NoteEvent> SelectedNotes => _selectedNotes;
    
    public event System.Action OnSelectionChanged;
    
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
            // Fix Seek Bug: Only update time from AudioController if it's actually playing.
            // If paused, we trust the PlaybackTime set by Seek().
            if (AudioController.IsPlaying)
            {
                float newTime = AudioController.GetTime();
                if (Mathf.Abs(newTime - PlaybackTime) > 0.0001f)
                {
                    PlaybackTime = newTime;
                    EmitSignal(SignalName.PlaybackTimeUpdated, PlaybackTime);
                }
            }
        }
    }

    #region API - Selection
    
    public void SelectNote(NoteEvent note, bool exclusive = true)
    {
        if (exclusive) 
        {
            _selectedNotes.Clear();
            _selectedNotes.Add(note);
        }
        else
        {
            _selectedNotes.Add(note);
        }
        OnSelectionChanged?.Invoke();
    }
    
    public void DeselectNote(NoteEvent note)
    {
        if (_selectedNotes.Remove(note))
        {
            OnSelectionChanged?.Invoke();
        }
    }
    
    public void ToggleSelection(NoteEvent note)
    {
        if (_selectedNotes.Contains(note)) _selectedNotes.Remove(note);
        else _selectedNotes.Add(note);
        OnSelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (_selectedNotes.Count > 0)
        {
            _selectedNotes.Clear();
            OnSelectionChanged?.Invoke();
        }
    }
    
    public bool IsSelected(NoteEvent note) => _selectedNotes.Contains(note);
    
    public void RefreshSelectionUI()
    {
        OnSelectionChanged?.Invoke();
    }
    
    public void HandleMarqueeSelection(IEnumerable<NoteEvent> targetedNotes)
    {
        // "Add/Toggle" Logic:
        // 1. Identify New Notes (Targeted but NOT currently selected)
        // 2. If present, ADD them (Merge).
        // 3. If NO new notes (Targeting only existing selection), TOGGLE them OFF (Deselect).
        
        bool hasNew = false;
        foreach(var n in targetedNotes)
        {
            if (!_selectedNotes.Contains(n))
            {
                hasNew = true;
                break;
            }
        }
        
        bool changed = false;
        if (hasNew)
        {
            // Add All
            foreach(var n in targetedNotes)
            {
                if (_selectedNotes.Add(n)) changed = true;
            }
        }
        else
        {
            // Toggle Off (Deselect)
            foreach(var n in targetedNotes)
            {
                if (_selectedNotes.Remove(n)) changed = true;
            }
        }
        
        if (changed) OnSelectionChanged?.Invoke();
    }
    
    // Edit Session
    private Dictionary<NoteEvent, NoteEvent> _originalSnapshot = new();
    
    public void CaptureSnapshot(IEnumerable<NoteEvent> notes)
    {
        foreach(var note in notes)
        {
            if (!_originalSnapshot.ContainsKey(note))
            {
                // Create minimal copy (struct-like copy)
                // NoteEvent is a class, needs explicit Clone
                _originalSnapshot[note] = new NoteEvent 
                { 
                    Time = note.Time, 
                    Lane = note.Lane, 
                    Duration = note.Duration,
                    Pitch = note.Pitch,
                    Source = note.Source,
                    State = NoteEvent.NoteState.Normal // Snapshot is always Normal basis
                };
            }
            // Mark Current as Dirty
            note.State = NoteEvent.NoteState.Dirty;
        }
    }
    
    public NoteEvent GetOriginal(NoteEvent note)
    {
        if (_originalSnapshot.TryGetValue(note, out var original)) return original;
        return note; // If no snapshot, current is original
    }
    
    public void CommitEdits()
    {
        if (_originalSnapshot.Count > 0)
        {
            GD.Print($"[EditorContext] Committing edits for {_originalSnapshot.Count} notes.");
            _originalSnapshot.Clear();
            OnSelectionChanged?.Invoke(); // Refresh UI visuals (Ghosts disappear)
        }
    }
    
    // Revert ALL Changes
    public void CancelEdit()
    {
        if (_originalSnapshot.Count > 0)
        {
            GD.Print($"[EditorContext] Reverting ALL ({_originalSnapshot.Count}) notes.");
            foreach(var kvp in _originalSnapshot)
            {
                RestoreNoteState(kvp.Key, kvp.Value);
            }
            _originalSnapshot.Clear();
            CurrentBeatmap.Sort(); 
            OnSelectionChanged?.Invoke();
            EmitSignal(SignalName.BeatmapLoaded); 
        }
    }
    
    // Revert Specific Notes
    public void RevertEdits(IEnumerable<NoteEvent> targets)
    {
        bool changed = false;
        foreach(var note in targets)
        {
            if (_originalSnapshot.TryGetValue(note, out var original))
            {
                RestoreNoteState(note, original);
                _originalSnapshot.Remove(note);
                changed = true;
            }
        }
        
        if (changed)
        {
            CurrentBeatmap.Sort();
            OnSelectionChanged?.Invoke();
            EmitSignal(SignalName.BeatmapLoaded);
        }
    }
    
    private void RestoreNoteState(NoteEvent target, NoteEvent source)
    {
        target.Time = source.Time;
        target.Lane = source.Lane;
        target.Duration = source.Duration;
        target.Pitch = source.Pitch;
        target.Source = source.Source;
        target.State = NoteEvent.NoteState.Normal; // Reset to Normal
    }

    #endregion

    #region API - General
    
    public void LoadBeatmapJSON(string jsonContent)
    {
        CancelEdit(); // Clear any pending edits
        try 
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            BeatmapData data = JsonSerializer.Deserialize<BeatmapData>(jsonContent, options);
            
            if (data != null)
            {
                CurrentBeatmap = data;
                CurrentBeatmap.Sort(); // Ensure sorted
                ClearSelection();
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
        {
            AudioController.Pause();
            // Enter Edit Mode implicitly
        }
        else
        {
            // Exit Edit Mode -> KEEP Ghosts (Persistence)
            // Dirty notes stay Dirty until manually Applied.
            // This ensures visuals persist during playback.
            
            ClearSelection();
            AudioController.Resume();
        }
    }
    
    public void Seek(float time)
    {
        AudioController.Seek(time);
        PlaybackTime = time;
        EmitSignal(SignalName.PlaybackTimeUpdated, PlaybackTime);
    }
    
    /// <summary>
    /// Snaps a time value to the nearest grid interval based on BPM and SnapInterval.
    /// Also respects User Precision (e.g. 0.05s).
    /// </summary>
    public float SnapTime(float time)
    {
        // 1. BPM Grid Snap
        if (CurrentBeatmap.BPM > 0)
        {
            float secondsPerBeat = 60.0f / CurrentBeatmap.BPM;
            float intervalSeconds = secondsPerBeat * SnapInterval; // e.g. 0.5s * 0.25 = 0.125s
            
            if (intervalSeconds > 0.001f)
            {
                time = Mathf.Round(time / intervalSeconds) * intervalSeconds;
            }
        }
        
        // 2. Hard Precision Snap (0.05s)
        if (SnapPrecision > 0)
        {
             time = Mathf.Round(time / SnapPrecision) * SnapPrecision;
        }

        return time;
    }

    #endregion
}
