using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using RhythmBeatmapEditor.Core.Models;
using ObjectPool;

namespace RhythmBeatmapEditor.Editor.Visuals
{
    public partial class TimelineController : Control
    {
        // --- Configuration ---
        [Export] public float PixelsPerSecond { get; set; } = 600f;
        [Export] public float LookAheadTime { get; set; } = 3.0f;
        [Export] public float HitLineOffset { get; set; } = 100f;

        // --- Internal References ---
        private Control _laneContainer;
        private Control _noteLayer;
        private List<Control> _laneAnchors = new();
        
        // --- Pooling System ---
        private ObjectPool<NoteObject> _notePool;
        private Node _poolStorage;
        
        // --- State ---
        private BeatmapData _currentMap;
        private Dictionary<NoteEvent, NoteObject> _activeVisuals = new();

        public override void _Ready()
        {
            // 1. Setup UI Structure (Layers, Hit Line)
            SetupInternalStructure();

            // 2. Setup Memory Management (Object Pool)
            SetupObjectPool();
        }

        public void Initialise(BeatmapData map)
        {
            _currentMap = map;
            
            // 1. Determine Lane Count based on map data (default to 4)
            int lanes = 4;
            if (map.Notes != null && map.Notes.Count > 0) 
                lanes = map.Notes.Max(n => n.Lane) + 1;

            // 2. Generate Lane Anchors
            RebuildLanes(lanes);

            // 3. Reset State
            ResetVisuals();
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
            
            // Track
            _activeVisuals[data] = vis;
        }

        private void DespawnNote(NoteEvent data)
        {
            if (_activeVisuals.TryGetValue(data, out var vis))
            {
                vis.OnInput -= HandleNoteInput; // Crucial: Unsubscribe to prevent leaks
                _notePool.Return(vis);
                _activeVisuals.Remove(data);
            }
        }
        
        private void LayoutNote(NoteObject vis, NoteEvent data, float time, float hitY)
        {
            // Y Position: Note moves DOWN towards hitY
            float timeDiff = data.Time - time;
            float yPos = hitY - (timeDiff * PixelsPerSecond);
            
            // Height: Duration scaling
            float h = Math.Max(15f, data.Duration * PixelsPerSecond);
            
            // X Position: Snap to Lane
            if (data.Lane >= 0 && data.Lane < _laneAnchors.Count)
            {
                var lane = _laneAnchors[data.Lane];
                
                // Convert Global X to Local X (relative to NoteLayer)
                float localX = lane.GlobalPosition.X - _noteLayer.GlobalPosition.X;
                float w = lane.Size.X;
                
                // Position logic: yPos is the Start Time (Bottom of rect)
                vis.Position = new Vector2(localX + 2, yPos - h); 
                vis.Size = new Vector2(w - 4, h);
            }
        }

        private void HandleNoteInput(NoteObject source, InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                GD.Print($"[Editor] Clicked Note: {source.Data.Time}");
                source.SetSelectState(!source.IsSelected);
            }
        }

        private void ResetVisuals()
        {
            foreach(var vis in _activeVisuals.Values)
            {
                vis.OnInput -= HandleNoteInput;
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

            // Initialize Pool using Factory Pattern
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
                0 => new Color("FF64FF"), // Left (Pink)
                1 => new Color("64FFFF"), // Down (Cyan)
                2 => new Color("64FFFF"), // Up (Cyan)
                3 => new Color("FF64FF"), // Right (Pink)
                _ => Colors.White
            };
        }
    }
}