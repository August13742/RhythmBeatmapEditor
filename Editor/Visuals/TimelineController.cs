using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using RhythmBeatmapEditor.Core.Models;
using RhythmBeatmapEditor.Core.Editor;
using ObjectPool;

namespace RhythmBeatmapEditor.Editor.Visuals
{
    public partial class TimelineController : Control
    {
        // --- Configuration ---
        [Export] public float PixelsPerSecond { get; set; } = 600f;
        [Export] public float LookAheadTime { get; set; } = 5.0f; // Seconds ahead to spawn
        [Export] public float HitLineOffset { get; set; } = 100f;

        [ExportCategory("Editing")]
        [Export] public float SnapPrecision { get; set; } = 0.05f;
        
        // --- Internal References ---
        private Control _laneContainer;
        private Control _noteLayer;
        private List<Control> _laneAnchors = new();
        
        // --- Pooling System ---
        private ObjectPool<NoteObject> _notePool;
        private Node _poolStorage;
        
        // --- State ---
        private EditorContext _context;
        private BeatmapData _currentMap;
        private Dictionary<NoteEvent, NoteObject> _activeVisuals = new();
        
        // Marquee
        private bool _isMarqueeDragging;
        private Vector2 _marqueeStart;
        private ColorRect _marqueeVisual;

        public override void _Ready()
        {
            // 1. Setup UI Structure (Layers, Hit Line)
            SetupInternalStructure();

            // 2. Setup Memory Management (Object Pool)
            SetupObjectPool();
            
            // 3. Marquee Visual
            _marqueeVisual = new ColorRect { Color = new Color(0.2f, 0.6f, 1.0f, 0.3f), Visible = false };
            AddChild(_marqueeVisual);
        }

        public void Initialise(EditorContext context)
        {
            // Cleanup previous if exists
            if (_context != null)
                 _context.OnSelectionChanged -= HandleExternalSelectionInfo;

            _context = context;
            _currentMap = context.CurrentBeatmap;

            // Subscribe to events
             _context.OnSelectionChanged += HandleExternalSelectionInfo;
            
            // 1. Determine Lane Count based on map data (default to 4)
            int lanes = 4;
            if (_currentMap.Notes != null && _currentMap.Notes.Count > 0) 
                lanes = _currentMap.Notes.Max(n => n.Lane) + 1;

            // 2. Generate Lane Anchors
            RebuildLanes(lanes);

            // 3. Reset State
            ResetVisuals();
        }
        
        public override void _ExitTree()
        {
            if (_context != null)
                _context.OnSelectionChanged -= HandleExternalSelectionInfo;
        }

        public override void _Process(double delta)
        {
            if (_context == null) return;
            
            // Sync settings
            if (Mathf.Abs(_context.SnapPrecision - SnapPrecision) > 0.0001f)
            {
                _context.SnapPrecision = SnapPrecision;
            }
            
            Tick(_context.PlaybackTime);
        }

        public void Tick(float time)
        {
            if (_currentMap == null) return;
            
            // A. Calculate View Window
            float start = time - 1.0f;     // Keep notes visible 1s after hit
            float end = time + LookAheadTime; // Spawn notes 3s early
            
            // B. Reconcile Visuals (Spawn/Despawn)
            
            // 1. Identify notes that left the window
            // (Using a separate list to avoid modifying collection while iterating)
            var toRemove = new List<NoteEvent>();
            foreach(var kvp in _activeVisuals)
            {
                if (kvp.Key.Time < start || kvp.Key.Time > end) 
                    toRemove.Add(kvp.Key);
            }
            foreach(var key in toRemove) DespawnNote(key);

            // 2. Identify new notes entering the window
            // Optimization: Since map.Notes is sorted, Binary Search would be O(log N). 
            // LINQ Where is O(N). Acceptable for < 5000 notes.
            var visibleNotes = _currentMap.Notes.Where(n => n.Time >= start && n.Time <= end);
            foreach(var note in visibleNotes)
            {
                if (!_activeVisuals.ContainsKey(note)) SpawnNote(note);
            }

            // C. Layout Update (Move Notes)
            float hitY = Size.Y - HitLineOffset;
            foreach(var kvp in _activeVisuals)
            {
                LayoutNote(kvp.Value, kvp.Key, time, hitY);
            }
        }
        
        // --- Internal Logic ---

        private void SpawnNote(NoteEvent data)
        {
            // Rent from pool (creates new if empty)
            var vis = _notePool.Rent();
            
            // Setup Visuals
            vis.Bind(data, GetLaneColor(data.Lane));
            vis.OnInput += HandleNoteInput;
            vis.OnDrag += HandleNoteDrag;
            vis.OnDragEnd += HandleNoteDragEnd;
            
            // Track
            _activeVisuals[data] = vis;
            
            // Sync selection state immediately
            if (_context != null && _context.IsSelected(data))
            {
                 vis.SetSelectState(true);
            }
            else
            {
                 vis.SetSelectState(false);
            }
        }

        public override void _GuiInput(InputEvent @event)
    {
        if (_context == null || !_context.IsEditMode) return;

        // Marquee Selection
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // Start Selection
                _isMarqueeDragging = true;
                _marqueeStart = mb.Position;
                _marqueeVisual.Visible = true;
                _marqueeVisual.Position = _marqueeStart;
                _marqueeVisual.Size = Vector2.Zero;

                bool isMulti = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Shift);
                // if (!isMulti) _context.ClearSelection();
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
             _marqueeVisual.Position = min;
             _marqueeVisual.Size = max - min;
        }
    }
    
    private void PerformMarqueeSelect(Rect2 selectionRect)
    {
        var overlappingNotes = new List<NoteEvent>();
        
        // Check intersections with active visuals
        foreach(var kvp in _activeVisuals)
        {
            Rect2 noteRect = kvp.Value.GetRect();
            if (selectionRect.Intersects(noteRect))
            {
                overlappingNotes.Add(kvp.Key);
            }
        }
        
        if (overlappingNotes.Count > 0)
        {
             _context.HandleMarqueeSelection(overlappingNotes);
        }
    }

    private void DespawnNote(NoteEvent data)
        {
            if (_activeVisuals.TryGetValue(data, out var vis))
            {
                vis.OnInput -= HandleNoteInput; // Crucial: Unsubscribe to prevent leaks
                vis.OnDrag -= HandleNoteDrag;
                vis.OnDragEnd -= HandleNoteDragEnd;
                _notePool.Return(vis);
                _activeVisuals.Remove(data);
            }
        }
        
        private void LayoutNote(NoteObject vis, NoteEvent data, float time, float hitY)
        {
            float pps = _context?.ScrollSpeed ?? PixelsPerSecond;

            // Y Position: Note moves DOWN towards hitY
            float timeDiff = data.Time - time;
            float yPos = hitY - (timeDiff * pps);
            
            // Height: Duration scaling
            float h = Math.Max(15f, data.Duration * pps);
            
            // X Position: Snap to Lane
            if (data.Lane >= 0 && data.Lane < _laneAnchors.Count)
            {
                var lane = _laneAnchors[data.Lane];
                
                // Convert Global X to Local X (relative to NoteLayer)
                float localX = lane.GlobalPosition.X - _noteLayer.GlobalPosition.X;
                float w = lane.Size.X;
                
                vis.Position = new Vector2(localX + 2, yPos - h); 
                vis.Size = new Vector2(w - 4, h);
                
                // Ghost Logic
                if (_context != null)
                {
                    var original = _context.GetOriginal(data);
                    
                    // Show Ghost only if:
                    // 1. Snapshot exists (original != data)
                    // 2. Note is actually Dirty (Unconfirmed edit)
                    if (original != data && data.State == NoteEvent.NoteState.Dirty)
                    {
                         float origDiff = original.Time - time;
                         float origY = hitY - (origDiff * pps);
                         float offsetY = origY - yPos;
                         
                         vis.SetGhostState(true);
                         vis.SetGhostOffset(new Vector2(0, offsetY));
                         vis.SetDeltaText($"{data.Time - original.Time:+0.00;-0.00}s");
                    }
                    else
                    {
                         vis.SetGhostState(false);
                         vis.SetDeltaText(null);
                    }
                }
            }
        }

        private void HandleNoteInput(NoteObject source, InputEvent e)
        {
            if (_context == null || !_context.IsEditMode) return;

            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                // Check modifiers
                bool isMulti = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Shift);
                
                if (isMulti)
                {
                    _context.ToggleSelection(source.Data);
                }
                else
                {
                    if (!_context.IsSelected(source.Data))
                    {
                        _context.SelectNote(source.Data, true); // Exclusive select
                    }
                    // Else: If already selected, do nothing (drag might start)
                }
                
                // Note: If we click a selected note without Ctrl, we KEEP selection (to allow drag).
                // However, commonly, clicking a single note in a group *selects only that note* unless dragging.
                // Logic: MouseDown just sets potential; MouseUp confirms?
                // For now: Simple Exclusive Select if not modifier. 
                // BUT: If we have 10 notes selected, and we click ONE to drag, we don't want to deselect others yet.
                // Standard Logic: 
                // - Click on Unselected: Select Exclusive.
                // - Click on Selected: Do nothing (wait for Drag or Up). 
                // - Up on Selected (no drag, no mod): Select Exclusive. 
                // For prototype: Just keep it simple. If not selected, Select Exclusive.
            }
        }
        
        // Drag State
        private float _unsnappedDragTime;
        private NoteEvent _dragLeadNote;
        
        private void HandleNoteDrag(NoteObject source, Vector2 delta)
        {
             if (_context == null || source == null || !_context.IsEditMode) return;
             
             // Initialize Drag State if new drag
             if (_dragLeadNote != source.Data)
             {
                 _dragLeadNote = source.Data;
                 _unsnappedDragTime = source.Data.Time;
                 _context.CaptureSnapshot(_context.SelectedNotes);
             }
             
             // 1. Update Unsnapped Time (Inverted Y)
             float pps = _context?.ScrollSpeed ?? PixelsPerSecond;
             float timeDelta = -delta.Y / pps;
             // 1. Accumulate Input
             _unsnappedDragTime += timeDelta;
             
             // 2. Calculate Snapped Target (Relative to ORIGINAL position)
             // This prevents "fighting" the grid if the note was placed off-grid (e.g. 5.12)
             // It ensures we move in increments of SnapPrecision (e.g. +0.05 -> 5.17)
             
             var original = _context.GetOriginal(source.Data);
             float baseTime = original.Time;
             float rawDelta = _unsnappedDragTime - baseTime; 
             
             // If we didn't capture properly (e.g. drag started before snapshot logic?), fallback to current.
             // Note: _unsnappedDragTime was initialised to source.Data.Time (which IS original at start).
             
             float precision = _context.SnapPrecision;
             if (precision <= 0.0001f) precision = 0.01f; // Safety
             
             // Round delta to nearest precision
             float snappedDelta = Mathf.Round(rawDelta / precision) * precision;
             
             float currentLeadTime = source.Data.Time;
             float targetTime = baseTime + snappedDelta; // Target Absolute Time
             
             if (targetTime < 0) targetTime = 0;
             
             // 3. Apply Difference to All Selected Notes
             // MoveDelta is difference between Target and Current
             float moveDelta = targetTime - currentLeadTime;
             
             if (Mathf.Abs(moveDelta) > 0.000001f)
             {
                 foreach(var note in _context.SelectedNotes)
                 {
                     note.Time += moveDelta;
                     if (note.Time < 0) note.Time = 0;
                     
                     // Ensure Snapshot exists for all notes involved (handled by CaptureSnapshot at drag start)
                     if (note.State != NoteEvent.NoteState.Dirty)
                     {
                          // If drag logic didn't capture this note (e.g. multi-select expanded?), catch it.
                          // But we trust HandleNoteDrag initialization.
                     }
                 }
             }

             // 3. Selection Update?
             // Context selection didn't change (same objects), but their Property changed.
             // We need to refresh Inspector if open.
             // We can fire a custom signal or just let `_Process` update?
             // SongControlPanel logic specifically listens to SelectionChanged.
             // It doesn't listen to "Time Changed" on note.
             // We might need to manually trigger update?
             // Since we have "Edit Session" later, let's leave this for now.
             // The visual position update happens in Tick/Process automatically because `Data.Time` changed.
        }
        
        private void HandleNoteDragEnd(NoteObject source)
        {
            if (_context == null) return;
            
            GD.Print($"[Editor] Drag End. New Time: {source.Data.Time}");
            // Reset Drag State
            _dragLeadNote = null;
            _unsnappedDragTime = 0;
            
            // 1. Sort Data (Time changed)
            _context.CurrentBeatmap.Sort();
            
            // 2. Notify Change
            // _context.NotifyNotesUpdated()?
            // For now, toggle selection to refresh UI?
            // 2. Notify Change
            // Just trigger OnSelectionChanged to refresh UI (e.g. Inspector values)
            // We do NOT want to change the selection (Keep multi-selection if active)
            _context.RefreshSelectionUI();
        }
        


        private void HandleExternalSelectionInfo()
        {
            // Update ALL visuals
            var selectedArgs = _context.SelectedNotes;
            
            foreach(var kvp in _activeVisuals)
            {
                // Optimisation: HashSet lookup
                bool isSelected = _context.IsSelected(kvp.Key);
                kvp.Value.SetSelectState(isSelected);
            }
        }

        private void ResetVisuals()
        {
            foreach(var vis in _activeVisuals.Values)
            {
                vis.OnInput -= HandleNoteInput;
                vis.OnDrag -= HandleNoteDrag;
                vis.OnDragEnd -= HandleNoteDragEnd;
                _notePool.Return(vis);
            }
            _activeVisuals.Clear();
        }

        // --- Setup Helpers ---

        private void SetupInternalStructure()
        {
            // 1. Background
            var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
            bg.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            // 2. Lane Container (Horizontal Layout)
            _laneContainer = new HBoxContainer();
            _laneContainer.SetAnchorsPreset(LayoutPreset.FullRect);
            ((HBoxContainer)_laneContainer).AddThemeConstantOverride("separation", 2);
            AddChild(_laneContainer);

            // 3. Note Layer (Overlay)
            _noteLayer = new Control();
            _noteLayer.SetAnchorsPreset(LayoutPreset.FullRect);
            _noteLayer.MouseFilter = MouseFilterEnum.Pass; // Let clicks pass through empty space
            AddChild(_noteLayer);
            
            // 4. Hit Line
            var line = new ColorRect { Color = Colors.Green, CustomMinimumSize = new Vector2(0, 2) };
            line.SetAnchorsPreset(LayoutPreset.BottomWide);
            line.Position = new Vector2(0, -HitLineOffset);
            AddChild(line);
        }
        
        private void SetupObjectPool()
        {
            // Create a hidden node to store inactive notes
            _poolStorage = new Node { Name = "PoolStorage" };
            AddChild(_poolStorage);

            // Initialise Pool using Factory Pattern
            // Since NoteObject builds its own visuals in _Ready(), we just need to "new" it.
            _notePool = new ObjectPool<NoteObject>(
                factoryMethod: () => new NoteObject(), 
                prewarm: 50, 
                activeParent: _noteLayer, 
                inactiveParent: _poolStorage
            );
        }

        private void RebuildLanes(int laneCount)
        {
            // Clear existing
            foreach(Node n in _laneContainer.GetChildren()) n.QueueFree();
            _laneAnchors.Clear();

            // Create new
            for (int i = 0; i < laneCount; i++)
            {
                var lane = new Control();
                lane.Name = $"Lane_{i}";
                lane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                lane.SizeFlagsVertical = SizeFlags.ExpandFill;
                
                // Lane Background (Stripes)
                var bg = new ColorRect { Color = new Color(0.1f, 0.1f, 0.1f, i % 2 == 0 ? 0.3f : 0.2f) };
                bg.SetAnchorsPreset(LayoutPreset.FullRect);
                lane.AddChild(bg);
                
                _laneContainer.AddChild(lane);
                _laneAnchors.Add(lane);
            }
        }

        private Color GetLaneColor(int lane)
        {
            return lane switch {
                0 => Colors.Pink,
                1 => Colors.Cyan,
                2 => Colors.Cyan,
                3 => Colors.Pink,
                _ => Colors.Magenta
            };
        }
    }
}