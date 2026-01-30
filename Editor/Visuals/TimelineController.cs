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
        
        // --- Multi-Map Support ---
        private Dictionary<string, Color> _mapColors = new();
        private static readonly Color[] MapColorPalette = new Color[]
        {
            Colors.DeepSkyBlue,    // Map 1 - Blue
            Colors.SpringGreen,   // Map 2 - Green  
            Colors.Gold,          // Map 3 - Gold
            Colors.Tomato         // Map 4 - Red
        };
        
        /// <summary>
        /// Column layout data for multi-map mode
        /// </summary>
        private struct MapColumn
        {
            public string MapKey;
            public float ColumnX;      // Left edge of column in NoteLayer space
            public float ColumnWidth;  // Width of column
            public int LaneCount;      // Number of lanes in this column
            public Color Color;        // Column tint color
            public ColorRect DimOverlay; // Overlay to dim inactive columns during play
        }
        private MapColumn[] _mapColumns;
        private bool _isMultiColumnMode = false;
        
        [ExportCategory("Multi-Map")]
        [Export] public float InactiveColumnDim { get; set; } = 0.5f; // Dim alpha for inactive columns

        // --- Optimisation: Layout Cache ---
        // We store the calculated X positions here to avoid GlobalPosition calls in Update
        private struct LaneLayout
        {
            public float LocalX;
            public float Width;
            public Color Color;
        }
        private LaneLayout[] _laneCache;        
        // Offset from NoteLayer to GhostLayer for coordinate conversion
        private Vector2 _noteToGhostOffset;
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
            
            // Setup map colors for multi-map mode
            SetupMapColors();
            
            // Pass 'this' (TimelineView) as event source since _GuiInput receives coords in our local space
            _input.Initialise(context, this, NoteLayer, PixelsPerSecond);
            _context.OnSelectionChanged += HandleExternalSelectionInfo;

            // Determine mode and setup accordingly
            _isMultiColumnMode = context.IsMultiMapMode && context.LoadedMaps.Count > 1;
            
            // Adjust aspect ratio for multi-column mode
            AdjustAspectRatioForMode();
            
            if (_isMultiColumnMode)
            {
                SetupMultiColumnLanes();
            }
            else
            {
                // Single map mode - use max lanes
                int lanes = GetMaxLanesAcrossMaps();
                context.MaxLanes = lanes;
                SetupLanes(lanes);
                _mapColumns = null;
            }

            // 2. Reset
            ResetVisuals();
            _spawnStartIndex = 0;
            
        }
        
        private void SetupMapColors()
        {
            _mapColors.Clear();
            if (_context.IsMultiMapMode)
            {
                int i = 0;
                foreach (var key in _context.LoadedMaps.Keys)
                {
                    _mapColors[key] = MapColorPalette[i % MapColorPalette.Length];
                    i++;
                }
            }
        }
        
        /// <summary>
        /// Adjust the AspectRatioContainer ratio based on mode.
        /// Multi-column mode needs wider ratio to fit all columns.
        /// </summary>
        private void AdjustAspectRatioForMode()
        {
            // Find the AspectRatioContainer (parent of Highway)
            if (Highway?.GetParent() is AspectRatioContainer arc)
            {
                if (_isMultiColumnMode)
                {
                    // Wider ratio for multi-column (e.g., 4 columns)
                    int mapCount = _context.LoadedMaps.Count;
                    // Each column at ~0.5 ratio, so 4 columns = 2.0 ratio
                    arc.Ratio = 0.5f * mapCount;
                }
                else
                {
                    // Default single-column ratio
                    arc.Ratio = 0.6f;
                }
            }
        }
        
        /// <summary>
        /// Setup multiple columns for multi-map comparison mode.
        /// Each map gets its own column with its own lanes.
        /// </summary>
        private void SetupMultiColumnLanes()
        {
            // Clear existing lanes
            foreach (Node n in LaneContainer.GetChildren()) n.QueueFree();
            
            var mapKeys = _context.LoadedMaps.Keys.ToList();
            int mapCount = mapKeys.Count;
            _mapColumns = new MapColumn[mapCount];
            
            Color laneBgColor = Color.Color8(20, 20, 25);
            Color borderColor = Color.Color8(35, 35, 45);
            Color columnDivider = Color.Color8(60, 60, 70);
            
            // Create column containers with separators
            for (int m = 0; m < mapCount; m++)
            {
                string mapKey = mapKeys[m];
                var map = _context.LoadedMaps[mapKey];
                int lanes = map.LaneCount > 0 ? map.LaneCount : 4;
                Color colColor = _mapColors.TryGetValue(mapKey, out var c) ? c : Colors.White;
                
                // Add column separator (except for first)
                if (m > 0)
                {
                    var sep = new ColorRect
                    {
                        Name = $"ColumnSep_{m}",
                        Color = columnDivider,
                        CustomMinimumSize = new Vector2(4, 0),
                        SizeFlagsVertical = SizeFlags.ExpandFill,
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    LaneContainer.AddChild(sep);
                }
                
                // Create a container for this column's lanes
                var columnContainer = new HBoxContainer
                {
                    Name = $"Column_{mapKey}",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill
                };
                columnContainer.AddThemeConstantOverride("separation", 2);
                
                // Add header label
                var header = new Label
                {
                    Name = "Header",
                    Text = mapKey,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Position = new Vector2(0, 5),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                header.AddThemeColorOverride("font_color", colColor);
                
                // Create lanes within this column
                for (int i = 0; i < lanes; i++)
                {
                    var lane = new Control
                    {
                        Name = $"Lane_{m}_{i}",
                        SizeFlagsHorizontal = SizeFlags.ExpandFill,
                        MouseFilter = MouseFilterEnum.Pass
                    };
                    
                    // Background with slight column tint
                    var bg = new ColorRect
                    {
                        Color = new Color(laneBgColor.R + colColor.R * 0.03f, 
                                         laneBgColor.G + colColor.G * 0.03f, 
                                         laneBgColor.B + colColor.B * 0.03f, 1),
                        LayoutMode = 1,
                        AnchorsPreset = (int)LayoutPreset.FullRect,
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    lane.AddChild(bg);
                    
                    // Left border
                    var leftBorder = new ColorRect
                    {
                        Color = borderColor,
                        CustomMinimumSize = new Vector2(1, 0),
                        LayoutMode = 1,
                        AnchorLeft = 0, AnchorRight = 0,
                        AnchorTop = 0, AnchorBottom = 1,
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    lane.AddChild(leftBorder);
                    
                    // Right border on last lane
                    if (i == lanes - 1)
                    {
                        var rightBorder = new ColorRect
                        {
                            Color = borderColor,
                            CustomMinimumSize = new Vector2(1, 0),
                            LayoutMode = 1,
                            AnchorLeft = 1, AnchorRight = 1,
                            AnchorTop = 0, AnchorBottom = 1,
                            GrowHorizontal = GrowDirection.Begin,
                            MouseFilter = MouseFilterEnum.Ignore
                        };
                        lane.AddChild(rightBorder);
                    }
                    
                    columnContainer.AddChild(lane);
                }
                
                LaneContainer.AddChild(columnContainer);
                
                // Create dim overlay for this column in NoteLayer (not HBoxContainer)
                // Will be positioned in RefreshMultiColumnCache
                var dimOverlay = new ColorRect
                {
                    Name = $"DimOverlay_{mapKey}",
                    Color = new Color(0, 0, 0, InactiveColumnDim),
                    Visible = false,
                    ZIndex = 100, // Ensure overlay renders on top of notes
                    MouseFilter = MouseFilterEnum.Ignore // Critical: don't block marquee selection
                };
                NoteLayer.AddChild(dimOverlay);
                
                // Store column metadata (will be populated in RefreshMultiColumnCache)
                _mapColumns[m] = new MapColumn
                {
                    MapKey = mapKey,
                    LaneCount = lanes,
                    Color = colColor,
                    DimOverlay = dimOverlay
                };
            }
            
            // Force cache refresh
            CallDeferred(nameof(RefreshMultiColumnCache));
        }
        
        private int GetMaxLanesAcrossMaps()
        {
            int maxLanes = 0;
            
            foreach (var map in _context.LoadedMaps.Values)
            {
                int lanes = map.LaneCount;
                
                // Validate against actual note data
                foreach (var note in map.Notes)
                {
                    if (note.IsVisual && note.Lane + 1 > lanes)
                        lanes = note.Lane + 1;
                }
                
                maxLanes = Math.Max(maxLanes, lanes);
            }
            
            return maxLanes > 0 ? maxLanes : 4; // Default to 4 lanes
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
            
            // Update dim overlays for multi-map mode (only dim during playback)
            UpdateColumnDimOverlays();

            Tick(_context.PlaybackTime);
        }
        
        /// <summary>
        /// Show dim overlay on inactive map columns during playback
        /// </summary>
        private void UpdateColumnDimOverlays()
        {
            if (!_isMultiColumnMode || _mapColumns == null) return;
            
            bool isPlaying = _context.IsPlaying;
            string activeKey = _context.ActiveMapKey;
            
            for (int i = 0; i < _mapColumns.Length; i++)
            {
                var overlay = _mapColumns[i].DimOverlay;
                if (overlay == null) continue;
                
                // Show dim overlay only during playback and only for non-active maps
                bool shouldDim = isPlaying && _mapColumns[i].MapKey != activeKey;
                overlay.Visible = shouldDim;
            }
        }
        
        private Rect2 _lastLaneRect;
        private float _lastLayerX;

        public void Tick(float time)
        {
            if (_context == null) return;

            // --- DIRTY CHECK ---
            // check if the container Resized OR Moved.
            Rect2 currentLaneRect = LaneContainer.GetGlobalRect();
            float currentLayerX = NoteLayer.GlobalPosition.X;

            // Check if Rect changed (Position OR Size) OR if NoteLayer moved
            bool isDirty = currentLaneRect != _lastLaneRect || 
                        Math.Abs(currentLayerX - _lastLayerX) > 0.01f;

            if (isDirty)
            {
                if (_isMultiColumnMode)
                    RefreshMultiColumnCache();
                else
                    RefreshLaneCache();
                
                // Update trackers
                _lastLaneRect = currentLaneRect;
                _lastLayerX = currentLayerX;
            }

            // Calculate visible time window based on screen dimensions and scroll speed
            float pps = _context.ScrollSpeed > 0 ? _context.ScrollSpeed : PixelsPerSecond;
            float hitY = Highway.Size.Y - HitLineOffset;
            
            // Time visible above hit line (notes approaching)
            float lookAhead = hitY / pps;
            // Time visible below hit line (notes passed) + small buffer
            float lookBehind = (Highway.Size.Y - hitY + 50f) / pps;
            
            float start = time - lookBehind;
            float end = time + lookAhead;

            // 1. Despawn notes outside visible window
            _despawnCache.Clear();
            foreach (var kvp in _activeVisuals)
            {
                if (kvp.Key.Time < start || kvp.Key.Time > end)
                    _despawnCache.Add(kvp.Key);
            }
            foreach (var note in _despawnCache) DespawnNote(note);

            // 2. Spawn notes from all loaded maps
            if (_context.IsMultiMapMode)
            {
                SpawnNotesMultiMap(start, end);
            }
            else
            {
                SpawnNotesSingleMap(start, end);
            }

            // 3. Layout Update
            GhostLayer.Clear();
            
            foreach (var kvp in _activeVisuals)
            {
                // Pass the cached array, not the Scene Nodes
                LayoutNote(kvp.Value, kvp.Key, time, hitY);
            }
            
            UpdateGhostsAndOverlays(time, hitY);

            GhostLayer.Commit();
        }
        
        private void SpawnNotesSingleMap(float start, float end)
        {
            if (_currentMap == null) return;
            
            var notes = _currentMap.Notes;
            int count = notes.Count;
            if (count == 0) return;
            
            if (_spawnStartIndex >= count) _spawnStartIndex = count - 1;
            while (_spawnStartIndex > 0 && notes[_spawnStartIndex].Time > start) _spawnStartIndex--;
            while (_spawnStartIndex < count && notes[_spawnStartIndex].Time < start) _spawnStartIndex++;

            for (int i = _spawnStartIndex; i < count; i++)
            {
                var note = notes[i];
                if (note.Time > end) break;
                if (!note.IsVisual) continue;
                if (!_activeVisuals.ContainsKey(note)) SpawnNote(note, null);
            }
        }
        
        /// <summary>
        /// Track spawn indices per map for multi-map mode
        /// </summary>
        private Dictionary<string, int> _multiMapSpawnIndices = new();
        
        /// <summary>
        /// Track which map each note belongs to for multi-column layout
        /// </summary>
        private Dictionary<NoteEvent, string> _noteToMapKey = new();
        
        private void SpawnNotesMultiMap(float start, float end)
        {
            foreach (var kvp in _context.LoadedMaps)
            {
                string mapKey = kvp.Key;
                var map = kvp.Value;
                var notes = map.Notes;
                int count = notes.Count;
                if (count == 0) continue;
                
                // Get or initialize spawn index for this map
                if (!_multiMapSpawnIndices.TryGetValue(mapKey, out int spawnIndex))
                {
                    spawnIndex = 0;
                    _multiMapSpawnIndices[mapKey] = spawnIndex;
                }
                
                // Adjust spawn index
                if (spawnIndex >= count) spawnIndex = count - 1;
                while (spawnIndex > 0 && notes[spawnIndex].Time > start) spawnIndex--;
                while (spawnIndex < count && notes[spawnIndex].Time < start) spawnIndex++;

                for (int i = spawnIndex; i < count; i++)
                {
                    var note = notes[i];
                    if (note.Time > end) break;
                    if (!note.IsVisual) continue;
                    if (!_activeVisuals.ContainsKey(note)) SpawnNote(note, mapKey);
                }
                
                _multiMapSpawnIndices[mapKey] = spawnIndex;
            }
        }
        
        // --- Internal Logic ---

        private void SpawnNote(NoteEvent data, string mapKey)
        {
            var vis = _notePool.Rent();
            
            // Get color based on map or source
            Color color;
            if (mapKey != null && _mapColors.TryGetValue(mapKey, out Color mapColor))
            {
                color = mapColor;
            }
            else
            {
                color = GetSourceColor(data.Source);
            }
            
            vis.Bind(data, color);
            vis.OnInput += HandleNoteInput;
            vis.OnDrag += HandleNoteDrag;
            vis.OnDragEnd += HandleNoteDragEnd;
            
            _activeVisuals[data] = vis;
            
            // Track which map this note belongs to for multi-column layout
            if (mapKey != null)
            {
                _noteToMapKey[data] = mapKey;
                // Also ensure note's MapKey is synced (for edit operations)
                if (string.IsNullOrEmpty(data.MapKey))
                {
                    data.MapKey = mapKey;
                }
            }
            
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
            else
            {
                // Clicked empty space - clear selection
                _context.ClearSelection();
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
                _noteToMapKey.Remove(data);
                
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
            Rect2 rect;
            
            if (_isMultiColumnMode && _noteToMapKey.TryGetValue(data, out string mapKey))
            {
                rect = CalculateNoteRectMultiColumn(mapKey, data.Lane, data.Time, data.Duration, time, hitY);
            }
            else
            {
                rect = CalculateNoteRect(data.Lane, data.Time, data.Duration, time, hitY);
            }
            
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
        
        /// <summary>
        /// Calculate note rect within a specific map column for multi-map mode
        /// </summary>
        private Rect2 CalculateNoteRectMultiColumn(string mapKey, int lane, float noteTime, float duration, float refTime, float hitY)
        {
            if (_mapColumns == null) return new Rect2();
            
            float pps = _context.ScrollSpeed;
            float timeDiff = noteTime - refTime;
            float yPos = hitY - (timeDiff * pps);
            float h = Math.Max(15f, duration * pps);
            
            // Find the column for this map
            MapColumn? column = null;
            for (int i = 0; i < _mapColumns.Length; i++)
            {
                if (_mapColumns[i].MapKey == mapKey)
                {
                    column = _mapColumns[i];
                    break;
                }
            }
            
            if (column == null) return new Rect2();
            
            var col = column.Value;
            int laneCount = col.LaneCount > 0 ? col.LaneCount : 4;
            
            // Calculate lane position within column
            float laneWidth = col.ColumnWidth / laneCount;
            float laneX = col.ColumnX + (lane * laneWidth);
            
            float noteW = laneWidth * NoteWidthPercent;
            float padX = (laneWidth - noteW) / 2;
            
            return new Rect2(laneX + padX, yPos - h, noteW, h);
        }
        
        private void UpdateGhostsAndOverlays(float time, float hitY)
        {
             const float NoteHeadHeight = 20f; // Match NoteObject.tscn Head height
             
             // Iterate Dirty List
             foreach(var note in _context.GetDirtyNotes())
             {
                  var original = _context.GetOriginal(note);
                  
                  // 1. Ghost Logic (Show if Moved)
                  // Ghost shows Original Position (head only, not full duration)
                  if (Mathf.Abs(note.Time - original.Time) > 0.001f || note.Lane != original.Lane)
                  {
                      Color col = GetSourceColor(original.Source);
                      col.A = 0.4f; // Ghost alpha
                      
                      // Calculate rects in NoteLayer space - use 0 duration for head-only display
                      // Use multi-column mode if applicable
                      Rect2 ghostRect, targetRect;
                      if (_isMultiColumnMode && !string.IsNullOrEmpty(note.MapKey))
                      {
                          ghostRect = CalculateNoteRectMultiColumn(note.MapKey, original.Lane, original.Time, 0f, time, hitY);
                          targetRect = CalculateNoteRectMultiColumn(note.MapKey, note.Lane, note.Time, 0f, time, hitY);
                      }
                      else
                      {
                          ghostRect = CalculateNoteRect(original.Lane, original.Time, 0f, time, hitY);
                          targetRect = CalculateNoteRect(note.Lane, note.Time, 0f, time, hitY);
                      }
                      
                      // Adjust height to match note head
                      ghostRect.Size = new Vector2(ghostRect.Size.X, NoteHeadHeight);
                      ghostRect.Position = new Vector2(ghostRect.Position.X, ghostRect.Position.Y + (15f - NoteHeadHeight)); // Adjust Y since CalculateNoteRect uses min 15
                      
                      // Adjust target rect the same way for proper center
                      targetRect.Size = new Vector2(targetRect.Size.X, NoteHeadHeight);
                      targetRect.Position = new Vector2(targetRect.Position.X, targetRect.Position.Y + (15f - NoteHeadHeight));
                      
                      // Convert to GhostLayer space by applying offset
                      ghostRect.Position += _noteToGhostOffset;
                      Vector2 targetCenter = targetRect.GetCenter() + _noteToGhostOffset;
                      
                      GhostLayer.AddGhost(ghostRect, targetCenter, col);
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
            _multiMapSpawnIndices.Clear();
            _noteToMapKey.Clear();
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
            
            // Compute offset from NoteLayer to GhostLayer for ghost coordinate conversion
            if (GhostLayer != null)
            {
                _noteToGhostOffset = NoteLayer.GlobalPosition - GhostLayer.GlobalPosition;
            }
        }
        
        /// <summary>
        /// Refresh column cache for multi-map mode.
        /// Calculates X positions and widths for each map column.
        /// </summary>
        private void RefreshMultiColumnCache()
        {
            if (_mapColumns == null || LaneContainer == null || NoteLayer == null) return;
            
            float layerGlobalX = NoteLayer.GlobalPosition.X;
            float containerGlobalX = LaneContainer.GlobalPosition.X;
            float relativeBaseX = containerGlobalX - layerGlobalX;
            float columnHeight = NoteLayer.Size.Y;
            
            int colIdx = 0;
            float currentX = relativeBaseX;
            
            foreach (Node child in LaneContainer.GetChildren())
            {
                // Skip column separators
                if (child is ColorRect) 
                {
                    currentX += ((ColorRect)child).Size.X;
                    continue;
                }
                
                if (child is HBoxContainer colContainer && colIdx < _mapColumns.Length)
                {
                    _mapColumns[colIdx].ColumnX = currentX;
                    _mapColumns[colIdx].ColumnWidth = colContainer.Size.X;
                    
                    // Position and size the dim overlay to match this column
                    var overlay = _mapColumns[colIdx].DimOverlay;
                    if (overlay != null)
                    {
                        overlay.Position = new Vector2(currentX, 0);
                        overlay.Size = new Vector2(colContainer.Size.X, columnHeight);
                    }
                    
                    currentX += colContainer.Size.X;
                    colIdx++;
                }
            }
            
            // Compute ghost offset
            if (GhostLayer != null)
            {
                _noteToGhostOffset = NoteLayer.GlobalPosition - GhostLayer.GlobalPosition;
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
                    lane.MouseFilter = MouseFilterEnum.Pass; 

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