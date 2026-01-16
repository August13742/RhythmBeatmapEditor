using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Models;

namespace RhythmBeatmapEditor.Core.Editor;

public partial class EditorSelection : Node
{
    private HashSet<NoteEvent> _selectedNotes = new();
    public IReadOnlyCollection<NoteEvent> SelectedNotes => _selectedNotes;
    
    public event System.Action OnSelectionChanged;
    
    public void Select(NoteEvent note, bool exclusive = true)
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
    
    public void Deselect(NoteEvent note)
    {
        if (_selectedNotes.Remove(note))
        {
            OnSelectionChanged?.Invoke();
        }
    }
    
    public void Toggle(NoteEvent note)
    {
        if (_selectedNotes.Contains(note)) _selectedNotes.Remove(note);
        else _selectedNotes.Add(note);
        OnSelectionChanged?.Invoke();
    }

    public void Clear()
    {
        if (_selectedNotes.Count > 0)
        {
            _selectedNotes.Clear();
            OnSelectionChanged?.Invoke();
        }
    }
    
    public bool IsSelected(NoteEvent note) => _selectedNotes.Contains(note);
    
    public void NotifyChanged()
    {
        OnSelectionChanged?.Invoke();
    }
    
    public void HandleMarquee(IEnumerable<NoteEvent> targetedNotes)
    {
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
            foreach(var n in targetedNotes)
            {
                if (_selectedNotes.Add(n)) changed = true;
            }
        }
        else
        {
            foreach(var n in targetedNotes)
            {
                if (_selectedNotes.Remove(n)) changed = true;
            }
        }
        
        if (changed) OnSelectionChanged?.Invoke();
    }
}
