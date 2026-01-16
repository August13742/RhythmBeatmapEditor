using Godot;
using System;
using RhythmBeatmapEditor.Core.Models;
using ObjectPool;

public partial class NoteObject : Control, IPoolable
{
    public event Action<NoteObject, InputEvent> OnInput;
    public event Action<NoteObject, Vector2> OnDrag;
    public event Action<NoteObject> OnDragEnd;

    public NoteEvent Data { get; private set; }
    
    [Export] private Control _visualRect;
    [Export] private Label _lblDelta;
    
    public bool IsSelected { get; private set; }
    private Color _baseColor;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; 
    }
    
    public void OnSpawned() 
    {
        Modulate = Colors.White;
        SetSelectState(false);
        SetDeltaText(null);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void OnDespawned()
    {
        Data = null;
        OnInput = null;
        OnDrag = null;
        OnDragEnd = null;
        _baseColor = default;
    }

    public void Bind(NoteEvent data, Color color)
    {
        Data = data;
        _baseColor = color;
        if(_visualRect != null) _visualRect.SelfModulate = IsSelected ? Colors.White : _baseColor;
    }
    
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0)
        {
            OnDrag?.Invoke(this, mm.Relative);
        }
        
        if (@event is InputEventMouseButton mbRelease && !mbRelease.Pressed && mbRelease.ButtonIndex == MouseButton.Left)
        {
            OnDragEnd?.Invoke(this);
        }

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
             OnInput?.Invoke(this, @event);
        }
    }

    public void SetSelectState(bool selected)
    {
        IsSelected = selected;
        if(_visualRect != null) _visualRect.SelfModulate = IsSelected ? Colors.White : _baseColor;
    }
    
    public void SetDeltaText(string text)
    {
        if(_lblDelta == null) return;
        
        if (string.IsNullOrEmpty(text))
        {
            _lblDelta.Visible = false;
        }
        else
        {
            _lblDelta.Text = text;
            _lblDelta.Visible = true;
        }
    }
}
