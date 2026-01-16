using Godot;
using System.Collections.Generic;
using RhythmBeatmapEditor.Core.Models;
using AudioSystem;

namespace RhythmBeatmapEditor.Core.Audio;

public partial class SynthManager : Node
{
    private Dictionary<string, SFXResource> _synthBank = new();

    public void Bake(BeatmapData data)
    {
        _synthBank.Clear();
        var uniqueKeys = new HashSet<string>();
        
        foreach(var note in data.Notes)
        {
            // Simple key generation: Source + Pitch + Duration Bucket
            float bucket = VocalSynthesiser.GetBucket(note.Duration);
            int midi = (int)note.Pitch;
            string source = note.Source.ToLower();
            
            string key = $"{source}_{midi}_{bucket:F2}";
            if (uniqueKeys.Add(key))
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
        string key = $"{source}_{midi}_{bucket:F2}";
        
        if (_synthBank.TryGetValue(key, out var res))
        {
            // Stereo Panning
            float pan = (note.Lane - 1.5f) / 1.5f * 0.5f; //not used for now
            AudioManager.Instance.PlaySFX(res);
        }
    }
}
