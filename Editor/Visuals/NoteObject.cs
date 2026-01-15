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
    
    private ColorRect _visualRect;
    private ColorRect _ghostRect;
    private Label _lblDelta;
    
    public bool IsSelected { get; private set; }

    public override void _Ready()
    {
        // One-time setup
        _ghostRect = new ColorRect { Color = new Color(1, 1, 1, 0.3f), Visible = false };
        _ghostRect.SetAnchorsPreset(LayoutPreset.FullRect);
        _ghostRect.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_ghostRect);

        _visualRect = new ColorRect();
        _visualRect.SetAnchorsPreset(LayoutPreset.FullRect);
        _visualRect.MouseFilter = MouseFilterEnum.Ignore; 
        AddChild(_visualRect);
        
        _lblDelta = new Label { 
            Visible = false, 
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        _lblDelta.SetAnchorsPreset(LayoutPreset.TopWide);
        _lblDelta.Position = new Vector2(0, -20); // Above note
        AddChild(_lblDelta);

        MouseFilter = MouseFilterEnum.Stop; 
    }
    
    // IPoolable: Reset state when reused
    public void OnSpawned() 
    {
        Modulate = Colors.White;
        SetSelectState(false);
        SetGhostState(false);
        _lblDelta.Visible = false;
    }

    public void OnDespawned()
    {
        Data = null;
        OnInput = null;
        OnDrag = null;
        OnDragEnd = null;
        _baseColor = default;
    }

    private Color _baseColor;

    public void Bind(NoteEvent data, Color color)
    {
        Data = data;
        _baseColor = color;
        _visualRect.Color = IsSelected ? Colors.White : _baseColor;
    }
    
    public override void _GuiInput(InputEvent @event)
    {
        // Handle Dragging
        if (@event is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0)
        {
            // Emit Drag
            OnDrag?.Invoke(this, mm.Relative);
        }
        
        // Handle Release (End Drag)
        if (@event is InputEventMouseButton mbRelease && !mbRelease.Pressed && mbRelease.ButtonIndex == MouseButton.Left)
        {
            OnDragEnd?.Invoke(this);
        }

        // Handle Click (Selection)
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
             OnInput?.Invoke(this, @event);
        }
    }

    public void SetSelectState(bool selected)
    {
        IsSelected = selected;
        // Visual feedback
        _visualRect.Color = IsSelected ? Colors.White : _baseColor;
    }
    
    public void SetGhostState(bool visible)
    {
        _ghostRect.Visible = visible;
        QueueRedraw();
    }
    
    public void SetGhostOffset(Vector2 offset)
    {
        _ghostRect.Position = offset;
        QueueRedraw();
    }
    
    public void SetDeltaText(string text)
    {
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
    
    public override void _Draw()
    {
        if (_ghostRect.Visible)
        {
            // Draw connection line
            // From: Center of VisualRect (Current)
            // To: Center of Ghost (Original)
            // Note: VisualRect is offset by nothing, it fills the Control.
            // Control pivot is TopLeft. 
            // Center = Size / 2.
            // GhostRect is offset by _ghostRect.Position relative to TopLeft.
            // GhostCenter = _ghostRect.Position + (Size / 2).
            
            var center = Size / 2;
            var ghostCenter = _ghostRect.Position + center;
            var color = new Color(1, 1, 1, 0.4f);
            
            DrawLine(center, ghostCenter, color, 1.5f, true);
            DrawCircle(ghostCenter, 2.0f, color); // Optional dot
        }
    }
}