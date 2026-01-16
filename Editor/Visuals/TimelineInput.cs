using Godot;
using RhythmBeatmapEditor.Core.Models;
using RhythmBeatmapEditor.Core.Editor;
using System.Collections.Generic;

namespace RhythmBeatmapEditor.Editor.Visuals;

public partial class TimelineInput : Node
{

    
    private EditorContext _context;
    private Control _timelineRoot; // For coordinate conversions
    private Control _noteLayer;
    private float _pixelsPerSecond = 100f; // Fallback
    
    // Drag State
    private float _accumulatedDragX = 0f;
    private float _unsnappedDragTime;
    private NoteEvent _dragLeadNote;
    
    // Marquee State
    private bool _isMarqueeDragging;
    private Vector2 _marqueeStart;
    private ColorRect _marqueeVisual;
    
    public void Initialise(EditorContext context, Control timelineRoot, Control noteLayer, float pps)
    {
        _context = context;
        _timelineRoot = timelineRoot;
        _noteLayer = noteLayer;
        _pixelsPerSecond = pps;
        
        // Setup Marquee Visual
        _marqueeVisual = new ColorRect { Color = new Color(0.2f, 0.6f, 1.0f, 0.3f), Visible = false };
        _noteLayer.AddChild(_marqueeVisual);
    }
    
    public void HandleGuiInput(InputEvent @event)
    {
        if (_context == null || !_context.IsEditMode) return;
        
        // Marquee Selection
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // Start Selection
                _isMarqueeDragging = true;
                _marqueeStart = mb.Position; // Local to TimelineController (if caller is TimelineController)
                _marqueeVisual.Visible = true;
                _marqueeVisual.Position = _marqueeStart;
                _marqueeVisual.Size = Vector2.Zero;
            }
            else if (_isMarqueeDragging)
            {
                // End Selection
                _isMarqueeDragging = false;
                _marqueeVisual.Visible = false;
                
                PerformMarqueeSelect(_marqueeVisual.GetRect());
            }
        }
        else if (@event is InputEventMouseMotion mm && _isMarqueeDragging)
        {
             // Update Visual
             var end = mm.Position;
             var min = new Vector2(Mathf.Min(_marqueeStart.X, end.X), Mathf.Min(_marqueeStart.Y, end.Y));
             var max = new Vector2(Mathf.Max(_marqueeStart.X, end.X), Mathf.Max(_marqueeStart.Y, end.Y));
             
             // Marquee visual needs to be local to NoteLayer? 
             // If TimelineHandler passes event from TimelineController (Root), we need to adjust if NoteLayer is offset?
             // Assuming NoteLayer fills Root.
             _marqueeVisual.Position = min;
             _marqueeVisual.Size = max - min;
        }
    }
    
    private void PerformMarqueeSelect(Rect2 selectionRect)
    {
        EmitSignal(SignalName.RequestMarqueeSelect, selectionRect);
    }
    
    [Signal] public delegate void RequestMarqueeSelectEventHandler(Rect2 rect);


    public void HandleNoteInput(NoteObject source, InputEvent e)
    {
        if (_context == null || !_context.IsEditMode) return;

        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
             bool isMulti = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Shift);
             
             if (isMulti)
             {
                 _context.ToggleSelection(source.Data);
             }
             else
             {
                 if (!_context.IsSelected(source.Data))
                 {
                     _context.SelectNote(source.Data, true); 
                 }
             }
        }
    }
    
    public void HandleNoteDrag(NoteObject source, Vector2 delta)
    {
         if (_context == null || source == null || !_context.IsEditMode) return;
         
         if (_dragLeadNote != source.Data)
         {
             _dragLeadNote = source.Data;
             _unsnappedDragTime = source.Data.Time; 
             _context.CaptureSnapshot(_context.SelectedNotes);
             _accumulatedDragX = 0f;
         }
         
         // Lane Dragging
         _accumulatedDragX += delta.X;
         
         // Dynamic Threshold: Timeline Width / Lane Count
         float laneWidth = 150f; // Default fallback
         if (_timelineRoot != null && _context.MaxLanes > 0)
         {
             laneWidth = _timelineRoot.Size.X / _context.MaxLanes;
         }
         float threshold = laneWidth;
         
         int laneShift = 0;
         while (Mathf.Abs(_accumulatedDragX) > threshold)
         {
             if (_accumulatedDragX > 0) { laneShift++; _accumulatedDragX -= threshold; }
             else { laneShift--; _accumulatedDragX += threshold; }
         }
         
         if (laneShift != 0)
         {
             int minLane = int.MaxValue, maxLane = int.MinValue;
             foreach(var n in _context.SelectedNotes)
             {
                 if (n.Lane < minLane) minLane = n.Lane;
                 if (n.Lane > maxLane) maxLane = n.Lane;
             }
             
             if (maxLane + laneShift >= _context.MaxLanes) laneShift = _context.MaxLanes - 1 - maxLane;
             if (minLane + laneShift < 0) laneShift = 0 - minLane;
             
             if (laneShift != 0)
                 foreach(var note in _context.SelectedNotes) note.Lane += laneShift;
         }
         
         // Time Dragging
         float pps = _context.ScrollSpeed > 0 ? _context.ScrollSpeed : _pixelsPerSecond;
         float timeDelta = -delta.Y / pps;
         _unsnappedDragTime += timeDelta;
         
         var original = _context.GetOriginal(source.Data);
         float baseTime = original.Time;
         float rawDelta = _unsnappedDragTime - baseTime; 
         
         float precision = _context.SnapPrecision > 0.0001f ? _context.SnapPrecision : 0.01f;
         float snappedDelta = Mathf.Round(rawDelta / precision) * precision;
         float targetTime = baseTime + snappedDelta;
         if (targetTime < 0) targetTime = 0;
         
         float moveDelta = targetTime - source.Data.Time;
         if (Mathf.Abs(moveDelta) > 0.000001f)
         {
             foreach(var note in _context.SelectedNotes)
             {
                 note.Time += moveDelta;
                 if (note.Time < 0) note.Time = 0;
             }
         }
         
         _context.RefreshSelectionUI();
    }
    
    public void HandleNoteDragEnd(NoteObject source)
    {
        if (_context == null) return;
        _dragLeadNote = null;
        _unsnappedDragTime = 0;
        _context.CurrentBeatmap.Sort();
        _context.RefreshSelectionUI();
    }
    
    public void HandleLaneInput(InputEvent @event, int laneIndex, float hitLineOffset, float height)
    {
        if (_context == null || !_context.IsEditMode) return;
        
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (Input.IsKeyPressed(Key.Ctrl))
            {
                float pps = _context.ScrollSpeed;
                float hitY = height - hitLineOffset;
                // We use timelineRoot to get consistent Y coordinate (Vertical scroll/layout)
                float myY = _timelineRoot.GetLocalMousePosition().Y; 
                
                float time = _context.PlaybackTime + (hitY - myY) / pps;
                time = _context.SnapTime(time);
                
                if (time < 0) time = 0;
                
                var newNote = new NoteEvent 
                {
                    Time = time,
                    Lane = laneIndex,
                    Duration = 0.1f, // Default duration
                    Pitch = 60, // Default C4
                    Source = "unknown"
                };
                
                _context.AddNote(newNote);
            }
        }
    }
}
