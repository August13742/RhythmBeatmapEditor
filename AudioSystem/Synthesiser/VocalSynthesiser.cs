using Godot;
using System;
using System.Collections.Generic;
using AudioSystem;

public static class VocalSynthesiser
{
    private const int SAMPLE_RATE = 44100;

    #region Data Structures
    public class Formant
    {
        public float Freq;
        public float BW;
        public float GainDb;
        public Formant(float freq, float bw, float gainDb) { Freq = freq; BW = bw; GainDb = gainDb; }
    }

    public class VocalProfile
    {
        public string Name;
        public Dictionary<string, List<Formant>> Vowels;
        public float FormantShift = 1.0f;
        public float Breathiness = 0.05f;
        public float Tension = 0.5f;
        public float MasterGain = 1.0f;
    }
    #endregion

    #region Presets
    private static Dictionary<string, VocalProfile> _presets;
    
    public static void InitialisePresets()
    {
        if (_presets != null) return;

        var BASE = new Dictionary<string, List<Formant>> {
            { "A", new List<Formant> { new(800, 80, 0), new(1200, 100, -6), new(2800, 120, -12) } },
            { "I", new List<Formant> { new(300, 50, -6), new(2300, 90, -12), new(3000, 120, -18) } },
            { "U", new List<Formant> { new(350, 60, -5), new(800, 80, -10), new(2500, 120, -18) } },
            { "E", new List<Formant> { new(500, 70, -4), new(1800, 90, -10), new(2600, 120, -16) } },
            { "O", new List<Formant> { new(500, 70, -2), new(900, 90, -8), new(2600, 120, -16) } }
        };

        _presets = new Dictionary<string, VocalProfile>
        {
            { "POWER_RIN", new VocalProfile { 
                Name = "Power", Vowels = BASE, FormantShift = 1.08f, Breathiness = 0.01f, Tension = 0.8f, MasterGain = 0.9f 
            }},
            { "SOFT_LUKA", new VocalProfile { 
                Name = "Soft", Vowels = BASE, FormantShift = 0.95f, Breathiness = 0.15f, Tension = 0.1f, MasterGain = 1.1f 
            }},
             { "PURE_MIKU", new VocalProfile { 
                Name = "Pure", Vowels = BASE, FormantShift = 1.12f, Breathiness = 0.02f, Tension = 0.4f, MasterGain = 1.0f 
            }}
        };
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
        if (denom == 0) return 0.0f;
        return 1.0f / Mathf.Sqrt(denom);
    }

    private static float GetNoteFreq(int midi)
    {
        return 440.0f * Mathf.Pow(2.0f, (midi - 69) / 12.0f);
    }

    private static AudioStreamWav FloatsToWav(float[] samplesLeft, float[] samplesRight)
    {
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SAMPLE_RATE,
            Stereo = true
        };

        var bytes = new List<byte>(samplesLeft.Length * 4); // 2 bytes per sample * 2 channels

        for (int i = 0; i < samplesLeft.Length; i++)
        {
            // Clamp and convert L
            short sL = (short)(Mathf.Clamp(samplesLeft[i], -1f, 1f) * 32767);
            bytes.Add((byte)(sL & 0xFF));
            bytes.Add((byte)((sL >> 8) & 0xFF));

            // Clamp and convert R
            short sR = (short)(Mathf.Clamp(samplesRight[i], -1f, 1f) * 32767);
            bytes.Add((byte)(sR & 0xFF));
            bytes.Add((byte)((sR >> 8) & 0xFF));
        }

        wav.Data = bytes.ToArray();
        return wav;
    }
    #endregion

    #region Generators

    /// <summary>
    /// Generates a Vocal Tone and wraps it in an SFXResource ready for the AudioManager.
    /// </summary>
    public static SFXResource GenerateVocal(int midi, string vowel = "A", float duration = 0.25f, string profileName = "POWER_RIN")
    {
        InitialisePresets();
        float minDuration = 0.3f;
        duration = Mathf.Max(duration, minDuration); // Enforce min duration 

        if (!_presets.ContainsKey(profileName)) profileName = "POWER_RIN";
        var profile = _presets[profileName];
        
        float f0 = GetNoteFreq(midi);
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];
        
        // Target Formants
        var targetVowels = profile.Vowels.ContainsKey(vowel) ? profile.Vowels[vowel] : profile.Vowels["A"];
        
        // Pre-calc shifted formants
        var activeFormants = new List<(float fc, float bw, float gain)>();
        foreach(var fmt in targetVowels)
        {
            float dbScale = Mathf.Pow(10, fmt.GainDb / 20.0f);
            activeFormants.Add((fmt.Freq * profile.FormantShift, fmt.BW, dbScale));
        }

        // Additive Synthesis
        int n = 1;
        float nyquist = SAMPLE_RATE / 2f;
        
        // Phase tracking
        // We use a simplified phase approach here compared to numpy's cumsum for performance
        // But to keep pitch bending (vibrato), we calculate phase per sample.
        
        float[] t = new float[totalSamples];
        float[] phaseAccum = new float[totalSamples];
        float currentPhase = 0f;

        // Precompute Time and Phase (with Vibrato)
        for(int i = 0; i < totalSamples; i++)
        {
            t[i] = (float)i / SAMPLE_RATE;
            float vib = Mathf.Sin(2 * Mathf.Pi * 5.5f * t[i]) * 0.08f;
            float freqMod = f0 * Mathf.Pow(2.0f, vib / 12.0f);
            currentPhase += 2 * Mathf.Pi * freqMod / SAMPLE_RATE;
            phaseAccum[i] = currentPhase;
        }

        // Sum Harmonics
        while (true)
        {
            float freqN = f0 * n;
            if (freqN >= nyquist || freqN > 10000) break;

            float slope = -12.0f + (profile.Tension * 4.0f);
            float sourceAmp = Mathf.Pow(n, slope / 6.0f);
            
            float filtAmp = 0.0f;
            for(int k=0; k<activeFormants.Count; k++)
            {
                filtAmp += KlattGain(freqN, activeFormants[k].fc, activeFormants[k].bw) * activeFormants[k].gain;
            }

            float finalAmp = sourceAmp * filtAmp;
            if (finalAmp > 0.0001f)
            {
                for(int i=0; i < totalSamples; i++)
                {
                    wave[i] += finalAmp * Mathf.Sin(n * phaseAccum[i]);
                }
            }
            n++;
        }

        // Noise (Breath)
        if (profile.Breathiness > 0)
        {
            var rng = new Random();
            for(int i=0; i<totalSamples; i++)
            {
                // Gaussian approx
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.2f; 
                float breathMod = 0.5f + 0.5f * Mathf.Cos(phaseAccum[i]);
                wave[i] += noise * breathMod * profile.Breathiness;
            }
        }

        // Envelope (ADSR)
        int atk = (int)(0.01f * SAMPLE_RATE);
        int rel = (int)(0.05f * SAMPLE_RATE);
        
        for(int i=0; i<totalSamples; i++)
        {
            float env = 1.0f;
            // Attack
            if (i < atk) env = (float)i / atk;
            // Release
            else if (i > totalSamples - rel) 
                env = 1.0f - ((float)(i - (totalSamples - rel)) / rel);
            
            wave[i] *= env;
        }

        // Normalize
        float peak = 0f;
        foreach(var s in wave) peak = Mathf.Max(peak, Mathf.Abs(s));
        if (peak > 0) 
        {
            float normFactor = (1.0f / peak) * 0.25f * profile.MasterGain;
            for(int i=0; i<totalSamples; i++) wave[i] *= normFactor;
        }

        // Stereo Delay (Haas effect simulation from Python code)
        float[] left = wave;
        float[] right = new float[totalSamples];
        int delaySamples = (int)(0.005f * SAMPLE_RATE); // 5ms delay

        for (int i = 0; i < totalSamples; i++)
        {
            int srcIdx = i - delaySamples;
            if (srcIdx < 0) srcIdx += totalSamples; // Circular wrap or 0?
            right[i] = left[srcIdx];
        }

        var res = new SFXResource();
        res.Clips = new AudioStream[] { FloatsToWav(left, right) };
        res.Volume = 1.0f;
        return res;
    }

    public static SFXResource GenerateDrums(int type)
    {
        float duration = 0.1f;
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];
        var rng = new Random();

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            
            if (type % 2 == 0) // Kick
            {
                float freqSweep = 150f * Mathf.Exp(-60f * t);
                wave[i] = Mathf.Sin(2 * Mathf.Pi * freqSweep * t);
                wave[i] *= Mathf.Exp(-10f * t);
            }
            else // Snare
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                wave[i] = noise * Mathf.Exp(-30f * t);
            }
            
            wave[i] *= 0.4f; // Tuning
        }

        var res = new SFXResource();
        res.Clips = new AudioStream[] { FloatsToWav(wave, wave) }; // Mono to Stereo
        return res;
    }

    public static SFXResource GenerateInstrument(string type, int midi)
    {
        float freq = GetNoteFreq(midi);
        float duration = type == "PIANO" ? 0.3f : 0.2f;
        if (type == "OTHER") duration = 0.15f;
        
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            float sample = 0f;

            switch(type)
            {
                case "SQUARE":
                    float phaseSq = 2 * Mathf.Pi * freq * t;
                    float raw = ((phaseSq % (2 * Mathf.Pi)) < Mathf.Pi) ? 1.0f : -1.0f;
                    sample = raw * Mathf.Exp(-12f * t) * 0.3f;
                    break;

                case "BASS":
                    // Triangle-ish
                    sample = 2 * Mathf.Abs(2 * ((t * freq) % 1) - 1) - 1;
                    sample *= Mathf.Exp(-10f * t) * 0.35f;
                    break;
                
                case "PIANO":
                    float modIdx = 2.0f * Mathf.Exp(-15f * t);
                    sample = Mathf.Sin(2 * Mathf.Pi * freq * t + modIdx * Mathf.Sin(2 * Mathf.Pi * freq * 2 * t));
                    sample *= Mathf.Exp(-5f * t) * 0.3f;
                    break;

                case "OTHER":
                    sample = Mathf.Sign(Mathf.Sin(2 * Mathf.Pi * freq * 1.5f * t));
                    float decay = 3.0f / duration;
                    sample *= Mathf.Exp(-decay * t) * 0.25f;
                    break;
            }
            wave[i] = sample;
        }

        var res = new SFXResource();
        res.Clips = new AudioStream[] { FloatsToWav(wave, wave) };
        return res;
    }

    #endregion
}