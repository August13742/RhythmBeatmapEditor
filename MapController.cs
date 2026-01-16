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
        [Export] public NoteInspector Inspector { get; set; }
        [Export] public SongControlPanel SongPanel{get;private set;}
        private EditorContext _context;
        
        // Paths (Hardcoded for prototype)
        private const string TEST_SONG_PATH_RELATIVE = "res://TestData/Music/betelgeuse.mp3";
        private const string TEST_MAP_PATH_RELATIVE = "res://TestData/Beatmap/HARD.json";
        private int _noteIndex = 0;
        private Core.Audio.SynthManager _synth;

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
            
            _synth = new Core.Audio.SynthManager { Name = "SynthManager" };
            AddChild(_synth); 

            // 1.5 Setup UI Overlays (Song Control Panel)
            if(SongPanel == null) SetupHUD();

            // 2. Load Content
            CallDeferred(nameof(LoadContent));
        }

        private void LoadContent()
        {
            // Globalize paths so System.IO can read them
            string absSongPath = ProjectSettings.GlobalizePath(TEST_SONG_PATH_RELATIVE);
            string absMapPath = ProjectSettings.GlobalizePath(TEST_MAP_PATH_RELATIVE);
            GD.Print($"[MapController] Loading Song: {absSongPath}");
            
            // Audio
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
            
            // Initialise
            panel.Initialise(_context);
        }
        
        private void OnBeatmapLoaded()
        {
             _synth.Bake(_context.CurrentBeatmap);
             TimelineUI.Initialise(_context); 
             Inspector?.Initialise(_context); 
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
                    if (time - note.Time < 0.1f) 
                    {
                         _synth.Play(note);
                    }
                    _noteIndex++;
                }
                else break;
            }
        }


        
        public override void _UnhandledInput(InputEvent @event)
        {
            if (_context == null) return;

            if (@event.IsActionPressed("editor_copy")) _context.CopySelectedNotes();
            if (@event.IsActionPressed("editor_paste")) _context.PasteNotes();
            if (@event.IsActionPressed("editor_delete")) _context.DeleteSelectedNotes();

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