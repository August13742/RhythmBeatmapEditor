using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Editor;
using System.IO;
using AudioSystem;
using RhythmBeatmapEditor.Editor.Visuals;

namespace RhythmBeatmapEditor
{
    public partial class MapController : Node
    {
        [Export] public TimelineController TimelineUI { get; set; }

        private EditorContext _context;
        
        // Paths (Hardcoded for prototype)
        private const string TEST_SONG_PATH_RELATIVE = "res://TestData/Music/betelgeuse.mp3";
        private const string TEST_MAP_PATH_RELATIVE = "res://TestData/Beatmap/HARD.json";
        private int _noteIndex = 0;
        private Dictionary<string, SFXResource> _synthBank = new();

        public override void _Ready()
        {
            GD.Print("[MapController] Starting...");

            // 0. Validation
            if (TimelineUI == null)
            {
                GD.PrintErr("[MapController] TimelineUI is not assigned! Please assign it in the Inspector.");
                SetProcess(false);
                return;
            }

            // 1. Systems Setup
            // Ensure AudioManager exists
            if (AudioManager.Instance == null) 
            {
                var am = new AudioManager { Name = "AudioManager" };
                AddChild(am);
            }
            
            // Setup Logic Context
            _context = new EditorContext { Name = "EditorContext", ScrollSpeed = 600f };
            AddChild(_context);

            // Connect Signals
            _context.BeatmapLoaded += OnBeatmapLoaded; 

            // 1.5 Setup UI Overlays (Song Control Panel)
            SetupHUD();

            // 2. Load Content
            CallDeferred(nameof(LoadContent));
        }

        private void LoadContent()
        {
            // Globalize paths so System.IO can read them
            string absSongPath = ProjectSettings.GlobalizePath(TEST_SONG_PATH_RELATIVE);
            string absMapPath = ProjectSettings.GlobalizePath(TEST_MAP_PATH_RELATIVE);
            GD.Print($"[MapController] Loading Song: {absSongPath}");
            
            // Audio (Ensure your AudioController handles absolute paths)
            _context.AudioController.LoadSong(absSongPath);
            
            // Map
            if (File.Exists(absMapPath))
            {
                string json = File.ReadAllText(absMapPath);
                _context.LoadBeatmapJSON(json);
            }
            else
            {
                GD.PrintErr($"[Error] Map not found: {absMapPath}");
            }
        }
        
        private void SetupHUD()
        {
            var layer = new CanvasLayer { Name = "HUDLayer", Layer = 10 };
            AddChild(layer);
            
            // Load from Scene
            var scene = GD.Load<PackedScene>("res://Editor/Visuals/SongControlPanel.tscn");
            var panel = scene.Instantiate<SongControlPanel>();
            panel.Name = "SongControlPanel";
            layer.AddChild(panel);
            
            // Layout: Top Bar with some padding
            panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
            panel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            panel.Position = new Vector2(0, 0); 
            panel.CustomMinimumSize = new Vector2(0, 60); 
            
            // Initialize
            panel.Initialise(_context);
        }
        
        private void OnBeatmapLoaded()
        {
             BakeSynthBank();
             TimelineUI.Initialise(_context); 
        }

        public override void _Process(double delta)
        {
            if (!_context.IsPlaying) return;

            float time = _context.PlaybackTime;
            
            // 1. Update UI (Stateless)
            TimelineUI.Tick(time);

            // 2. Audio Triggers (Stateful - one shot)
            // Note: If we rewind, _noteIndex needs to reset.
            var notes = _context.CurrentBeatmap.Notes;
            
            // Handle Rewind detection simply
            if (_noteIndex > 0 && notes[_noteIndex - 1].Time > time)
            {
                // We jumped back. Reset index.
                while(_noteIndex > 0 && notes[_noteIndex-1].Time > time) _noteIndex--;
            }

            while (_noteIndex < notes.Count)
            {
                var note = notes[_noteIndex];
                // Check if we just passed it
                if (note.Time <= time)
                {
                    // Only play if we are within a reasonable window
                    if (time - note.Time < 0.1f) PlayNoteSFX(note);
                    _noteIndex++;
                }
                else break;
            }
        }

        private void BakeSynthBank()
        {
            if (_context.CurrentBeatmap == null) return;
            
            GD.Print("[MapController] Baking Synths...");
            var notes = _context.CurrentBeatmap.Notes;
            
            foreach (var note in notes)
            {
                float bucket = VocalSynthesiser.GetBucket(note.Duration);
                int midi = (int)note.Pitch;
                string source = note.Source.ToLower();
                string key = $"{source}_{midi}_{bucket:F2}";
                
                if (!_synthBank.ContainsKey(key))
                {
                    SFXResource res = null;
                    
                    if (source.Contains("vocal"))
                    {
                        int vIdx = (midi * 13 + 7) % 5;
                        var vowel = (VocalSynthesiser.VowelType)vIdx;
                        res = VocalSynthesiser.GenerateVocal(midi, vowel, bucket, VocalSynthesiser.VocalCharacter.Power);
                    }
                    else if (source.Contains("drum"))
                    {
                        var type = (midi % 2 == 0) ? VocalSynthesiser.InstrumentType.Kick : VocalSynthesiser.InstrumentType.Snare;
                        res = VocalSynthesiser.GenerateDrums(type);
                    }
                    else if (source.Contains("piano"))
                    {
                        res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Piano, midi);
                    }
                    else if (source.Contains("guitar"))
                    {
                        res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Guitar, midi, 0f);
                    }
                    else if (source.Contains("bass"))
                    {
                        res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Bass, midi);
                    }
                    else
                    {
                        res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Square, midi);
                    }

                    if (res != null && res.Clips.Length > 0)
                    {
                        _synthBank[key] = res;
                    }
                }
            }
            GD.Print($"[MapController] Baked {_synthBank.Count} unique sounds.");
        }

        private void PlayNoteSFX(RhythmBeatmapEditor.Core.Models.NoteEvent note)
        {
            float bucket = VocalSynthesiser.GetBucket(note.Duration);
            int midi = (int)note.Pitch;
            string source = note.Source.ToLower();
            string key = $"{source}_{midi}_{bucket:F2}";
            
            if (_synthBank.TryGetValue(key, out var res))
            {
                // Stereo Panning
                float pan = (note.Lane - 1.5f) / 1.5f * 0.5f; 
                AudioManager.Instance.PlaySFX(res);
                GD.Print($"[MapController] Hit: {key} @ {note.Time:F2}s");
            }
        }
        
        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventKey k && k.Pressed)
            {
                if (k.Keycode == Key.Space) _context.TogglePlay();
                if (k.Keycode == Key.R)
                {
                    _context.Seek(0);
                    _noteIndex = 0;
                }
            }
        }
    }
}