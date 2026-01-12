using Godot;
using RhythmBeatmapEditor.Core.Editor;
using AudioSystem;
namespace RhythmBeatmapEditor;

/// <summary>
/// Prodecural Entry Point for the Visualiser Scene.
/// usage: Attach this to the Root Node of a scene.
/// </summary>
public partial class VisualiserMain : Node
{
    private EditorContext _context;
    private RhythmBeatmapEditor.Editor.Visuals.TimelineView _view;
    
    // Config for the procedural test
    // NOTE: Update these paths to match your local setup test!
    private const string SONG_PATH = @"c:\Users\augus\Desktop\PythonHelperScripts\RhythmGameVisualiser\rhythm_engine\Music\betelgeuse.mp3";
    private const string MAP_PATH = @"c:\Users\augus\Desktop\PythonHelperScripts\RhythmGameVisualiser\rhythm_engine\stems\betelgeuse\beatmap\HARD.json";

    private int _noteIndex = 0;

    public override void _Ready()
    {
        GD.Print("[VisualiserMain] Starting Procedural Setup...");
        
        // 0. Ensure AudioManager exists (for local test scene)
        if (AudioManager.Instance == null)
        {
            var am = new AudioManager();
            am.Name = "AudioManager";
            AddChild(am);
            GD.Print("[VisualiserMain] Spawned local AudioManager instance.");
        }
        
        // 1. Create Context
        _context = new EditorContext();
        _context.Name = "EditorContext";
        // Override defaults to match Python Visualiser
        _context.ScrollSpeed = 600f; 
        AddChild(_context);
        
        // 2. Create View
        _view = new RhythmBeatmapEditor.Editor.Visuals.TimelineView();
        _view.Name = "TimelineView";
        _view.Context = _context;
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_view);
        
        // Setup SFX (Old local player removed, using AudioManager)
        // Play startup sound via AM
        var sfxRes = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Square, 60, 0f);
        if (sfxRes != null && sfxRes.Clips.Length > 0)
        {
            AudioManager.Instance.PlaySFX(sfxRes);
            GD.Print("[Visualiser] Startup SFX played via AudioManager.");
        }
        
        CallDeferred(nameof(LoadContent));
    }

    // Removed GenerateSimpleSFX in favor of VocalSynthesiser

    // Cache: Key = (SourceString, MidiPitch, DurationBucket) -> SFXResource
    // We use a simple struct key or string key. String is easier for rapid prototype.
    private System.Collections.Generic.Dictionary<string, SFXResource> _synthBank = new();
    
    private void BakeSynthBank()
    {
        if (_context.CurrentBeatmap == null) return;
        
        GD.Print("[Visualiser] Baking Synths...");
        var notes = _context.CurrentBeatmap.Notes;
        
        foreach (var note in notes)
        {
            // Determine Bucket
            float bucket = VocalSynthesiser.GetBucket(note.Duration);
            int midi = (int)note.Pitch;
            string source = note.Source.ToLower();
            
            // Unique Key
            string key = $"{source}_{midi}_{bucket:F2}";
            
            if (!_synthBank.ContainsKey(key))
            {
                SFXResource res = null;
                
                // Route to correct generator
                if (source.Contains("vocal"))
                {
                    // Select Vowel based on pitch (Mock logic from Python)
                    // vowels = ['A', 'I', 'U', 'E', 'O']
                    // v_idx = (midi * 13 + 7) % 5 
                    int vIdx = (midi * 13 + 7) % 5;
                    var vowel = (VocalSynthesiser.VowelType)vIdx;
                    
                    res = VocalSynthesiser.GenerateVocal(midi, vowel, bucket, VocalSynthesiser.VocalCharacter.Power);
                }
                else if (source.Contains("drum"))
                {
                    // Map MIDI to Type? Or just Use Kick/Snare based on pitch?
                    // Python: Mod 2. Even=Kick, Odd=Snare.
                    var type = (midi % 2 == 0) ? VocalSynthesiser.InstrumentType.Kick : VocalSynthesiser.InstrumentType.Snare;
                    res = VocalSynthesiser.GenerateDrums(type);
                }
                else if (source.Contains("piano"))
                {
                    res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Piano, midi);
                }
                else if (source.Contains("guitar"))
                {
                    res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Guitar, midi, 0f); // 0f pitch var
                }
                else if (source.Contains("bass"))
                {
                    res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Bass, midi);
                }
                else
                {
                    // Other / Fallback
                    res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Square, midi);
                }

                if (res != null && res.Clips.Length > 0)
                {
                    _synthBank[key] = res;
                }
            }
        }
        GD.Print($"[Visualiser] Baked {_synthBank.Count} unique sounds.");
    }

    public override void _Process(double delta)
    {
        if (_context == null || _context.CurrentBeatmap == null || !_context.IsPlaying) return;

        float time = _context.PlaybackTime;
        var notes = _context.CurrentBeatmap.Notes;

        // Check for hits
        while (_noteIndex < notes.Count)
        {
            var note = notes[_noteIndex];
            if (note.Time <= time)
            {
                // Hit!
                PlayNote(note);
                // GD.Print($"[Visualiser] Note Hit @ {note.Time:F2}s"); // Optional spam
                _noteIndex++;
            }
            else
            {
                break; // Notes are sorted
            }
        }
    }
    
    private void PlayNote(RhythmBeatmapEditor.Core.Models.NoteEvent note)
    {
        // Construct Key
        float bucket = VocalSynthesiser.GetBucket(note.Duration);
        int midi = (int)note.Pitch;
        string source = note.Source.ToLower();
        string key = $"{source}_{midi}_{bucket:F2}";
        
        if (_synthBank.TryGetValue(key, out var res))
        {
            // Stereo Panning based on Lane
            // Lane 0 = Left, Lane 3 = Right
            float pan = (note.Lane - 1.5f) / 1.5f * 0.5f; 
            
            // Note: If resource is Force2D or BypassSpatial, pos is ignored.
            // But we can approximate 2D panning via 3D position relative to listener or simple Play.
            // Since AudioManager handles basics, we just fire it.
            // To support panning properly in AudioManager, we'd need to set Bus or use 3D with listener.
            // For now, center channel is acceptable for verification.
            
            AudioManager.Instance.PlaySFX(res);
            
            GD.Print($"[Visualiser] Hit: {key} @ {note.Time:F2}s");
        }
        else
        {
            GD.PrintErr($"[Visualiser] MISSING KEY: {key}");
        }
    }
    


    private void LoadContent()
    {
        GD.Print("[Visualiser] LoadContent called.");
        GD.Print($"[Visualiser] Loading Song: {SONG_PATH}");
        GD.Print($"[Visualiser] Loading Map: {MAP_PATH}");
        
        // Load Audio
        _context.AudioController.LoadSong(SONG_PATH); 
        
        if (System.IO.File.Exists(MAP_PATH))
        {
            string json = System.IO.File.ReadAllText(MAP_PATH);
            _context.LoadBeatmapJSON(json);
            
            // Bake after load
            BakeSynthBank();
        }
        else
        {
            GD.PrintErr($"[VisualiserMain] Map file not found: {MAP_PATH}");
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey k && k.Pressed)
        {
            if (k.Keycode == Key.Space)
            {
                _context.TogglePlay();
            }
            if (k.Keycode == Key.R)
            {
                _context.Seek(0);
                _noteIndex = 0;
            }
        }
    }
}
