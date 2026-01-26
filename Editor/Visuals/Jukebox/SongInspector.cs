using Godot;
using System;
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
        private string _currentSongPath; // Store the full path (res://Music/file.ogg)
        private string _selectedMapPath;
        
        public override void _Ready()
        {
            _lblSongName = GetNode<Label>("LabelSongName");
            _difficultyContainer = GetNode<HBoxContainer>("DifficultyContainer");
            _lblStats = GetNode<RichTextLabel>("LabelStats");
            _btnLoad = GetNode<Button>("ButtonLoad");
            
            _btnLoad.Pressed += OnLoadPressed;
            _btnLoad.Disabled = true;
            
            Visible = false;
        }
        
        // Updated Signature: Accepts the full path resolved by SongList
        public void Inspect(string songName, string resourcePath)
        {
            _currentSongName = songName;
            _currentSongPath = resourcePath; 
            _selectedMapPath = null;
            _btnLoad.Disabled = true;
            Visible = true;
            
            _lblSongName.Text = songName;
            _lblStats.Text = "[i]Select a difficulty...[/i]";
            
            // Clear Buttons
            foreach(var child in _difficultyContainer.GetChildren()) child.QueueFree();
            
            // Use Godot's DirAccess instead of System.IO
            // This works for both Editor and Exported builds
            string beatmapFolder = $"res://Beatmap/{songName}";
            var foundMaps = new Dictionary<string, string>();
            
            using var dir = DirAccess.Open(beatmapFolder);
            if (dir != null)
            {
                dir.ListDirBegin();
                string fileName = dir.GetNext();
                while (fileName != "")
                {
                    if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                    {
                        string dName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                        foundMaps[dName.ToUpper()] = $"{beatmapFolder}/{fileName}";
                    }
                    fileName = dir.GetNext();
                }
            }
            else
            {
                 _lblStats.Text = "[color=red]Map directory not found![/color]";
            }
            
            // Standard Order
            string[] standardDiffs = { "EASY", "NORMAL", "HARD", "ALT_HARD" };
            
            // 1. Create Standard Diffs
            foreach(var diff in standardDiffs)
            {
                bool exists = foundMaps.ContainsKey(diff);
                string path = exists ? foundMaps[diff] : null;
                CreateDiffButton(diff, path, exists);
                if(exists) foundMaps.Remove(diff);
            }
            
            // 2. Create Extras
            foreach(var entry in foundMaps)
            {
                CreateDiffButton(entry.Key, entry.Value, true);
            }
        }
        
        private void CreateDiffButton(string diffName, string path, bool enabled)
        {
            var btn = new Button
            {
                Text = diffName,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(80, 40)
            };

            if (enabled)
            {
                btn.Pressed += () => OnDifficultySelected(btn, path);
            }
            else
            {
                btn.Disabled = true;
                btn.Modulate = new Color(1, 1, 1, 0.3f);
                btn.TooltipText = "Map file missing";
            }
            
            _difficultyContainer.AddChild(btn);
        }
        
        private void OnDifficultySelected(Button selectedBtn, string mapPath)
        {
            foreach(Node child in _difficultyContainer.GetChildren())
            {
                if (child is Button b) b.SetPressedNoSignal(b == selectedBtn);
            }
            
            _selectedMapPath = mapPath;
            _btnLoad.Disabled = false;
            
            DisplayStats(mapPath);
        }
        
        private void DisplayStats(string mapPath)
        {
            try 
            {
                // Use Godot FileAccess to read res:// paths
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
                    if (root.TryGetProperty("metadata", out var meta))
                        if(meta.TryGetProperty("bpm", out var b)) bpm = b.GetDouble();
                    
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
                                if (type.GetString() == "hold") holdCount++;
                                else tapCount++;
                            }
                        }
                    }
                    
                    var ts = TimeSpan.FromSeconds(duration);
                    
                    _lblStats.Text = $"[b]BPM:[/b] {bpm}\n" +
                                     $"[b]Duration:[/b] {ts:mm\\:ss}\n" +
                                     $"[b]Total Notes:[/b] {noteCount}\n" +
                                     $" - Taps: {tapCount}\n" +
                                     $" - Holds: {holdCount}\n";
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
            
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopMusic(0.5f);
            }
            
            // Use the path stored from Inspect(), ignoring extension guessing
            SessionData.CurrentSongPath = _currentSongPath;
            SessionData.CurrentMapPath = _selectedMapPath;
            
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