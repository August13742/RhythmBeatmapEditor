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
    [Export] private Panel _selectionHighlight;
    
    [ExportCategory("Selection Highlight")]
    [Export] public Color HighlightColor { get; set; } = Colors.White;
    [Export] public int HighlightBorderWidth { get; set; } = 3;
    
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
        if(_selectionHighlight != null) _selectionHighlight.Visible = false;
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
        // Only allow drag if note is selected
        if (@event is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0)
        {
            if (IsSelected)
            {
                OnDrag?.Invoke(this, mm.Relative);
            }
            // If not selected, don't consume the event - let it pass through for marquee
        }
        
        if (@event is InputEventMouseButton mbRelease && !mbRelease.Pressed && mbRelease.ButtonIndex == MouseButton.Left)
        {
            if (IsSelected)
            {
                OnDragEnd?.Invoke(this);
            }
        }

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
             OnInput?.Invoke(this, @event);
        }
    }

    public void SetSelectState(bool selected)
    {
        IsSelected = selected;
        
        // Use dedicated selection highlight panel with exported parameters
        if (_selectionHighlight != null)
        {
            _selectionHighlight.Visible = IsSelected;
            
            // Update StyleBox if visible (create dynamic StyleBoxFlat)
            if (IsSelected)
            {
                var styleBox = new StyleBoxFlat
                {
                    BgColor = new Color(0, 0, 0, 0), // Transparent background
                    BorderWidthLeft = HighlightBorderWidth,
                    BorderWidthTop = HighlightBorderWidth,
                    BorderWidthRight = HighlightBorderWidth,
                    BorderWidthBottom = HighlightBorderWidth,
                    BorderColor = HighlightColor,
                    CornerRadiusTopLeft = 2,
                    CornerRadiusTopRight = 2,
                    CornerRadiusBottomLeft = 2,
                    CornerRadiusBottomRight = 2
                };
                _selectionHighlight.AddThemeStyleboxOverride("panel", styleBox);
            }
        }
        
        // Keep Modulate neutral to preserve note colors
        Modulate = Colors.White;
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
