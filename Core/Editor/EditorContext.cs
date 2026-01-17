using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Models;
using RhythmBeatmapEditor.AudioSystem;
using System.Text.Json;
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
    [Signal] public delegate void ModeChangedEventHandler(bool isEditMode); 
    
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
    [Export] public int MaxLanes { get; set; } = 4; // Configurable lane limit
    
    // Components
    public EditorSelection Selection { get; private set; }
    public EditorHistory History { get; private set; }
    
    // Facade Properties
    public IReadOnlyCollection<NoteEvent> SelectedNotes => Selection.SelectedNotes;
    
    // Clipboard
    private List<NoteEvent> _clipboard = new();
    
    public event System.Action OnSelectionChanged;
    
    #endregion

    public override void _Ready()
    {
        // Initialize sub-systems
        AudioController = new EditorAudioController();
        AddChild(AudioController);
        
        Selection = new EditorSelection { Name = "EditorSelection" };
        AddChild(Selection);
        Selection.OnSelectionChanged += () => OnSelectionChanged?.Invoke();
        
        History = new EditorHistory { Name = "EditorHistory" };
        AddChild(History);
        History.OnHistoryChanged += () => OnSelectionChanged?.Invoke(); // Refresh UI on history undo
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

    #region API - Selection (Facade)
    
    public void SelectNote(NoteEvent note, bool exclusive = true) => Selection.Select(note, exclusive);
    public void DeselectNote(NoteEvent note) => Selection.Deselect(note);
    public void ToggleSelection(NoteEvent note) => Selection.Toggle(note);
    public void ClearSelection() => Selection.Clear();
    public bool IsSelected(NoteEvent note) => Selection.IsSelected(note);
    public void RefreshSelectionUI() => Selection.NotifyChanged();
    public void HandleMarqueeSelection(IEnumerable<NoteEvent> targetedNotes) => Selection.HandleMarquee(targetedNotes);
    
    #endregion

    // Edit Session (Facade)
    // private Dictionary<NoteEvent, NoteEvent> _originalSnapshot = new(); // Removed
    
    public void CaptureSnapshot(IEnumerable<NoteEvent> notes) => History.CaptureSnapshot(notes);
    public NoteEvent GetOriginal(NoteEvent note) => History.GetOriginal(note);
    public IEnumerable<NoteEvent> GetDirtyNotes() => History.ModifiedNotes;
    public void CommitNotes(IEnumerable<NoteEvent> notes) => History.CommitSpecificInput(notes);
    public void CommitEdits() => History.CommitEdits();
    
    // Revert ALL Changes
    public void CancelEdit()
    {
        if (History.CancelEdit(CurrentBeatmap))
        {
            EmitSignal(SignalName.BeatmapLoaded);
        }
    }
    
    // Revert Specific Notes
    public void RevertEdits(IEnumerable<NoteEvent> targets)
    {
        History.RevertEdits(targets, CurrentBeatmap);
        EmitSignal(SignalName.BeatmapLoaded);
    }
    
    #region API - Editing Actions

    public void DeleteSelectedNotes()
    {
        if (Selection.SelectedNotes.Count == 0) return;
        
        CaptureSnapshot(Selection.SelectedNotes);
        
        foreach(var note in Selection.SelectedNotes)
        {
            note.State = NoteEvent.NoteState.Deleted;
        }
        
        RefreshSelectionUI();
    }
    
    public void AddNote(NoteEvent note)
    {
        CurrentBeatmap.Notes.Add(note);
        CurrentBeatmap.Sort();
        
        // Select the new note
        Selection.Select(note);
        EmitSignal(SignalName.BeatmapLoaded); // Full refresh to ensure visualisation
    }
    
    public void CopySelectedNotes()
    {
        if (Selection.SelectedNotes.Count == 0) return;
        
        _clipboard.Clear();
        
        // Find anchor (earliest note)
        float minTime = float.MaxValue;
        foreach(var n in Selection.SelectedNotes)
        {
            if (n.Time < minTime) minTime = n.Time;
        }
        
        foreach(var n in Selection.SelectedNotes)
        {
            var clone = new NoteEvent 
            { 
                Time = n.Time - minTime, // Relative Time
                Lane = n.Lane, 
                Duration = n.Duration,
                Pitch = n.Pitch,
                Source = n.Source,
                State = NoteEvent.NoteState.Normal 
            };
            _clipboard.Add(clone);
        }
        GD.Print($"[EditorContext] Copied {_clipboard.Count} notes.");
    }
    
    public void PasteNotes()
    {
        if (_clipboard.Count == 0) return;
        
        Selection.Clear();
        
        var newNotes = new List<NoteEvent>();
        foreach(var clip in _clipboard)
        {
            var note = new NoteEvent 
            {
                Time = PlaybackTime + clip.Time,
                Lane = clip.Lane,
                Duration = clip.Duration,
                Pitch = clip.Pitch,
                Source = clip.Source,
                State = NoteEvent.NoteState.Normal
            };
            
            // Clamp Lane
            note.Lane = Mathf.Clamp(note.Lane, 0, MaxLanes - 1);
            
            CurrentBeatmap.Notes.Add(note);
            newNotes.Add(note);
            Selection.Select(note, false); // Add to selection
        }
        
        CurrentBeatmap.Sort();
        EmitSignal(SignalName.BeatmapLoaded);
        RefreshSelectionUI();
        GD.Print($"[EditorContext] Pasted {newNotes.Count} notes.");
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
            EmitSignal(SignalName.ModeChanged, true);
        }
        else
        {
            // Exit Edit Mode -> KEEP Ghosts (Persistence)
            // Dirty notes stay Dirty until manually Applied.
            // This ensures visuals persist during playback.
            
            AudioController.Play(PlaybackTime);
            // AudioController.Resume(); // Replaced with Play(time) for robust sync
            EmitSignal(SignalName.ModeChanged, false);
        }
    }
    
    public void Seek(float time)
    {
        // GD.Print($"[Context] Seek Request: {time}");
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
