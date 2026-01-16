using Godot;
using System;
using System.Collections.Generic;
using AudioSystem;

public partial class SynthTest : Control
{
    private GridContainer _grid;
    // Cache Key: "ProfileIndex_VowelIndex_Midi"
    private Dictionary<string, SFXResource> _cache = new();

    public override void _Ready()
    {
        var scroll = new ScrollContainer { LayoutMode = 1, AnchorsPreset = (int)LayoutPreset.FullRect };
        AddChild(scroll);

        var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(center);
        
        _grid = new GridContainer { Columns = 5 };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        center.AddChild(_grid);

        // 1. Generate Vocal Matrix
        AddHeader("--- Vocal Personalities ---");
        
        var characters = Enum.GetValues(typeof(VocalSynthesiser.VocalCharacter));
        var vowels = Enum.GetValues(typeof(VocalSynthesiser.VowelType));

        foreach (VocalSynthesiser.VocalCharacter charEnum in characters)
        {
            AddLabel(charEnum.ToString());
            
            // Create a button for each vowel for this character
            foreach (VocalSynthesiser.VowelType vEnum in vowels)
            {
                CreateBtn($"{vEnum}", () => PlayVocal(69, vEnum, charEnum));
            }
            
            // Fill remaining columns if grid is wider than vowels+label
            // Columns = 5. We used 1 (label) + 5 (vowels)? No, let's restructure.
            // Let's do Row: [Name] [A] [I] [U] [E] [O] -> requires 6 columns
        }
        
        // Fix grid columns for the vocal matrix
        _grid.Columns = 6;

        // 2. Instruments
        AddFullRowHeader("--- Instruments ---");
        
        CreateBtn("Kick", () => PlayDrum(VocalSynthesiser.InstrumentType.Kick));
        CreateBtn("Snare", () => PlayDrum(VocalSynthesiser.InstrumentType.Snare));
        CreateBtn("Bass", () => PlayInst(VocalSynthesiser.InstrumentType.Bass, 48));
        CreateBtn("Piano", () => PlayInst(VocalSynthesiser.InstrumentType.Piano, 60));
        CreateBtn("Square", () => PlayInst(VocalSynthesiser.InstrumentType.Square, 69));
        CreateBtn("Chip", () => PlayInst(VocalSynthesiser.InstrumentType.Other, 72));
    }

    private void PlayVocal(int midi, VocalSynthesiser.VowelType vowel, VocalSynthesiser.VocalCharacter character)
    {
        string key = $"{(int)character}_{(int)vowel}_{midi}";
        
        if (!_cache.ContainsKey(key))
        {
            // Simple visual feedback could go here (e.g. change mouse cursor)
            _cache[key] = VocalSynthesiser.GenerateVocal(midi, vowel, 0.4f, character);
        }
        AudioManager.Instance.PlaySFX(_cache[key]);
    }

    private void PlayDrum(VocalSynthesiser.InstrumentType type)
    {
        string key = $"DRUM_{type}";
        if (!_cache.ContainsKey(key))
            _cache[key] = VocalSynthesiser.GenerateDrums(type);
        AudioManager.Instance.PlaySFX(_cache[key]);
    }

    private void PlayInst(VocalSynthesiser.InstrumentType type, int midi)
    {
        string key = $"INST_{type}_{midi}";
        if (!_cache.ContainsKey(key))
            _cache[key] = VocalSynthesiser.GenerateInstrument(midi,duration:0.15f);
        AudioManager.Instance.PlaySFX(_cache[key]);
    }

    // UI Helpers
    private void CreateBtn(string text, Action onPressed)
    {
        var btn = new Button { Text = text, CustomMinimumSize = new Vector2(80, 40) };
        btn.Pressed += () => onPressed.Invoke();
        _grid.AddChild(btn);
    }

    private void AddLabel(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Right };
        _grid.AddChild(label);
    }

    private void AddHeader(string text)
    {
        // Spacer
        for(int i=0; i<6; i++) _grid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        _grid.AddChild(label);
        // Fill rest of row
        for(int i=0; i<5; i++) _grid.AddChild(new Control());
    }
    
    private void AddFullRowHeader(string text)
    {
        for(int i=0; i<6; i++) _grid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        _grid.AddChild(label);
        for(int i=0; i<5; i++) _grid.AddChild(new Control());
    }
}