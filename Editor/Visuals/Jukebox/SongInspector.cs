using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using RhythmBeatmapEditor.Core;

namespace RhythmBeatmapEditor.Editor.Visuals.Jukebox
{
    /// <summary>
    /// Parsed beatmap file info from filename.
    /// </summary>
    public struct BeatmapFileInfo
    {
        public string Difficulty;
        public int Lanes;
        public string FilePath;
        
        /// <summary>
        /// Parses filename like "HARD_4k.json" into difficulty and lane count.
        /// </summary>
        public static BeatmapFileInfo Parse(string fileName, string filePath)
        {
            var info = new BeatmapFileInfo { FilePath = filePath, Lanes = 4 };
            
            string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            
            // Pattern: {DIFF}_{N}k  e.g. HARD_4k, ALT_HARD_6k
            var match = Regex.Match(name, @"^(.+?)_(\d+)k$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                info.Difficulty = match.Groups[1].Value.ToUpper();
                info.Lanes = int.Parse(match.Groups[2].Value);
            }
            else
            {
                // Fallback: whole name is difficulty, default 4 lanes
                info.Difficulty = name.ToUpper();
            }
            
            return info;
        }
        
        public string DisplayName => Lanes != 4 ? $"{Difficulty} ({Lanes}K)" : Difficulty;
    }

    public partial class SongInspector : VBoxContainer
    {
        [Export] public PackedScene VisualiserScene { get; set; }
        
        private Label _lblSongName;
        private VBoxContainer _selectionContainer; // Changed to VBox for vertical layout
        private RichTextLabel _lblStats;
        private Button _btnLoad;
        private CheckBox _chkCompareMode;
        
        private string _currentSongName;
        private string _currentSongPath;
        private BeatmapFileInfo? _selectedMap;
        
        // Multi-select state
        private bool _isCompareMode = false;
        private List<BeatmapFileInfo> _selectedMaps = new();
        private const int MaxCompareCount = 4;
        
        // Lane group state
        private Dictionary<int, List<BeatmapFileInfo>> _mapsByLanes = new();
        private int _expandedLaneGroup = -1;
        private HBoxContainer _difficultyRow; // Row showing difficulties for expanded lane
        
        public override void _Ready()
        {
            _lblSongName = GetNode<Label>("LabelSongName");
            _selectionContainer = GetNode<VBoxContainer>("SelectionContainer");
            _lblStats = GetNode<RichTextLabel>("LabelStats");
            _btnLoad = GetNode<Button>("ButtonLoad");
            
            _btnLoad.Pressed += OnLoadPressed;
            _btnLoad.Disabled = true;
            
            // Create compare mode checkbox dynamically
            _chkCompareMode = new CheckBox
            {
                Text = "Compare Mode (max 4)",
                TooltipText = "Enable to select multiple difficulties for comparison"
            };
            _chkCompareMode.Toggled += OnCompareModeToggled;
            
            Visible = false;
        }
        
        public void Inspect(string songName, string resourcePath)
        {
            _currentSongName = songName;
            _currentSongPath = resourcePath; 
            _selectedMap = null;
            _selectedMaps.Clear();
            _btnLoad.Disabled = true;
            _expandedLaneGroup = -1;
            Visible = true;
            
            _lblSongName.Text = songName;
            _lblStats.Text = "[i]Select a difficulty...[/i]";
            UpdateLoadButton();
            
            foreach(var child in _selectionContainer.GetChildren()) child.QueueFree();
            
            string beatmapFolder = $"res://Beatmap/{songName}";
            var foundMaps = new List<BeatmapFileInfo>();
            
            using var dir = DirAccess.Open(beatmapFolder);
            if (dir != null)
            {
                dir.ListDirBegin();
                string fileName = dir.GetNext();
                while (fileName != "")
                {
                    if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                    {
                        var info = BeatmapFileInfo.Parse(fileName, $"{beatmapFolder}/{fileName}");
                        foundMaps.Add(info);
                    }
                    fileName = dir.GetNext();
                }
            }
            else
            {
                 _lblStats.Text = "[color=red]Map directory not found![/color]";
                 return;
            }
            
            // Group by lanes
            _mapsByLanes.Clear();
            foreach (var info in foundMaps)
            {
                if (!_mapsByLanes.ContainsKey(info.Lanes))
                    _mapsByLanes[info.Lanes] = new List<BeatmapFileInfo>();
                _mapsByLanes[info.Lanes].Add(info);
            }
            
            // Sort difficulties within each lane group
            string[] diffOrder = { "EASY", "NORMAL", "HARD", "ALT_HARD" };
            foreach (var list in _mapsByLanes.Values)
            {
                list.Sort((a, b) => {
                    int idxA = Array.IndexOf(diffOrder, a.Difficulty);
                    int idxB = Array.IndexOf(diffOrder, b.Difficulty);
                    if (idxA < 0) idxA = 100;
                    if (idxB < 0) idxB = 100;
                    return idxA.CompareTo(idxB);
                });
            }
            
            // Get sorted lane counts
            var laneGroups = new List<int>(_mapsByLanes.Keys);
            laneGroups.Sort();
            
            // If only one lane group exists, auto-expand it
            if (laneGroups.Count == 1)
            {
                _expandedLaneGroup = laneGroups[0];
                BuildExpandedView();
            }
            else
            {
                // Show lane selector buttons
                BuildLaneSelectorView(laneGroups);
            }
        }
        
        private void BuildLaneSelectorView(List<int> laneGroups)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            
            foreach (int lanes in laneGroups)
            {
                var btn = new Button
                {
                    Text = $"{lanes}K",
                    ToggleMode = true,
                    CustomMinimumSize = new Vector2(60, 40)
                };
                btn.Pressed += () => OnLaneGroupSelected(lanes);
                row.AddChild(btn);
            }
            
            _selectionContainer.AddChild(row);
            
            // Add placeholder for difficulty row
            _difficultyRow = new HBoxContainer();
            _difficultyRow.AddThemeConstantOverride("separation", 4);
            _selectionContainer.AddChild(_difficultyRow);
            
            // Add compare mode checkbox
            _selectionContainer.AddChild(_chkCompareMode);
        }
        
        private void OnLaneGroupSelected(int lanes)
        {
            _expandedLaneGroup = lanes;
            _selectedMap = null;
            _btnLoad.Disabled = true;
            _lblStats.Text = "[i]Select a difficulty...[/i]";
            
            // Update lane button states
            var laneRow = _selectionContainer.GetChild(0) as HBoxContainer;
            if (laneRow != null)
            {
                foreach (Node child in laneRow.GetChildren())
                {
                    if (child is Button b)
                    {
                        bool isSelected = b.Text == $"{lanes}K";
                        b.SetPressedNoSignal(isSelected);
                    }
                }
            }
            
            // Rebuild difficulty row
            foreach (var child in _difficultyRow.GetChildren()) child.QueueFree();
            
            if (_mapsByLanes.TryGetValue(lanes, out var maps))
            {
                foreach (var info in maps)
                {
                    CreateDiffButton(info, _difficultyRow);
                }
            }
        }
        
        private void BuildExpandedView()
        {
            // Single lane group - show difficulties directly
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            
            if (_mapsByLanes.TryGetValue(_expandedLaneGroup, out var maps))
            {
                foreach (var info in maps)
                {
                    CreateDiffButton(info, row);
                }
            }
            
            _selectionContainer.AddChild(row);
            _difficultyRow = row;
            
            // Add compare mode checkbox
            _selectionContainer.AddChild(_chkCompareMode);
        }
        
        private void CreateDiffButton(BeatmapFileInfo info, HBoxContainer container)
        {
            var btn = new Button
            {
                Text = info.Difficulty,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(90, 40)
            };
            
            btn.Pressed += () => OnDifficultySelected(btn, info);
            container.AddChild(btn);
        }
        
        private void OnDifficultySelected(Button selectedBtn, BeatmapFileInfo info)
        {
            if (_isCompareMode)
            {
                // Multi-select mode: toggle selection
                string key = $"{info.Difficulty}_{info.Lanes}k";
                int existingIndex = _selectedMaps.FindIndex(m => $"{m.Difficulty}_{m.Lanes}k" == key);
                
                if (existingIndex >= 0)
                {
                    // Deselect
                    _selectedMaps.RemoveAt(existingIndex);
                    selectedBtn.SetPressedNoSignal(false);
                }
                else if (_selectedMaps.Count < MaxCompareCount)
                {
                    // Select
                    _selectedMaps.Add(info);
                    selectedBtn.SetPressedNoSignal(true);
                }
                else
                {
                    // Max reached - don't allow more
                    selectedBtn.SetPressedNoSignal(false);
                    GD.Print($"[SongInspector] Maximum {MaxCompareCount} maps for compare mode.");
                }
                
                UpdateLoadButton();
                DisplayMultiStats();
            }
            else
            {
                // Single-select mode: exclusive selection
                foreach(Node child in _difficultyRow.GetChildren())
                {
                    if (child is Button b) b.SetPressedNoSignal(b == selectedBtn);
                }
                
                _selectedMap = info;
                _selectedMaps.Clear();
                _selectedMaps.Add(info);
                
                UpdateLoadButton();
                DisplayStats(info.FilePath, info.Lanes);
            }
        }
        
        private void OnCompareModeToggled(bool enabled)
        {
            _isCompareMode = enabled;
            _selectedMap = null;
            _selectedMaps.Clear();
            
            // Reset all button states
            foreach(Node child in _difficultyRow.GetChildren())
            {
                if (child is Button b) b.SetPressedNoSignal(false);
            }
            
            UpdateLoadButton();
            _lblStats.Text = enabled 
                ? "[i]Select up to 4 difficulties to compare...[/i]" 
                : "[i]Select a difficulty...[/i]";
        }
        
        private void UpdateLoadButton()
        {
            int count = _selectedMaps.Count;
            _btnLoad.Disabled = count == 0;
            
            if (_isCompareMode && count > 0)
            {
                _btnLoad.Text = $"LOAD ({count})";
            }
            else
            {
                _btnLoad.Text = "LOAD / PLAY";
            }
        }
        
        private void DisplayMultiStats()
        {
            if (_selectedMaps.Count == 0)
            {
                _lblStats.Text = "[i]Select up to 4 difficulties to compare...[/i]";
                return;
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[b]Selected Maps:[/b] {_selectedMaps.Count}/{MaxCompareCount}");
            sb.AppendLine();
            
            int totalNotes = 0;
            foreach (var map in _selectedMaps)
            {
                string key = $"{map.Difficulty}_{map.Lanes}k";
                int noteCount = GetNoteCountFast(map.FilePath);
                totalNotes += noteCount;
                sb.AppendLine($"• {key}: {noteCount} notes");
            }
            
            sb.AppendLine();
            sb.AppendLine($"[b]Total Notes:[/b] {totalNotes}");
            
            _lblStats.Text = sb.ToString();
        }
        
        private int GetNoteCountFast(string mapPath)
        {
            try
            {
                using var file = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read);
                if (file == null) return 0;
                
                string json = file.GetAsText();
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("notes", out var notes))
                {
                    return notes.GetArrayLength();
                }
            }
            catch { }
            return 0;
        }
        
        private void DisplayStats(string mapPath, int lanes)
        {
            try 
            {
                string jsonContent;
                using (var file = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read))
                {
                    if (file == null) throw new Exception($"Failed to open {mapPath}");
                    jsonContent = file.GetAsText();
                }

                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    var root = doc.RootElement;
                    
                    double bpm = 0;
                    int metaLanes = lanes;
                    if (root.TryGetProperty("metadata", out var meta))
                    {
                        if(meta.TryGetProperty("bpm", out var b)) bpm = b.GetDouble();
                        if(meta.TryGetProperty("lanes", out var l)) metaLanes = l.GetInt32();
                    }
                    
                    int noteCount = 0;
                    double duration = 0;
                    int holdCount = 0;
                    int tapCount = 0;
                    int ghostCount = 0;
                    
                    if (root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                    {
                        noteCount = notes.GetArrayLength();
                        foreach(var note in notes.EnumerateArray())
                        {
                            if (note.TryGetProperty("time", out var t))
                            {
                                double time = t.GetDouble();
                                double dur = 0;
                                if (note.TryGetProperty("dur", out var d)) dur = d.GetDouble();
                                if (time + dur > duration) duration = time + dur;
                            }
                            
                            if (note.TryGetProperty("type", out var type))
                            {
                                string typeStr = type.GetString();
                                if (typeStr == "hold") holdCount++;
                                else if (typeStr == "ghost") ghostCount++;
                                else tapCount++;
                            }
                        }
                    }
                    
                    var ts = TimeSpan.FromSeconds(duration);
                    
                    _lblStats.Text = $"[b]BPM:[/b] {bpm}\n" +
                                     $"[b]Lanes:[/b] {metaLanes}K\n" +
                                     $"[b]Duration:[/b] {ts:mm\\:ss}\n" +
                                     $"[b]Visual Notes:[/b] {tapCount + holdCount}\n" +
                                     $" - Taps: {tapCount}\n" +
                                     $" - Holds: {holdCount}\n" +
                                     (ghostCount > 0 ? $"[b]Ghost Notes:[/b] {ghostCount}\n" : "");
                }
            }
            catch(Exception e)
            {
                _lblStats.Text = $"[color=red]Error parsing map:[/color]\n{e.Message}";
                GD.PrintErr(e);
            }
        }
        
        private void OnLoadPressed()
        {
            if (_selectedMaps.Count == 0) return;
            
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopMusic(0.5f);
            }
            
            SessionData.CurrentSongPath = _currentSongPath;
            
            if (_isCompareMode && _selectedMaps.Count > 1)
            {
                // Multi-map mode: store list of paths
                var paths = new List<string>();
                foreach (var map in _selectedMaps)
                {
                    paths.Add(map.FilePath);
                }
                SessionData.CurrentMapPaths = paths;
                SessionData.CurrentMapPath = _selectedMaps[0].FilePath; // Primary map
            }
            else
            {
                // Single map mode
                SessionData.CurrentMapPath = _selectedMaps[0].FilePath;
                SessionData.CurrentMapPaths = null;
            }
            
            if (VisualiserScene != null)
            {
                Utility.CrossfadeManager.Instance.LoadScene(VisualiserScene);
            }
            else
            {
                 Utility.CrossfadeManager.Instance.LoadScene("uid://57yv48lyutsu");
            }
        }
    }
}