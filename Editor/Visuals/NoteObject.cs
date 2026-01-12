using Godot;
using System;
using RhythmBeatmapEditor.Core.Models;
using ObjectPool;

public partial class NoteObject : Control, IPoolable
{
    public event Action<NoteObject, InputEvent> OnInput;

    public NoteEvent Data { get; private set; }
    
    private ColorRect _visualRect;
    public bool IsSelected { get; private set; }

    public override void _Ready()
    {
        // One-time setup
        _visualRect = new ColorRect();
        _visualRect.SetAnchorsPreset(LayoutPreset.FullRect);
        _visualRect.MouseFilter = MouseFilterEnum.Ignore; // Let Parent (this) catch input
        AddChild(_visualRect);

        MouseFilter = MouseFilterEnum.Stop; // Stop propagation, handle input
    }

    // IPoolable: Reset state when reused
    public void OnSpawned() 
    {
        // Reset transform or effects if needed
        Modulate = Colors.White;
        SetSelectState(false);
    }

    public void OnDespawned() 
    {
        // Clear references
        Data = null;
        // Clear all subscribers to prevent memory leaks!
        OnInput = null; 
    }

    public void Bind(NoteEvent data, Color color)
    {
        Data = data;
        _visualRect.Color = color;
        // TooltipText only updates on mouse hover, might not want to spam string allocs here
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Invoke the C# event
        OnInput?.Invoke(this, @event);
    }

    public void SetSelectState(bool selected)
    {
        IsSelected = selected;
        // Visual feedback
        _visualRect.Color = IsSelected ? Colors.White : _visualRect.Color; 
    }
}