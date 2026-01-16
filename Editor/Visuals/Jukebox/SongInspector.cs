using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using RhythmBeatmapEditor.Core;

namespace RhythmBeatmapEditor.Editor.Visuals.Jukebox
{
    public partial class SongInspector : VBoxContainer
    {
        [Export] public PackedScene VisualiserScene { get; set; }
        
        private Label _lblSongName;
        private HBoxContainer _difficultyContainer;
        private RichTextLabel _lblStats;
        private Button _btnLoad;
        
        private string _currentSongName;
        private string _selectedMapPath;
        
        // Helper class for lightweight JSON parsing
        private class BeatmapMetadata
        {
            public float bpm { get; set; }
            public string difficulty { get; set; }
        }
        
        // Helper for raw parsing if needed, but Godot's JSON or System.Text.Json is fine.
        // We only need to peek at the file.
        
        public override void _Ready()
        {
            _lblSongName = GetNode<Label>("LabelSongName");
            _difficultyContainer = GetNode<HBoxContainer>("DifficultyContainer");
            _lblStats = GetNode<RichTextLabel>("LabelStats");
            _btnLoad = GetNode<Button>("ButtonLoad");
            
            _btnLoad.Pressed += OnLoadPressed;
            _btnLoad.Disabled = true;
            
            // Hide by default
            Visible = false;
        }
        
        public void Inspect(string songName)
        {
            _currentSongName = songName;
            _selectedMapPath = null;
            _btnLoad.Disabled = true;
            Visible = true;
            
            _lblSongName.Text = songName;
            _lblStats.Text = "[i]Select a difficulty...[/i]";
            
            // Clear Buttons
            foreach(var child in _difficultyContainer.GetChildren()) child.QueueFree();
            
            string beatmapFolder = ProjectSettings.GlobalizePath($"res://Beatmap/{songName}");
            
            // Define Standard Order
            string[] standardDiffs = { "EASY", "NORMAL", "HARD", "ALT_HARD" };
            
            // Find Maps
            var foundMaps = new Dictionary<string, string>(); // DiffName -> Path
            if (Directory.Exists(beatmapFolder))
            {
                var files = Directory.GetFiles(beatmapFolder, "*.json");
                foreach(var f in files)
                {
                    string dName = Path.GetFileNameWithoutExtension(f);
                    foundMaps[dName.ToUpper()] = f;
                }
            }
            else
            {
                 _lblStats.Text = "[color=red]Map directory not found![/color]";
            }
            
            // 1. Create Standard Diffs (Pre-baked)
            foreach(var diff in standardDiffs)
            {
                bool exists = foundMaps.ContainsKey(diff);
                string path = exists ? foundMaps[diff] : null;
                
                CreateDiffButton(diff, path, exists);
                
                // Remove from found so we don't duplicate
                if(exists) foundMaps.Remove(diff);
            }
            
            // 2. Create Remaining Diffs (Extras)
            foreach(var entry in foundMaps)
            {
                CreateDiffButton(entry.Key, entry.Value, true);
            }
        }
        
        private void CreateDiffButton(string diffName, string path, bool enabled)
        {
            var btn = new Button();
            btn.Text = diffName;
            btn.ToggleMode = true;
            btn.CustomMinimumSize = new Vector2(80, 40);
            
            if (enabled)
            {
                btn.Pressed += () => OnDifficultySelected(btn, path);
            }
            else
            {
                btn.Disabled = true;
                btn.Modulate = new Color(1, 1, 1, 0.3f); // Dim it
                btn.TooltipText = "Map file missing";
            }
            
            _difficultyContainer.AddChild(btn);
        }
        
        private void OnDifficultySelected(Button selectedBtn, string mapPath)
        {
            // Update UI Selection (Single selection logic)
            foreach(Node child in _difficultyContainer.GetChildren())
            {
                if (child is Button b)
                {
                    b.SetPressedNoSignal(b == selectedBtn);
                }
            }
            
            _selectedMapPath = mapPath;
            _btnLoad.Disabled = false;
            
            // Generate Stats
            DisplayStats(mapPath);
        }
        
        private void DisplayStats(string mapPath)
        {
            try 
            {
                string jsonContent = File.ReadAllText(mapPath);
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    var root = doc.RootElement;
                    
                    // Metadata
                    double bpm = 0;
                    if (root.TryGetProperty("metadata", out var meta))
                    {
                         if(meta.TryGetProperty("bpm", out var b)) bpm = b.GetDouble();
                    }
                    
                    // Notes
                    int noteCount = 0;
                    double duration = 0;
                    int holdCount = 0;
                    int tapCount = 0;
                    
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
                                else tapCount++;
                            }
                        }
                    }
                    
                    var ts = TimeSpan.FromSeconds(duration);
                    string durationStr = $"{ts.Minutes:D2}:{ts.Seconds:D2}";
                    
                    string text = $"[b]BPM:[/b] {bpm}\n";
                    text += $"[b]Duration:[/b] {durationStr}\n";
                    text += $"[b]Total Notes:[/b] {noteCount}\n";
                    text += $" - Taps: {tapCount}\n";
                    text += $" - Holds: {holdCount}\n";
                    
                    _lblStats.Text = text;
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
            if (string.IsNullOrEmpty(_selectedMapPath)) return;
            
            string songPath = ProjectSettings.GlobalizePath($"res://Music/{_currentSongName}.mp3");
            
            // Set Session Data
            SessionData.CurrentSongPath = songPath;
            SessionData.CurrentMapPath = _selectedMapPath;
            
            // Change Scene
            if (VisualiserScene != null)
            {
                GetTree().ChangeSceneToPacked(VisualiserScene);
            }
            else
            {
                 // Fallback based on typical path
                 GetTree().ChangeSceneToFile("uid://57yv48lyutsu");
            }
        }
    }
}
