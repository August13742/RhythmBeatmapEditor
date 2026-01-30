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
            
            // Mark the owning map as dirty
            var map = GetMapForNote(note);
            map?.MarkDirty();
        }
        
        RefreshSelectionUI();
    }
    
    public void AddNote(NoteEvent note)
    {
        // Tag with active map key
        note.MapKey = ActiveMapKey;
        
        CurrentBeatmap.Notes.Add(note);
        CurrentBeatmap.Sort();
        CurrentBeatmap.MarkDirty();
        
        // Select the new note
        Selection.Select(note);
        EmitSignal(SignalName.BeatmapLoaded); // Full refresh to ensure visualisation
    }
    
    /// <summary>
    /// Add note to a specific map (for multi-map mode)
    /// </summary>
    public void AddNoteToMap(NoteEvent note, string mapKey)
    {
        if (!LoadedMaps.TryGetValue(mapKey, out var map)) return;
        
        note.MapKey = mapKey;
        map.Notes.Add(note);
        map.Sort();
        map.MarkDirty();
        
        Selection.Select(note);
        EmitSignal(SignalName.BeatmapLoaded);
    }
    
    /// <summary>
    /// Get the beatmap that owns a given note (via MapKey)
    /// </summary>
    public BeatmapData GetMapForNote(NoteEvent note)
    {
        if (!string.IsNullOrEmpty(note.MapKey) && LoadedMaps.TryGetValue(note.MapKey, out var map))
            return map;
        return CurrentBeatmap; // Fallback to active map
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
                State = NoteEvent.NoteState.Normal,
                MapKey = ActiveMapKey // Paste to active map
            };
            
            // Clamp Lane to active map's lane count
            int maxLane = CurrentBeatmap.LaneCount - 1;
            note.Lane = Mathf.Clamp(note.Lane, 0, maxLane);
            
            CurrentBeatmap.Notes.Add(note);
            newNotes.Add(note);
            Selection.Select(note, false); // Add to selection
        }
        
        CurrentBeatmap.Sort();
        CurrentBeatmap.MarkDirty();
        EmitSignal(SignalName.BeatmapLoaded);
        RefreshSelectionUI();
        GD.Print($"[EditorContext] Pasted {newNotes.Count} notes to '{ActiveMapKey}'.");
    }

    #endregion
    
    #region API - General
    
    public void LoadBeatmapJSON(string jsonContent, string mapKey = null, string sourcePath = null)
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
                
                // Set runtime tracking properties
                data.MapKey = mapKey;
                data.SourcePath = sourcePath ?? "";
                data.IsEdited = sourcePath?.EndsWith("_edited.json") ?? false;
                data.IsDirty = false;
                
                // Tag all notes with their map key
                foreach (var note in data.Notes)
                {
                    note.MapKey = mapKey;
                }
                
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
    /// Load multiple beatmaps for comparison mode.
    /// mapData: Dictionary of mapKey -> (json, sourcePath)
    /// </summary>
    public void LoadMultipleBeatmaps(Dictionary<string, (string json, string path)> mapData)
    {
        CancelEdit();
        LoadedMaps.Clear();
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
        
        foreach (var kvp in mapData)
        {
            try
            {
                BeatmapData data = JsonSerializer.Deserialize<BeatmapData>(kvp.Value.json, options);
                if (data != null)
                {
                    data.Sort();
                    
                    // Set runtime tracking properties
                    data.MapKey = kvp.Key;
                    data.SourcePath = kvp.Value.path;
                    data.IsEdited = kvp.Value.path?.EndsWith("_edited.json") ?? false;
                    data.IsDirty = false;
                    
                    // Tag all notes with their map key
                    foreach (var note in data.Notes)
                    {
                        note.MapKey = kvp.Key;
                    }
                    
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
    
    #region API - Save
    
    /// <summary>
    /// Save the active beatmap to an _edited.json file.
    /// </summary>
    public bool SaveActiveMap()
    {
        return SaveMap(ActiveMapKey);
    }
    
    /// <summary>
    /// Save a specific beatmap by key.
    /// </summary>
    public bool SaveMap(string mapKey)
    {
        if (!LoadedMaps.TryGetValue(mapKey, out var map))
        {
            GD.PrintErr($"[EditorContext] Cannot save: map '{mapKey}' not found.");
            return false;
        }
        
        if (!map.IsDirty)
        {
            GD.Print($"[EditorContext] Map '{mapKey}' has no changes to save.");
            return true;
        }
        
        // Build save path: replace .json with _edited.json
        string savePath = GetEditedPath(map.SourcePath);
        
        if (string.IsNullOrEmpty(savePath))
        {
            GD.PrintErr($"[EditorContext] Cannot determine save path for '{mapKey}'.");
            return false;
        }
        
        try
        {
            // Filter out deleted notes before saving
            var notesToSave = new List<NoteEvent>();
            foreach (var note in map.Notes)
            {
                if (note.State != NoteEvent.NoteState.Deleted)
                {
                    notesToSave.Add(note);
                }
            }
            
            // Create save data with cleaned notes
            var saveData = new BeatmapData
            {
                Metadata = map.Metadata,
                Notes = notesToSave,
                BPM = map.BPM
            };
            
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            string json = JsonSerializer.Serialize(saveData, options);
            
            // Write to file
            using var file = Godot.FileAccess.Open(savePath, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PrintErr($"[EditorContext] Failed to open file for writing: {savePath}");
                return false;
            }
            
            file.StoreString(json);
            file.Close();
            
            // Update state
            map.ClearDirty();
            map.IsEdited = true;
            map.SourcePath = savePath;
            
            GD.Print($"[EditorContext] Saved map '{mapKey}' to: {savePath}");
            return true;
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[EditorContext] Save failed: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Save all dirty maps.
    /// </summary>
    public int SaveAllDirtyMaps()
    {
        int saved = 0;
        foreach (var mapKey in LoadedMaps.Keys)
        {
            if (LoadedMaps[mapKey].IsDirty && SaveMap(mapKey))
            {
                saved++;
            }
        }
        return saved;
    }
    
    /// <summary>
    /// Check if any loaded map has unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges()
    {
        foreach (var map in LoadedMaps.Values)
        {
            if (map.IsDirty) return true;
        }
        return false;
    }
    
    /// <summary>
    /// Gets the _edited.json path for a source path.
    /// </summary>
    private string GetEditedPath(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return "";
        
        // If already _edited, keep it
        if (sourcePath.EndsWith("_edited.json"))
            return sourcePath;
            
        // Replace .json with _edited.json
        if (sourcePath.EndsWith(".json"))
            return sourcePath.Replace(".json", "_edited.json");
            
        return sourcePath + "_edited.json";
    }
    
    #endregion
}
