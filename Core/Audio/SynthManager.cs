using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Models;
using AudioSystem;

namespace RhythmBeatmapEditor.Core.Audio;

public partial class SynthManager : Node
{
    [Export] public bool ForceVocal { get; set; } = false;
    [Export] public VocalSynthesiser.VocalCharacter VocalProfile { get; set; } = VocalSynthesiser.VocalCharacter.Crystal;
    private Dictionary<string, SFXResource> _synthBank = new();

    public void Bake(BeatmapData data)
    {
        _synthBank.Clear();
        var uniqueKeys = new HashSet<string>();
        
        GD.Print("[SynthManager] Baking in Vocal + 8-bit Instrumental Mode...");

        foreach(var note in data.Notes)
        {
            float bucket = VocalSynthesiser.GetBucket(note.Duration);
            int midi = (int)note.Pitch;
            string source = note.Source.ToLower();
            
            // Force vocal for non-drum instruments if ForceVocal is enabled
            if (ForceVocal && !source.Contains("drum"))
            {
                source = "vocal";
            }
            
            string key = $"{source}_{midi}_{bucket:F2}";
            if (uniqueKeys.Add(key))
            {
                SFXResource res = null;
                
                if (source.Contains("vocal"))
                {
                    // 1. High Quality Vocal Synthesis
                    int vIdx = (midi * 13 + 7) % 5;

                    // --- HIGH PITCH FIX ---
                    if (midi > 80) 
                    {
                        if (vIdx == 1) vIdx = 0; // Swap I -> A
                        if (vIdx == 2) vIdx = 4; // Swap U -> O
                    }
                    // ---------------------------------------------

                    var vowel = (VocalSynthesiser.VowelType)vIdx;
                    res = VocalSynthesiser.GenerateVocal(midi, vowel, bucket, VocalProfile);
                }
                else if (source.Contains("drum"))
                {
                    // 2. 8-bit Drums (Kick/Snare)
                    var type = (midi % 2 == 0) ? VocalSynthesiser.InstrumentType.Kick : VocalSynthesiser.InstrumentType.Snare;
                    res = VocalSynthesiser.GenerateDrums(type);
                }
                else
                {
                    // 3. All other instruments -> 8-bit Square Wave
                    // This covers Piano, Guitar, Bass, and Other
                    res = VocalSynthesiser.GenerateInstrument(midi, bucket);
                }

                if (res != null)
                {
                    _synthBank[key] = res;
                }
            }
        }
        GD.Print($"[SynthManager] Baked {_synthBank.Count} unique synth samples.");
    }
    
    public void Play(NoteEvent note)
    {
        if (note.State == NoteEvent.NoteState.Deleted) return;

        float bucket = VocalSynthesiser.GetBucket(note.Duration);
        int midi = (int)note.Pitch;
        string source = note.Source.ToLower();
        
        // Force vocal for non-drum instruments if ForceVocal is enabled
        if (ForceVocal && !source.Contains("drum"))
        {
            source = "vocal";
        }
        
        string key = $"{source}_{midi}_{bucket:F2}";
        
        if (_synthBank.TryGetValue(key, out var res))
        {
            AudioManager.Instance.PlaySFX(res);
        }
    }
}