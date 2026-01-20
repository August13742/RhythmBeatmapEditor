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
        [Export] public bool ForceVocalSFX = false;
        [Export] public TimelineController TimelineUI { get; set; }
        [Export] public NoteInspector Inspector { get; set; }
        [Export] public EditorStateManager StateManager { get; set; }
        [Export] public SongControlPanel SongPanel{get;private set;}
        private EditorContext _context;
        
        private int _noteIndex = 0;
        private Core.Audio.SynthManager _synth;
        private NodePath jukeboxPath = "uid://jukebox";

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
            if(ForceVocalSFX)_synth.ForceVocal = ForceVocalSFX;
            AddChild(_synth); 

            // 1.5 Valdiate UI Overlays
            if(SongPanel == null) 
            {
                 // Assuming Scene Workflow, this should be set in Inspector.
                 // If not, we warn.
                 GD.PrintErr("[MapController] Song Control Panel not assigned!");
            }

            // 2. Load Content
            CallDeferred(nameof(LoadContent));
        }

        private void LoadContent()
        {
            string songPath, mapPath;

            // Check SessionData
            if (!string.IsNullOrEmpty(Core.SessionData.CurrentSongPath))
            {
                songPath = Core.SessionData.CurrentSongPath;
                mapPath = Core.SessionData.CurrentMapPath;
                GD.Print($"[MapController] Loading from Session: {songPath}");
            }
            else
            {
                // Error: No session data (direct launch not supported for now unless mocked, but we prefer Jukebox flow)
                GD.PrintErr("[MapController] No Session Data! Returning to Jukebox.");
                Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
                return;
            }
            
            // Audio
            if (File.Exists(songPath))
            {
                 _context.AudioController.LoadSong(songPath);
            }
            else
            {
                 GD.PrintErr($"[Error] Song not found: {songPath}");
                 Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
                 return;
            }
            
            // Map
            if (File.Exists(mapPath))
            {
                string json = File.ReadAllText(mapPath);
                _context.LoadBeatmapJSON(json);
            }
            else
            {
                GD.PrintErr($"[Error] Map not found: {mapPath}");
                Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
            }
        }
        
        private void OnBeatmapLoaded()
        {
             _synth.Bake(_context.CurrentBeatmap);
             TimelineUI.Initialise(_context); 
             Inspector?.Initialise(_context); 
             StateManager?.Initialise(_context); 
             SongPanel?.Initialise(_context); 
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