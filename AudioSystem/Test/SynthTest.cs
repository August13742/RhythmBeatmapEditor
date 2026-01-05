using Godot;
using System.Collections.Generic;
using AudioSystem;

public partial class SynthTest : Control
{
    private GridContainer _grid;
    private Dictionary<string, SFXResource> _cache = new();

    public override void _Ready()
    {
        // Simple UI Setup via Code
        var center = new CenterContainer { LayoutMode = 1, AnchorsPreset = (int)LayoutPreset.FullRect };
        AddChild(center);
        
        _grid = new GridContainer { Columns = 4 };
        center.AddChild(_grid);

        // 1. Vocal Tests
        AddHeader("Vocals (Midi 60-72)");
        string[] vowels = { "A", "I", "U", "E", "O" };
        
        foreach(var v in vowels)
        {
            CreateBtn($"Vocal {v} (Mid)", () => PlayVocal(69, v));
            CreateBtn($"Vocal {v} (High)", () => PlayVocal(76, v));
        }

        // 2. Instrument Tests
        AddHeader("Instruments");
        CreateBtn("Kick Drum", () => PlayDrum(0));
        CreateBtn("Snare Drum", () => PlayDrum(1));
        CreateBtn("Bass C3", () => PlayInst("BASS", 48));
        CreateBtn("Square A4", () => PlayInst("SQUARE", 69));
        CreateBtn("Piano C4", () => PlayInst("PIANO", 60));
        CreateBtn("Other/Chiptune", () => PlayInst("OTHER", 72));
    }

    private void PlayVocal(int midi, string vowel)
    {
        string key = $"VOC_{midi}_{vowel}";
        if (!_cache.ContainsKey(key))
        {
            GD.Print($"Baking {key}...");
            _cache[key] = VocalSynthesiser.GenerateVocal(midi, vowel, 0.5f, "POWER_RIN");
        }
        AudioManager.Instance.PlaySFX(_cache[key]);
    }

    private void PlayDrum(int type)
    {
        string key = $"DRUM_{type}";
        if (!_cache.ContainsKey(key))
        {
             _cache[key] = VocalSynthesiser.GenerateDrums(type);
        }
        AudioManager.Instance.PlaySFX(_cache[key]);
    }

    private void PlayInst(string type, int midi)
    {
        string key = $"{type}_{midi}";
        if (!_cache.ContainsKey(key))
        {
             _cache[key] = VocalSynthesiser.GenerateInstrument(type, midi);
        }
        AudioManager.Instance.PlaySFX(_cache[key]);
    }

    // UI Helpers
    private void CreateBtn(string text, System.Action onPressed)
    {
        var btn = new Button { Text = text, CustomMinimumSize = new Vector2(120, 40) };
        btn.Pressed += () => onPressed.Invoke();
        _grid.AddChild(btn);
    }

    private void AddHeader(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        var spacer = new Control { CustomMinimumSize = new Vector2(0, 20) };
        _grid.AddChild(spacer); _grid.AddChild(spacer.Duplicate()); _grid.AddChild(spacer.Duplicate()); _grid.AddChild(spacer.Duplicate());
        _grid.AddChild(label);
        // Fill row
        _grid.AddChild(new Control()); _grid.AddChild(new Control()); _grid.AddChild(new Control());
    }
}