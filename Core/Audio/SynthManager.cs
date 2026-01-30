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
        BakeMultiple(new[] { data });
    }
    
    /// <summary>
    /// Bake SFX for multiple beatmaps, deduplicating shared notes across difficulties
    /// </summary>
    public void BakeMultiple(IEnumerable<BeatmapData> maps)
    {
        _synthBank.Clear();
        var uniqueKeys = new HashSet<string>();
        int totalNotes = 0;
        
        GD.Print("[SynthManager] Baking in Vocal + 8-bit Instrumental Mode...");

        foreach (var data in maps)
        {
            if (data == null) continue;
            
            foreach(var note in data.Notes)
            {
                totalNotes++;
                float bucket = VocalSynthesiser.GetBucket(note.Duration);
                string source = note.Source.ToLower();
                
                // Force vocal for non-drum instruments if ForceVocal is enabled
                if (ForceVocal && !source.Contains("drum"))
                {
                    source = "vocal";
                }
                
                // Bake all pitches from AudioPool (or single Pitch fallback)
                foreach (int midi in note.GetAudioPitches())
                {
                    string key = $"{source}_{midi}_{bucket:F2}";
                    if (uniqueKeys.Add(key))
                    {
                        SFXResource res = null;
                        
                        if (source.Contains("vocal"))
                        {
                            int vIdx = (midi * 13 + 7) % 5;
                            if (midi > 80) 
                            {
                                if (vIdx == 1) vIdx = 0;
                                if (vIdx == 2) vIdx = 4;
                            }
                            var vowel = (VocalSynthesiser.VowelType)vIdx;
                            res = VocalSynthesiser.GenerateVocal(midi, vowel, bucket, VocalProfile);
                        }
                        else if (source.Contains("drum"))
                        {
                            var type = (midi % 2 == 0) ? VocalSynthesiser.InstrumentType.Kick : VocalSynthesiser.InstrumentType.Snare;
                            res = VocalSynthesiser.GenerateDrums(type);
                        }
                        else
                        {
                            res = VocalSynthesiser.GenerateInstrument(midi, bucket);
                        }

                        if (res != null)
                        {
                            _synthBank[key] = res;
                        }
                    }
                }
            }
        }
        GD.Print($"[SynthManager] Baked {_synthBank.Count} unique synth samples from {totalNotes} notes.");
    }
    
    public void Play(NoteEvent note)
    {
        if (note.State == NoteEvent.NoteState.Deleted) return;

        float bucket = VocalSynthesiser.GetBucket(note.Duration);
        string source = note.Source.ToLower();
        
        if (ForceVocal && !source.Contains("drum"))
        {
            source = "vocal";
        }
        
        // Play all pitches from AudioPool for polyphonic playback
        foreach (int midi in note.GetAudioPitches())
        {
            string key = $"{source}_{midi}_{bucket:F2}";
            
            if (_synthBank.TryGetValue(key, out var res))
            {
                AudioManager.Instance.PlaySFX(res);
            }
        }
    }
}