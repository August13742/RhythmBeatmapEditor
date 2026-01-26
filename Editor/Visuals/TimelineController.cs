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
        [Export] public float LookAheadTime { get; set; } = 5.0f;
        [Export] public float HitLineOffset { get; set; } = 100f;

        [ExportCategory("Editing")]
        [Export] public float SnapPrecision { get; set; } = 0.05f;
        [Export] public float NoteWidthPercent { get; set; } = 0.95f;

        [ExportCategory("Scene References")]
        [Export] public Control Highway { get; private set; } // New Highway Reference

        [Export] public Control LaneContainer { get; private set; }
        [Export] public Control NoteLayer { get; private set; }
        [Export] public GhostLayer GhostLayer { get; private set; }
        [Export] public Control HitLine { get; private set; }
        
        // REPLACEMENT: Export the Scene, don't load by string UID
        [Export] public PackedScene NoteScene { get; private set; } 
        // OPTIONAL: If want dynamic lanes, use a Lane Scene, not raw Controls
        [Export] public PackedScene LaneScene { get; private set; }

        // --- Internal Data ---
        private TimelineInput _input;
        private ObjectPool<NoteObject> _notePool;
        private Node _poolStorage;
        private EditorContext _context;
        private BeatmapData _currentMap;
        private Dictionary<NoteEvent, NoteObject> _activeVisuals = new();

        // --- Optimisation: Layout Cache ---
        // We store the calculated X positions here to avoid GlobalPosition calls in Update
        private struct LaneLayout
        {
            public float LocalX;
            public float Width;
            public Color Color;
        }
        private LaneLayout[] _laneCache;

        // --- Optimisation: Spawning ---
        private int _spawnStartIndex = 0;
        private List<NoteEvent> _despawnCache = new();

        
        public override void _Ready()
        {
            // Validation
            if (LaneContainer == null || NoteLayer == null || NoteScene == null || Highway == null)
            {
                GD.PrintErr("[TimelineController] Missing References.");
                SetProcess(false);
                return;
            }

            SetupObjectPool();
            
            _input = new TimelineInput { Name = "TimelineInput" };
            AddChild(_input);
            _input.RequestMarqueeSelect += PerformMarqueeSelect;

        }

        public void Initialise(EditorContext context)
        {
            if (_context != null) 
            {
                _context.OnSelectionChanged -= HandleExternalSelectionInfo;
            }
            _context = context;
            _currentMap = context.CurrentBeatmap;
            
            // Pass Highway as root for coordinate checks
            _input.Initialise(context, Highway, NoteLayer, PixelsPerSecond);
            _context.OnSelectionChanged += HandleExternalSelectionInfo;

            // 1. Setup Lanes
            int lanes = 4;
            if (_currentMap.Notes.Count > 0) 
                lanes = _currentMap.Notes.Max(n => n.Lane) + 1;

            SetupLanes(lanes);

            // 2. Reset
            ResetVisuals();
            _spawnStartIndex = 0;
            
        }
        
        public override void _ExitTree()
        {
            if (_context != null) _context.OnSelectionChanged -= HandleExternalSelectionInfo;

        }
        

        public override void _Process(double delta)
        {
            if (_context == null) return;
            
            // Re-sync HitLine visual position only if screen size changed (handled by anchors usually) or offset changed
            // Relative to Highway Size
            HitLine.Position = new Vector2(0, Highway.Size.Y - HitLineOffset);

            Tick(_context.PlaybackTime);
        }
        private Rect2 _lastLaneRect;
        private float _lastLayerX;

        public void Tick(float time)
        {
            if (_currentMap == null) return;

            // --- DIRTY CHECK ---
            // check if the container Resized OR Moved.
            Rect2 currentLaneRect = LaneContainer.GetGlobalRect();
            float currentLayerX = NoteLayer.GlobalPosition.X;

            // Check if Rect changed (Position OR Size) OR if NoteLayer moved
            bool isDirty = currentLaneRect != _lastLaneRect || 
                        Math.Abs(currentLayerX - _lastLayerX) > 0.01f;

            if (isDirty)
            {
                RefreshLaneCache();
                
                // Update trackers
                _lastLaneRect = currentLaneRect;
                _lastLayerX = currentLayerX;
                
                // GD.Print($"[Timeline] Layout updated. Width: {currentLaneRect.Size.X}"); 
            }

            float start = time - 1.0f;
            float end = time + LookAheadTime;

            // 1. Despawn
            _despawnCache.Clear();
            foreach (var kvp in _activeVisuals)
            {
                if (kvp.Key.Time < start || kvp.Key.Time > end)
                    _despawnCache.Add(kvp.Key);
            }
            foreach (var note in _despawnCache) DespawnNote(note);

            // 2. Spawn
            var notes = _currentMap.Notes;
            int count = notes.Count;
            if (count > 0)
            {
                if (_spawnStartIndex >= count)_spawnStartIndex = count - 1;
                while (_spawnStartIndex > 0 && notes[_spawnStartIndex].Time > start) _spawnStartIndex--;
                while (_spawnStartIndex < count && notes[_spawnStartIndex].Time < start) _spawnStartIndex++;

                for (int i = _spawnStartIndex; i < count; i++)
                {
                    var note = notes[i];
                    if (note.Time > end) break;
                    if (!_activeVisuals.ContainsKey(note)) SpawnNote(note);
                }
            }

            // 3. Layout Update
            GhostLayer.Clear();
            
            
            float hitY = Highway.Size.Y - HitLineOffset;
            
            foreach (var kvp in _activeVisuals)
            {
                // Pass the cached array, not the Scene Nodes
                LayoutNote(kvp.Value, kvp.Key, time, hitY);
            }
            
            UpdateGhostsAndOverlays(time, hitY);

            GhostLayer.Commit();
        }
        
        // --- Internal Logic ---

        private void SpawnNote(NoteEvent data)
        {
            var vis = _notePool.Rent();
            vis.Bind(data, GetSourceColor(data.Source));
            vis.OnInput += HandleNoteInput;
            vis.OnDrag += HandleNoteDrag;
            vis.OnDragEnd += HandleNoteDragEnd;
            
            _activeVisuals[data] = vis;
            
            if (_context != null && _context.IsSelected(data))
                vis.SetSelectState(true);
            else
                vis.SetSelectState(false);
        }

        public override void _GuiInput(InputEvent @event)
        {
            _input.HandleGuiInput(@event);
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
                vis.OnInput -= HandleNoteInput;
                vis.OnDrag -= HandleNoteDrag;
                vis.OnDragEnd -= HandleNoteDragEnd;
                _notePool.Return(vis);
                _activeVisuals.Remove(data);
                
                // Deselect if active (Out of range)
                if (_context != null && _context.IsSelected(data))
                {
                   _context.DeselectNote(data);
                }
            }
        }
        
        // --- Critical Optimisation: Layout Note ---
        private void LayoutNote(NoteObject vis, NoteEvent data, float time, float hitY)
        {
            Rect2 rect = CalculateNoteRect(data.Lane, data.Time, data.Duration, time, hitY);
            if (rect.Size.X > 0)
            {
                vis.Position = rect.Position;
                vis.Size = rect.Size;
            }
        }
        
        private Rect2 CalculateNoteRect(int lane, float noteTime, float duration, float refTime, float hitY)
        {
            float pps = _context.ScrollSpeed;
            float timeDiff = noteTime - refTime;
            float yPos = hitY - (timeDiff * pps);
            float h = Math.Max(15f, duration * pps);

            // READ FROM CACHE. No GlobalPosition calls.
            if (lane >= 0 && _laneCache != null && lane < _laneCache.Length)
            {
                var layout = _laneCache[lane]; 
                
                float noteW = layout.Width * NoteWidthPercent;
                float padX = (layout.Width - noteW) / 2;

                return new Rect2(layout.LocalX + padX, yPos - h, noteW, h);
            }
            return new Rect2();
        }
        
        private void UpdateGhostsAndOverlays(float time, float hitY)
        {
             // Iterate Dirty List
             foreach(var note in _context.GetDirtyNotes())
             {
                  var original = _context.GetOriginal(note);
                  
                  // 1. Ghost Logic (Show if Moved)
                  // Ghost shows Original Position
                  if (Mathf.Abs(note.Time - original.Time) > 0.001f || note.Lane != original.Lane)
                  {
                      Color col = GetSourceColor(original.Source);
                      col.A = 0.4f; // Ghost alpha
                      
                      Rect2 ghostRect = CalculateNoteRect(original.Lane, original.Time, original.Duration, time, hitY);
                      Rect2 targetRect = CalculateNoteRect(note.Lane, note.Time, note.Duration, time, hitY);
                      
                      // Safety: GhostLayer handles coordinate drawing
                      GhostLayer.AddGhost(ghostRect, targetRect.GetCenter(), col);
                  }
                  
                  // 2. Delta Label Logic (Show on Active Visual only)
                  if (_activeVisuals.TryGetValue(note, out var vis))
                  {
                      float diff = note.Time - original.Time;
                      if (Mathf.Abs(diff) > 0.001f)
                      {
                          vis.SetDeltaText($"{(diff>0?"+":"")}{diff:F2}s");
                      }
                      else
                      {
                          vis.SetDeltaText(null);
                      }
                  }
             }
        }

        private void HandleNoteInput(NoteObject source, InputEvent e) => _input.HandleNoteInput(source, e);
        
        private void HandleNoteDrag(NoteObject source, Vector2 delta) => _input.HandleNoteDrag(source, delta);
        
        private void HandleNoteDragEnd(NoteObject source) => _input.HandleNoteDragEnd(source);
        

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
            foreach (var vis in _activeVisuals.Values)
            {
                vis.OnInput -= HandleNoteInput;
                vis.OnDrag -= HandleNoteDrag;
                vis.OnDragEnd -= HandleNoteDragEnd;
                _notePool.Return(vis);
            }
            _activeVisuals.Clear();
        }

        // --- Lane Cache ---
        public void RefreshLaneCache()
        {
            if (LaneContainer == null || NoteLayer == null) return;
            
            int count = LaneContainer.GetChildCount();
            if (_laneCache == null || _laneCache.Length != count)
                _laneCache = new LaneLayout[count];

            // Calculate relationship between LaneContainer and NoteLayer ONCE.
            // NoteLayer is sibling or overlay, so we need relative coords.
            float containerGlobalX = LaneContainer.GlobalPosition.X;
            float layerGlobalX = NoteLayer.GlobalPosition.X;
            float relativeBaseX = containerGlobalX - layerGlobalX;

            for (int i = 0; i < count; i++)
            {
                if (LaneContainer.GetChild(i) is Control lane)
                {
                    // Cache the X position relative to NoteLayer
                    _laneCache[i].LocalX = relativeBaseX + lane.Position.X;
                    _laneCache[i].Width = lane.Size.X;
                    // Lane color for background is procedural, but note color is now Source based.
                    // We can keep this for debug or lane bg if needed, but unused by notes now.
                    _laneCache[i].Color = Colors.White; 
                }
            }
        }

        private void SetupLanes(int laneCount)
        {
            // Check if we actually need to rebuild
            if (LaneContainer.GetChildCount() == laneCount) return;

            // Clear existing
            foreach (Node n in LaneContainer.GetChildren()) n.QueueFree();

            Color laneBgColor = Color.Color8(20, 20, 25);
            Color borderColor = Color.Color8(35, 35, 45);

            for (int i = 0; i < laneCount; i++)
            {
                Control lane;

                // Prefer Instantiation over 'new Control'
                if (LaneScene != null)
                {
                    lane = LaneScene.Instantiate<Control>();
                }
                else
                {
                    lane = new Control();
                    lane.Name = $"Lane_{i}";
                    lane.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    lane.MouseFilter = MouseFilterEnum.Stop; 

                    // 1. Background
                    var bg = new ColorRect
                    {
                        Color = laneBgColor,
                        LayoutMode = 1,
                        AnchorsPreset = (int)LayoutPreset.FullRect,
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    lane.AddChild(bg);

                    // 2. Left Border (Standard Separator)
                    var leftBorder = new ColorRect
                    {
                        Color = borderColor,
                        CustomMinimumSize = new Vector2(2, 0),
                        LayoutMode = 1,
                        AnchorLeft = 0,
                        AnchorRight = 0,
                        AnchorTop = 0,
                        AnchorBottom = 1,
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    lane.AddChild(leftBorder);

                    // 3. Right Border (Sealing the Last Lane)
                    if (i == laneCount - 1)
                    {
                        var rightBorder = new ColorRect
                        {
                            Color = borderColor,
                            CustomMinimumSize = new Vector2(2, 0),
                            LayoutMode = 1,
                            AnchorLeft = 1,  // Anchor to Right Edge
                            AnchorRight = 1, 
                            AnchorTop = 0,
                            AnchorBottom = 1,
                            GrowHorizontal = GrowDirection.Begin, // Grow inwards (to the left)
                            MouseFilter = MouseFilterEnum.Ignore
                        };
                        lane.AddChild(rightBorder);
                    }
                }

                // Capture index for closure
                int idx = i;
                lane.GuiInput += (e) => HandleLaneInput(e, idx);

                LaneContainer.AddChild(lane);
            }

            // Force cache refresh
            CallDeferred(nameof(RefreshLaneCache));
        }

        private void SetupObjectPool()
        {
            _poolStorage = new Node { Name = "PoolStorage" };
            AddChild(_poolStorage);

            // Factory uses the Exported Scene
            _notePool = new ObjectPool<NoteObject>(
                factoryMethod: () => NoteScene.Instantiate<NoteObject>(), 
                prewarm: 50, 
                activeParent: NoteLayer, 
                inactiveParent: _poolStorage
            );
        }
        
        private void HandleLaneInput(InputEvent @event, int laneIndex)
        {
             _input.HandleLaneInput(@event, laneIndex, HitLineOffset, Highway.Size.Y);
        }

        private Color GetSourceColor(string source)
        {
            if (string.IsNullOrEmpty(source)) return Colors.Gray;
            
            source = source.ToLower();
            if (source.Contains("vocal")) return Colors.MediumOrchid;
            if (source.Contains("guitar")) return Colors.SpringGreen;
            if (source.Contains("piano")) return Colors.DeepSkyBlue;
            if (source.Contains("bass")) return Colors.Gold;
            if (source.Contains("drum")) return Colors.Tomato;
            
            return Colors.LightGray;
        }
    }
}