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
    
    [Export] public Control Head { get; private set; }
    [Export] public Control Body { get; private set; }
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
        if(Body != null) Body.Visible = false;
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
        
        if (Head != null)
        {
             // Fix for "Head" being a Panel with StyleBox vs ColorRect children
             // Assuming Head has a child "Fill" or we color the panel itself if StyleBoxFlat allows
             // I added a "Fill" child in the tscn update.
             var fill = Head.GetNodeOrNull<ColorRect>("Fill");
             if(fill != null) fill.Color = color;
             Head.SelfModulate = Colors.White; // Border
        }

        if (Body != null)
        {
             Color trailColor = color;
             trailColor.A = 0.5f;
             Body.SelfModulate = trailColor;
             
             // Show Trail ONLY if Hold
             Body.Visible = (data.Type == NoteEvent.NoteType.Hold);
        }
        
       SetSelectState(IsSelected);
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
        // Highlight logic
        Color borderCol = IsSelected ? Colors.Yellow : Colors.White;
        
        if (Head != null)
        {
            // Update Border Color
            if(Head is Panel p && p.GetThemeStylebox("panel") is StyleBoxFlat sb)
            {
               // This modifies the resource shared by all instances? 
               // Better to use SelfModulate if possible or duplicate stylebox.
               // For simplicity, let's just Modulate the Head container if it's the border?
               Head.SelfModulate = borderCol; 
            }
        }
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
