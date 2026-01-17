using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Models;

namespace RhythmBeatmapEditor.Core.Editor;

public partial class EditorHistory : Node
{
    private Dictionary<NoteEvent, NoteEvent> _originalSnapshot = new();
    
    public event System.Action OnHistoryChanged;

    public void CaptureSnapshot(IEnumerable<NoteEvent> notes)
    {
        foreach(var note in notes)
        {
            if (!_originalSnapshot.ContainsKey(note))
            {
                _originalSnapshot[note] = new NoteEvent 
                { 
                    Time = note.Time, 
                    Lane = note.Lane, 
                    Duration = note.Duration,
                    Pitch = note.Pitch,
                    Source = note.Source,
                    State = NoteEvent.NoteState.Normal
                };
            }
            note.State = NoteEvent.NoteState.Dirty;
        }
    }
    
    public NoteEvent GetOriginal(NoteEvent note)
    {
        if (_originalSnapshot.TryGetValue(note, out var original)) return original;
        return note;
    }
    
    public void CommitEdits()
    {
        if (_originalSnapshot.Count > 0)
        {
            GD.Print($"[EditorHistory] Committing edits for {_originalSnapshot.Count} notes.");
            _originalSnapshot.Clear();
            OnHistoryChanged?.Invoke();
        }
    }
    
    // Returns true if restoration happened
    public bool CancelEdit(BeatmapData currentMap)
    {
        if (_originalSnapshot.Count > 0)
        {
            GD.Print($"[EditorHistory] Reverting ALL ({_originalSnapshot.Count}) notes.");
            foreach(var kvp in _originalSnapshot)
            {
                RestoreNoteState(kvp.Key, kvp.Value);
            }
            _originalSnapshot.Clear();
            currentMap.Sort(); 
            OnHistoryChanged?.Invoke();
            return true;
        }
        return false;
    }
    
    public void RevertEdits(IEnumerable<NoteEvent> targets, BeatmapData currentMap)
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
            currentMap.Sort();
            OnHistoryChanged?.Invoke();
        }
    }
    
    private static void RestoreNoteState(NoteEvent target, NoteEvent source)
    {
        target.Time = source.Time;
        target.Lane = source.Lane;
        target.Duration = source.Duration;
        target.Pitch = source.Pitch;
        target.Source = source.Source;
        target.State = NoteEvent.NoteState.Normal;
    }
}
