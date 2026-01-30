using Godot;
using System.Collections.Generic;
using System.Linq;
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
    // Multi-map storage (keyed by "{DIFF}_{lanes}k", e.g. "HARD_4k")
    public Dictionary<string, BeatmapData> LoadedMaps { get; private set; } = new();
    public string ActiveMapKey { get; private set; } = "";
    
    // CurrentBeatmap is alias to active map (backwards compatible)
    public BeatmapData CurrentBeatmap => LoadedMaps.TryGetValue(ActiveMapKey, out var map) ? map : _fallbackMap;
    private BeatmapData _fallbackMap = new();
    
    // Multi-map mode
    public bool IsMultiMapMode => LoadedMaps.Count > 1;
    public int LoadedMapCount => LoadedMaps.Count;
    
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
    
    public void LoadBeatmapJSON(string jsonContent, string mapKey = null)
    {
        CancelEdit(); // Clear any pending edits
        try 
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            BeatmapData data = JsonSerializer.Deserialize<BeatmapData>(jsonContent, options);
            
            if (data != null)
            {
                // Generate key from metadata if not provided
                if (string.IsNullOrEmpty(mapKey))
                {
                    string diff = !string.IsNullOrEmpty(data.Metadata?.Difficulty) ? data.Metadata.Difficulty : "UNKNOWN";
                    int lanes = data.LaneCount;
                    mapKey = $"{diff}_{lanes}k";
                }
                
                data.Sort(); // Ensure sorted
                
                // If this is the first map or replacing single map, clear existing
                if (LoadedMaps.Count == 0 || !IsMultiMapMode)
                {
                    LoadedMaps.Clear();
                }
                
                LoadedMaps[mapKey] = data;
                ActiveMapKey = mapKey;
                
                ClearSelection();
                EmitSignal(SignalName.BeatmapLoaded);
                GD.Print($"[EditorContext] Loaded beatmap '{mapKey}' with {data.Notes.Count} notes. Total maps: {LoadedMaps.Count}");
            }
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[EditorContext] Failed to load beatmap: {e.Message}");
        }
    }
    
    /// <summary>
    /// Load multiple beatmaps for comparison mode
    /// </summary>
    public void LoadMultipleBeatmaps(Dictionary<string, string> mapJsons)
    {
        CancelEdit();
        LoadedMaps.Clear();
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
        
        foreach (var kvp in mapJsons)
        {
            try
            {
                BeatmapData data = JsonSerializer.Deserialize<BeatmapData>(kvp.Value, options);
                if (data != null)
                {
                    data.Sort();
                    LoadedMaps[kvp.Key] = data;
                    GD.Print($"[EditorContext] Loaded map '{kvp.Key}' with {data.Notes.Count} notes.");
                }
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"[EditorContext] Failed to load map '{kvp.Key}': {e.Message}");
            }
        }
        
        // Set first map as active
        if (LoadedMaps.Count > 0)
        {
            ActiveMapKey = LoadedMaps.Keys.First();
        }
        
        ClearSelection();
        EmitSignal(SignalName.BeatmapLoaded);
        GD.Print($"[EditorContext] Multi-map mode: {LoadedMaps.Count} maps loaded.");
    }
    
    /// <summary>
    /// Switch active map (for editing context)
    /// </summary>
    public void SetActiveMap(string mapKey)
    {
        if (LoadedMaps.ContainsKey(mapKey) && ActiveMapKey != mapKey)
        {
            ActiveMapKey = mapKey;
            ClearSelection();
            OnSelectionChanged?.Invoke();
            GD.Print($"[EditorContext] Active map switched to '{mapKey}'");
        }
    }
    
    /// <summary>
    /// Get beatmap by key (for multi-map rendering)
    /// </summary>
    public BeatmapData GetMap(string key) => LoadedMaps.TryGetValue(key, out var map) ? map : null;

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
