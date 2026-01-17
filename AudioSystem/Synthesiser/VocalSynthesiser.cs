using Godot;
using System;
using System.Collections.Generic;
using AudioSystem;

public static class VocalSynthesiser
{
    private const int SAMPLE_RATE = 44100;

    #region Enums & Config
    public enum VowelType { A, I, U, E, O }
    
    public enum VocalCharacter { Power, Crystal }

    public enum InstrumentType { Kick, Snare, Bass, Square, Piano, Guitar, Other }
    
    private static readonly float[] DUR_BUCKETS = { 0.25f, 0.50f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.25f, 2.5f, 3.0f, 4.0f };

    public static float GetBucket(float dur)
    {
        if (dur <= 0.15f) return 0.15f;
        float best = DUR_BUCKETS[0];
        float minDiff = Mathf.Abs(dur - best);
        foreach(var d in DUR_BUCKETS)
        {
            float diff = Mathf.Abs(dur - d);
            if (diff < minDiff) { minDiff = diff; best = d; }
        }
        return best;
    }
    #endregion

    #region Data Structures
    public struct Formant 
    {
        public float Freq; public float BW; public float GainDb;
        public Formant(float freq, float bw, float gainDb) { Freq = freq; BW = bw; GainDb = gainDb; }
    }

    public class VocalProfile
    {
        public string Name;
        public float FormantShift = 1.0f;
        public float Breathiness = 0.05f;
        public float Tension = 0.5f;
        public float MasterGain = 1.0f;
        public float VibratoAmount = 1.0f; 
    }

    private static List<Formant>[] _baseVowels; 
    private static VocalProfile[] _profiles;
    private static bool _Initialised = false;

    public static void InitialisePresets()
    {
        if (_Initialised) return;

        // 1. Setup Base Vowels (Standard Japanese Soprano)
        _baseVowels = new List<Formant>[5];
        _baseVowels[(int)VowelType.A] = new() { new(800, 80, 0), new(1200, 100, -6), new(2800, 120, -12) };
        _baseVowels[(int)VowelType.I] = new() { new(300, 50, -6), new(2300, 90, -12), new(3000, 120, -18) };
        _baseVowels[(int)VowelType.U] = new() { new(350, 60, -5), new(800, 80, -10), new(2500, 120, -18) };
        _baseVowels[(int)VowelType.E] = new() { new(500, 70, -4), new(1800, 90, -10), new(2600, 120, -16) };
        _baseVowels[(int)VowelType.O] = new() { new(500, 70, -2), new(900, 90, -8), new(2600, 120, -16) };

        // 2. Setup Profile (Using Power Rin as default for this mode)
        int profileCount = Enum.GetNames(typeof(VocalCharacter)).Length;
        _profiles = new VocalProfile[profileCount];
        _profiles[(int)VocalCharacter.Power] = new() { Name = "Power", FormantShift = 1.08f, Breathiness = 0.01f, Tension = 0.8f, MasterGain = 0.9f };
        _profiles[(int)VocalCharacter.Crystal] = new() { Name = "Crystal", FormantShift = 1.02f, Breathiness = 0.05f, Tension = 0.5f, MasterGain = 1.0f };
        _Initialised = true;
    }
    #endregion

    #region Helper Math
    private static float KlattGain(float freq, float fc, float bw)
    {
        if (freq <= 1.0f || fc <= 1.0f) return 0.0f;
        float x = freq / fc;
        float d = x * x * (bw / fc);
        float denom = (1.0f - x * x);
        denom = denom * denom + d * d;
        if (denom < 1e-6f) return 1.0f;
        return 1.0f / Mathf.Sqrt(denom);
    }

    private static float GetNoteFreq(int midi) => 440.0f * Mathf.Pow(2.0f, (midi - 69) / 12.0f);

    private static AudioStreamWav FloatsToWav(float[] samplesLeft, float[] samplesRight)
    {
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SAMPLE_RATE,
            Stereo = true
        };

        int sampleCount = samplesLeft.Length;
        byte[] bytes = new byte[sampleCount * 4];
        int byteIndex = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short sL = (short)(Mathf.Clamp(samplesLeft[i], -1f, 1f) * 32767);
            bytes[byteIndex++] = (byte)(sL & 0xFF);
            bytes[byteIndex++] = (byte)((sL >> 8) & 0xFF);

            short sR = (short)(Mathf.Clamp(samplesRight[i], -1f, 1f) * 32767);
            bytes[byteIndex++] = (byte)(sR & 0xFF);
            bytes[byteIndex++] = (byte)((sR >> 8) & 0xFF);
        }
        wav.Data = bytes;
        return wav;
    }
    #endregion

    #region Generators

    // --- 1. VOCAL GENERATOR ---
    public static SFXResource GenerateVocal(int midi, VowelType vowel, float duration = 0.25f, VocalCharacter character = VocalCharacter.Power, float pitchVariance = 0.0f, float volume = 1.0f, int lane = 2, int maxLanes = 4)
    {
        InitialisePresets();
        duration = Mathf.Max(duration, 0.35f); 

        var profile = _profiles[(int)character] ?? _profiles[0];
        var targetFormants = _baseVowels[(int)vowel];
        
        float f0 = GetNoteFreq(midi);
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];
        
        // Pre-calc Formants
        int fCount = targetFormants.Count;
        float[] f_fc = new float[fCount];
        float[] f_bw = new float[fCount];
        float[] f_gain = new float[fCount];
        for(int i = 0; i < fCount; i++) {
            f_fc[i] = targetFormants[i].Freq * profile.FormantShift;
            f_bw[i] = targetFormants[i].BW;
            f_gain[i] = Mathf.Pow(10, targetFormants[i].GainDb / 20.0f);
        }

        // Phase & Vibrato
        float[] phaseAccum = new float[totalSamples];
        float currentPhase = 0f;
        float vibRate = 5.5f;
        float vibDepth = 0.08f * profile.VibratoAmount;

        for(int i = 0; i < totalSamples; i++) {
            float t = (float)i / SAMPLE_RATE;
            float vib = Mathf.Sin(2 * Mathf.Pi * vibRate * t) * vibDepth;
            float freqMod = f0 * Mathf.Pow(2.0f, vib / 12.0f);
            currentPhase += 2 * Mathf.Pi * freqMod / SAMPLE_RATE;
            phaseAccum[i] = currentPhase;
        }

        // Additive Synthesis
        int n = 1;
        float nyquist = SAMPLE_RATE / 2f;
        float slopeBase = -12.0f + (profile.Tension * 4.0f);

        while (true) {
            float freqN = f0 * n;
            if (freqN >= nyquist || freqN > 10000) break;

            float sourceAmp = Mathf.Pow(n, slopeBase / 6.0f);
            float filtAmp = 0.0f;
            for(int k=0; k < fCount; k++) filtAmp += KlattGain(freqN, f_fc[k], f_bw[k]) * f_gain[k];

            float finalAmp = sourceAmp * filtAmp;
            if (finalAmp > 0.0001f) {
                for(int i=0; i < totalSamples; i++) wave[i] += finalAmp * Mathf.Sin(n * phaseAccum[i]);
            }
            n++;
        }

        // Breath Noise
        if (profile.Breathiness > 0.001f) {
            var rng = new Random();
            for(int i=0; i<totalSamples; i++) {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.2f; 
                float breathMod = 0.5f + 0.5f * Mathf.Cos(phaseAccum[i]);
                wave[i] += noise * breathMod * profile.Breathiness;
            }
        }

        // Envelope (Safe for short durations)
        int atk = (int)(0.01f * SAMPLE_RATE);
        int rel = (int)(0.05f * SAMPLE_RATE);
        
        for(int i=0; i<totalSamples; i++) {
            float env = 1.0f;
            if (totalSamples < rel) {
                 // If the whole note is shorter than the release, just fade out linearly
                 env = 1.0f - ((float)i / totalSamples);
            } else {
                if (i < atk) env = (float)i / atk;
                else if (i >= totalSamples - rel) env = 1.0f - ((float)(i - (totalSamples - rel)) / rel);
            }
            wave[i] *= env;
        }

        // Normalise
        float peak = 0f;
        for(int i=0; i<totalSamples; i++) if(Math.Abs(wave[i]) > peak) peak = Math.Abs(wave[i]);
        if (peak > 0.00001f) {
            float norm = (1.0f / peak) * 0.45f * profile.MasterGain;
            for(int i=0; i<totalSamples; i++) wave[i] *= norm;
        }

        return FinaliseResource(wave, volume, lane, maxLanes, pitchVariance);
    }

    // --- 8-BIT DRUMS GENERATOR ---
    public static SFXResource GenerateDrums(InstrumentType type, float volume = 1.0f, int lane = 2, int maxLanes = 4)
    {
        // Python: duration = 0.12
        float duration = 0.12f;
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];
        var rng = new Random();
        bool isKick = type == InstrumentType.Kick;
        
        // Tunable Volume
        float baseVol = InstrumentVolumes.GetValueOrDefault(type, 0.8f);

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            
            if (isKick)
            {
                // Kick: Square wave slide 100Hz -> 30Hz
                float f_start = 100.0f;
                float f_end = 30.0f;
                
                // Phase integral for linear chirp
                // phi = 2pi * (f_start * t + (f_end - f_start)/(2*dur) * t^2)
                float phase = 2 * Mathf.Pi * (f_start * t + ((f_end - f_start) / (2 * duration) * t * t));
                
                // Square wave logic
                float raw = ((phase % (2 * Mathf.Pi)) < Mathf.Pi) ? 1.0f : -1.0f;
                
                // Env: exp(-18 * t)
                wave[i] = raw * Mathf.Exp(-18f * t);
            }
            else // Snare
            {
                // Snare: White Noise
                float raw = (float)(rng.NextDouble() * 1.0 - 0.5); // Reduced amplitude range [-0.5, 0.5]
                // Env: exp(-30 * t) - Fast decay
                wave[i] = raw * Mathf.Exp(-30f * t);
            }
            
            // Reduced Global Volume for 8-bit drums
            wave[i] *= 0.3f;
        }

        return FinaliseResource(wave, volume * baseVol, lane, maxLanes);
    }

    // --- 3. BASIC INSTRUMENT GENERATOR (For 8-bit mode) ---
    public static SFXResource GenerateInstrument(int midi, float duration, float volume = 1.0f, int lane = 2, int maxLanes = 4)
    {
        float freq = GetNoteFreq(midi);
        // Ensure minimum audibility
        duration = Mathf.Max(duration, 0.15f);
        
        // Tunable Volume for 'Other' instruments
        float baseVol = InstrumentVolumes.GetValueOrDefault(InstrumentType.Guitar, 0.5f); // Use Guitar as generic placeholder or add Other
        
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            
            // Standard Square Wave
            float phase = 2 * Mathf.Pi * freq * t;
            float raw = ((phase % (2 * Mathf.Pi)) < Mathf.Pi) ? 1.0f : -1.0f;
            
            // Simple decay envelope
            wave[i] = raw * Mathf.Exp(-12f * t) * 0.3f;
        }

        return FinaliseResource(wave, volume * baseVol, lane, maxLanes);
    }
    #endregion

    #region Helpers & Finalisation
    
    // Default instrument volumes (Tunable)
    public static Dictionary<InstrumentType, float> InstrumentVolumes = new()
    {
        { InstrumentType.Kick, 0.9f },
        { InstrumentType.Snare, 0.9f },
        { InstrumentType.Bass, 0.8f },
        { InstrumentType.Piano, 0.9f },
        { InstrumentType.Guitar, 0.9f },
        { InstrumentType.Other, 0.85f }
    };

    private static SFXResource FinaliseResource(float[] monoWave, float volume, int lane, int maxLanes, float pitchVar = 0f)
    {
        // 1. Panning Logic
        // lane 0 -> Left, lane max -> Right
        // Center is 0.5
        float t = 0.5f;
        if (maxLanes > 1) t = (float)lane / (maxLanes - 1);
        
        // Clamp 0..1
        t = Mathf.Clamp(t, 0f, 1f);
        
        // Linear Panning Rule:
        // L = (1-t), R = t   (Simple)
        // Constant Power: L = cos(t * pi/2), R = sin(t * pi/2)
        float panL = Mathf.Cos(t * Mathf.Pi / 2.0f);
        float panR = Mathf.Sin(t * Mathf.Pi / 2.0f);
        
        int count = monoWave.Length;
        float[] left = new float[count];
        float[] right = new float[count];
        
        for(int i=0; i < count; i++)
        {
            left[i] = monoWave[i] * panL * volume;
            right[i] = monoWave[i] * panR * volume;
        }
        
        var res = new SFXResource();
        res.Clips = new AudioStream[] { FloatsToWav(left, right) };
        res.Volume = 1.0f; // Baked into samples, so keep this 1.0 usually, or use it for runtime scaling
        res.PitchVariance = pitchVar;
        return res;
    }
    #endregion
}