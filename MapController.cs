using Godot;
using System.Collections.Generic;
using System.Linq;
using RhythmBeatmapEditor.Core.Editor;
using AudioSystem;
using RhythmBeatmapEditor.Editor.Visuals;
using GodotFileAccess = Godot.FileAccess;
using Path = System.IO.Path;

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
        
        /// <summary>
        /// Per-map playback indices for multi-map SFX (keys 1-4)
        /// </summary>
        private Dictionary<string, int> _noteIndices = new();

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
            string rawSongPath = Core.SessionData.CurrentSongPath;
            
            if (string.IsNullOrEmpty(rawSongPath))
            {
                GD.PrintErr("[MapController] No Session Data! Returning to Jukebox.");
                Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
                return;
            }
            
            GD.Print($"[MapController] Loading from Session: {rawSongPath}");
            
            // Sanitize song path
            string songPath = ProjectSettings.LocalizePath(rawSongPath);

            // Load Audio
            if (GodotFileAccess.FileExists(songPath)) 
            {
                 _context.AudioController.LoadSong(songPath);
            }
            else
            {
                 GD.PrintErr($"[Error] Song not found at localized path: {songPath} (Raw: {rawSongPath})");
                 Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
                 return;
            }
            
            // Check for multi-map or single-map mode
            if (Core.SessionData.IsMultiMapMode && Core.SessionData.CurrentMapPaths?.Count > 0)
            {
                LoadMultipleMaps();
            }
            else
            {
                LoadSingleMap();
            }
        }
        
        private void LoadSingleMap()
        {
            string rawMapPath = Core.SessionData.CurrentMapPath;
            string mapPath = ProjectSettings.LocalizePath(rawMapPath);
            
            if (GodotFileAccess.FileExists(mapPath))
            {
                using var file = GodotFileAccess.Open(mapPath, GodotFileAccess.ModeFlags.Read);
                string json = file.GetAsText();
                _context.LoadBeatmapJSON(json);
            }
            else
            {
                GD.PrintErr($"[Error] Map not found: {mapPath}");
                Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
            }
        }
        
        private void LoadMultipleMaps()
        {
            var paths = Core.SessionData.CurrentMapPaths;
            var mapJsons = new Dictionary<string, string>();
            
            foreach (var rawPath in paths)
            {
                string localPath = ProjectSettings.LocalizePath(rawPath);
                if (!GodotFileAccess.FileExists(localPath))
                {
                    GD.PrintErr($"[MapController] Map not found: {localPath}");
                    continue;
                }
                
                using var file = GodotFileAccess.Open(localPath, GodotFileAccess.ModeFlags.Read);
                string json = file.GetAsText();
                
                // Extract map key from filename (e.g., "HARD_4k.json" -> "HARD_4k")
                string mapKey = Path.GetFileNameWithoutExtension(rawPath);
                mapJsons[mapKey] = json;
            }
            
            if (mapJsons.Count == 0)
            {
                GD.PrintErr("[MapController] No valid maps found!");
                Utility.CrossfadeManager.Instance.LoadScene(jukeboxPath);
                return;
            }
            
            // Load all maps into EditorContext
            _context.LoadMultipleBeatmaps(mapJsons);
            
            // Initialize note indices for each map
            _noteIndices.Clear();
            foreach (var key in _context.LoadedMaps.Keys)
            {
                _noteIndices[key] = 0;
            }
            
            GD.Print($"[MapController] Loaded {mapJsons.Count} maps for comparison");
        }
        
        private void OnBeatmapLoaded()
        {
             // In multi-map mode, bake SFX for ALL maps (deduplicates shared notes)
             if (_context.IsMultiMapMode)
             {
                 _synth.BakeMultiple(_context.LoadedMaps.Values);
             }
             else
             {
                 _synth.Bake(_context.CurrentBeatmap);
             }
             
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

            // Play SFX for active map only
            PlaySFXForMap(_context.ActiveMapKey, time);
        }
        
        /// <summary>
        /// Play SFX for a specific map by key
        /// </summary>
        private void PlaySFXForMap(string mapKey, float time)
        {
            var map = _context.GetMap(mapKey);
            if (map == null) return;
            
            if (!_noteIndices.TryGetValue(mapKey, out int noteIndex))
            {
                noteIndex = 0;
                _noteIndices[mapKey] = noteIndex;
            }
            
            var notes = map.Notes;
            
            // Handle Rewind
            if (noteIndex > 0 && notes[noteIndex - 1].Time > time)
            {
                while (noteIndex > 0 && notes[noteIndex - 1].Time > time) noteIndex--;
            }

            while (noteIndex < notes.Count)
            {
                var note = notes[noteIndex];
                if (note.Time <= time)
                {
                    if (time - note.Time < 0.1f) _synth.Play(note);
                    noteIndex++;
                }
                else break;
            }
            
            _noteIndices[mapKey] = noteIndex;
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
                    ResetAllNoteIndices();
                }
                
                // Multi-map mode: Keys 1-4 switch active map for SFX playback
                if (_context.IsMultiMapMode)
                {
                    int mapIndex = -1;
                    if (k.Keycode == Key.Key1) mapIndex = 0;
                    else if (k.Keycode == Key.Key2) mapIndex = 1;
                    else if (k.Keycode == Key.Key3) mapIndex = 2;
                    else if (k.Keycode == Key.Key4) mapIndex = 3;
                    
                    if (mapIndex >= 0)
                    {
                        var keys = new List<string>(_context.LoadedMaps.Keys);
                        if (mapIndex < keys.Count)
                        {
                            _context.SetActiveMap(keys[mapIndex]);
                            // No need to re-bake - all maps already baked with deduplication
                            GD.Print($"[MapController] Switched to map: {keys[mapIndex]}");
                        }
                    }
                }
            }
        }
        
        private void ResetAllNoteIndices()
        {
            _noteIndex = 0;
            foreach (var key in _noteIndices.Keys.ToArray())
            {
                _noteIndices[key] = 0;
            }
        }
    }
}