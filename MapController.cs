using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Editor;
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
        [Export] public SongControlPanel SongPanel { get; private set; }
        
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
                GD.PrintErr("[MapController] TimelineUI is not assigned!");
                SetProcess(false);
                return;
            }

            // 1. Systems Setup
            if (AudioManager.Instance == null) 
            {
                var am = new AudioManager { Name = "AudioManager" };
                AddChild(am);
            }
            
            _context = new EditorContext { Name = "EditorContext", ScrollSpeed = 600f };
            AddChild(_context);
            _context.BeatmapLoaded += OnBeatmapLoaded; 
            
            _synth = new Core.Audio.SynthManager { Name = "SynthManager" };
            if (ForceVocalSFX) _synth.ForceVocal = ForceVocalSFX;
            AddChild(_synth); 

            if (SongPanel == null) GD.PrintErr("[MapController] Song Control Panel not assigned!");

            // 2. Load Content
            CallDeferred(nameof(LoadContent));
        }

        private void LoadContent()
        {
            string rawSongPath, rawMapPath;

            // 1. Retrieve Data
            if (!string.IsNullOrEmpty(Core.SessionData.CurrentSongPath))
            {
                rawSongPath = Core.SessionData.CurrentSongPath;
                rawMapPath = Core.SessionData.CurrentMapPath;
                GD.Print($"[MapController] Loading from Session: {rawSongPath}");
            }
            else
            {
                GD.PrintErr("[MapController] No Session Data! Returning to Jukebox.");
                Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
                return;
            }
            
            // 2. Sanitize Paths
            // Converts "E:/GodotProjects/..." back to "res://Music/..."
            // This ensures ResourceLoader works in both Editor and Exported builds.
            string songPath = ProjectSettings.LocalizePath(rawSongPath);
            string mapPath = ProjectSettings.LocalizePath(rawMapPath);

            // 3. Load Audio
            // Use Godot.FileAccess
            if (FileAccess.FileExists(songPath)) 
            {
                 _context.AudioController.LoadSong(songPath);
            }
            else
            {
                 GD.PrintErr($"[Error] Song not found at localized path: {songPath} (Raw: {rawSongPath})");
                 Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
                 return;
            }
            
            // 4. Load Map
            if (FileAccess.FileExists(mapPath))
            {
                // Use FileAccess to read text to support .pck files
                using var file = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read);
                string json = file.GetAsText();
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
            TimelineUI.Tick(time);

            var notes = _context.CurrentBeatmap.Notes;
            
            // Handle Rewind
            if (_noteIndex > 0 && notes[_noteIndex - 1].Time > time)
            {
                while(_noteIndex > 0 && notes[_noteIndex-1].Time > time) _noteIndex--;
            }

            while (_noteIndex < notes.Count)
            {
                var note = notes[_noteIndex];
                if (note.Time <= time)
                {
                    if (time - note.Time < 0.1f) _synth.Play(note);
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