using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Editor;
using RhythmBeatmapEditor.Core.Models;

namespace RhythmBeatmapEditor.Editor.Visuals;

public partial class TimelineView : Control
{
    [Export] public EditorContext Context;
    
    private List<NoteNode> _activeNotes = new();
    
    // We need to know if we need to rebuild
    private bool _dirty = false;

    public override void _Ready()
    {
        if (Context == null)
        {
            // Try to find it if not assigned
            Context = GetNodeOrNull<EditorContext>("/root/EditorContext"); // Assuming Autoload or set manually
        }
        
        if (Context != null)
        {
            Context.BeatmapLoaded += OnBeatmapLoaded;
        }
    }

    private void OnBeatmapLoaded()
    {
        CallDeferred(nameof(RebuildVisuals));
    }
    
    private void RebuildVisuals()
    {
        // Clear existing
        foreach(var n in _activeNotes) n.QueueFree();
        _activeNotes.Clear();
        
        if (Context?.CurrentBeatmap == null) return;

        // Spawn all notes (Prototype approach - simple but effective for <2000 notes)
        // Optimization: Object Pooling or Windowed spawning based on time.
        foreach (var noteData in Context.CurrentBeatmap.Notes)
        {
            var noteNode = new NoteNode();
            noteNode.Setup(noteData, Context);
            AddChild(noteNode);
            _activeNotes.Add(noteNode);
        }
    }

    public override void _Process(double delta)
    {
        // Logic handled inside NoteNode for now (Position updates)
        // Global Grid rendering could happen here via _Draw()
    }
    
    public override void _Draw()
    {
        // Optional: Draw Grid Lines
        if (Context == null) return;
        
        float hitLineY = Size.Y - 100f;
        float totalWidth = 400f;
        float startX = (Size.X - totalWidth) / 2f;
        
        // Draw Hit Line
        DrawLine(new Vector2(startX - 20, hitLineY), new Vector2(startX + totalWidth + 20, hitLineY), Colors.Green, 2.0f);
        
        // Draw Lane Dividers
        for(int i=0; i<=4; i++)
        {
            float lx = startX + i * (totalWidth/4f);
            DrawLine(new Vector2(lx, 0), new Vector2(lx, Size.Y), new Color(1,1,1,0.2f), 1.0f);
        }
    }
}
