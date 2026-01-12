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
        // Config
        public float PixelsPerSecond { get; set; } = 600f;
        public float LookAheadTime { get; set; } = 3.0f;
        public float HitLineOffset { get; set; } = 100f;

        // References
        private Control _laneContainer;
        private Control _noteLayer;
        private List<Control> _laneAnchors = new();
        
        // Pooling
        private ObjectPool<NoteObject> _notePool;
        private Node _poolStorage;
        
        // Active State
        private BeatmapData _currentMap;
        private Dictionary<NoteEvent, NoteObject> _activeVisuals = new();

        public override void _Ready()
        {
            // 1. Procedural Setup of Internal UI Structure if not present
            SetupInternalStructure();

            // 2. Initialize Pool (using a basic ColorRect-based NoteObject prefab logic)
            // Since we are procedural, we create a PackedScene from code or just use a helper
            // For this specific test, we assume we can instantiate NoteObject directly.
            // However, ObjectPool expects a PackedScene usually. 
            // We will create a dummy PackedScene wrapper or use a custom Pool for code-only nodes.
            // For simplicity in this script: We use a custom factory function in the pool if supported, 
            // OR we just create a simple generated scene.
            
            _poolStorage = new Node { Name = "PoolStorage" };
            AddChild(_poolStorage);
            
            // Create a procedural PackedScene for the Note
            var noteScene = new PackedScene();
            var tempRoot = new NoteObject();
            noteScene.Pack(tempRoot); 
            // Note: Pack() only works if node is in tree and owned. 
            // Easier approach: Just use a custom pool logic or instantiate code classes.
            // Let's stick to the ObjectPool<T> we wrote. We need a PackedScene.
            // If you have a .tscn, load it. If not, we rely on a helper.
        }

        // --- Public API ---

        public void Initialise(BeatmapData map)
        {
            _currentMap = map;
            
            // 1. Determine Lane Count
            int lanes = 4;
            if (map.Notes.Count > 0) lanes = map.Notes.Max(n => n.Lane) + 1;

            // 2. Rebuild Lanes
            foreach(Node n in _laneContainer.GetChildren()) n.QueueFree();
            _laneAnchors.Clear();

            for (int i = 0; i < lanes; i++)
            {
                var lane = new Control();
                lane.Name = $"Lane_{i}";
                lane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                lane.SizeFlagsVertical = SizeFlags.ExpandFill;
                
                // Visual bg
                var bg = new ColorRect { Color = new Color(0.1f, 0.1f, 0.1f, 0.3f) };
                bg.SetAnchorsPreset(LayoutPreset.FullRect);
                lane.AddChild(bg);
                
                _laneContainer.AddChild(lane);
                _laneAnchors.Add(lane);
            }
            
            // 3. Clear Visuals
            ResetVisuals();
        }

        public void Tick(float time)
        {
            if (_currentMap == null) return;
            
            // A. Window Calc
            float start = time - 1.0f;
            float end = time + LookAheadTime;
            
            // B. Reconcile (ECS-lite)
            // 1. Despawn out of bounds
            var toRemove = new List<NoteEvent>();
            foreach(var kvp in _activeVisuals)
            {
                if (kvp.Key.Time < start || kvp.Key.Time > end) 
                    toRemove.Add(kvp.Key);
            }
            foreach(var key in toRemove) DespawnNote(key);

            // 2. Spawn incoming
            // Opt: Use binary search index logic for performance
            var visibleNotes = _currentMap.Notes.Where(n => n.Time >= start && n.Time <= end);
            foreach(var note in visibleNotes)
            {
                if (!_activeVisuals.ContainsKey(note)) SpawnNote(note);
            }

            // C. Layout Updates
            float hitY = Size.Y - HitLineOffset;
            foreach(var kvp in _activeVisuals)
            {
                LayoutNote(kvp.Value, kvp.Key, time, hitY);
            }
        }
        
        // --- Internals ---

        private void SpawnNote(NoteEvent data)
        {
            // Simple lazy init for the pool to avoid PackedScene complexity in pure code
            if (_notePool == null) InitPool();

            var vis = _notePool.Rent();
            vis.Bind(data, GetLaneColor(data.Lane));
            vis.OnInput += HandleNoteInput;
            _activeVisuals[data] = vis;
        }

        private void DespawnNote(NoteEvent data)
        {
            if (_activeVisuals.TryGetValue(data, out var vis))
            {
                vis.OnInput -= HandleNoteInput;
                _notePool.Return(vis);
                _activeVisuals.Remove(data);
            }
        }
        
        private void LayoutNote(NoteObject vis, NoteEvent data, float time, float hitY)
        {
            // Y Calc (Falls Down)
            float timeDiff = data.Time - time;
            float yPos = hitY - (timeDiff * PixelsPerSecond);
            
            // Height Calc
            float h = Math.Max(15f, data.Duration * PixelsPerSecond);
            
            // X Calc
            if (data.Lane >= 0 && data.Lane < _laneAnchors.Count)
            {
                var lane = _laneAnchors[data.Lane];
                // Convert Global X to Local X
                float localX = lane.GlobalPosition.X - _noteLayer.GlobalPosition.X;
                float w = lane.Size.X;
                
                vis.Position = new Vector2(localX + 2, yPos - h); // Head is at bottom
                vis.Size = new Vector2(w - 4, h);
            }
        }

        private void HandleNoteInput(NoteObject source, InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                GD.Print($"[Editor] Clicked Note: {source.Data.Time}");
                source.SetSelectState(!source.IsSelected); // Toggle visual
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

        private void SetupInternalStructure()
        {
            // Background
            var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f) };
            bg.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            // Lane Container
            _laneContainer = new HBoxContainer();
            _laneContainer.SetAnchorsPreset(LayoutPreset.FullRect);
            ((HBoxContainer)_laneContainer).AddThemeConstantOverride("separation", 2);
            AddChild(_laneContainer);

            // Note Layer (On Top)
            _noteLayer = new Control();
            _noteLayer.SetAnchorsPreset(LayoutPreset.FullRect);
            _noteLayer.MouseFilter = MouseFilterEnum.Pass;
            AddChild(_noteLayer);
            
            // Hit Line
            var line = new ColorRect { Color = Colors.Green, CustomMinimumSize = new Vector2(0, 2) };
            line.SetAnchorsPreset(LayoutPreset.BottomWide);
            line.Position = new Vector2(0, -HitLineOffset);
            AddChild(line);
        }

        private void InitPool()
        {
            // Hack to create a fake PackedScene for the pool since we are code-only
            var scene = new PackedScene();
            var node = new NoteObject();
            scene.Pack(node); // Warning: This might fail if node is not in tree. 
            // Robust fallback:
            // Just manually instantiate in the pool if PackedScene fails? 
            // The ObjectPool class we wrote uses PackedScene. Let's make a real dynamic one.
            
            // For now, assume a valid PackedScene is assigned OR we modify pool.
            // Let's assume we use a specialized "CodePool" or just fix the pack.
            // Actually, for this prototype, we will skip the Pack() and Instantiate T directly in the pool modification.
            
            // *Modified ObjectPool Logic for pure code usage:*
            // We'll create a variant of ObjectPool that takes a Factory Func.
            _notePool = new ObjectPool<NoteObject>(() => new NoteObject(), 50, _noteLayer, _poolStorage);
        }

        private Color GetLaneColor(int lane)
        {
            return lane switch {
                0 => new Color("FF64FF"), 1 => new Color("64FFFF"),
                2 => new Color("64FFFF"), 3 => new Color("FF64FF"),
                _ => Colors.White
            };
        }
    }
}