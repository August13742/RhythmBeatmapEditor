using Godot;
using System;
using System.Collections.Generic;
using AudioSystem;

public static class VocalSynthesiser
{
    private const int SAMPLE_RATE = 44100;

    #region Enums & Config
    public enum VowelType { A, I, U, E, O }
    
    public enum VocalCharacter 
    { 
        Power, Soft, Pure, Dark, Cute, Opera, Robot 
    }

    public enum InstrumentType
    {
        Kick, Snare, Bass, Square, Piano, Other
    }
    #endregion

    #region Data Structures & Presets
    public struct Formant 
    {
        public float Freq;
        public float BW;
        public float GainDb;
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

        // 1. Setup Base Vowels
        _baseVowels = new List<Formant>[5];
        _baseVowels[(int)VowelType.A] = new() { new(800, 80, 0), new(1200, 100, -6), new(2800, 120, -12) };
        _baseVowels[(int)VowelType.I] = new() { new(300, 50, -6), new(2300, 90, -12), new(3000, 120, -18) };
        _baseVowels[(int)VowelType.U] = new() { new(350, 60, -5), new(800, 80, -10), new(2500, 120, -18) };
        _baseVowels[(int)VowelType.E] = new() { new(500, 70, -4), new(1800, 90, -10), new(2600, 120, -16) };
        _baseVowels[(int)VowelType.O] = new() { new(500, 70, -2), new(900, 90, -8), new(2600, 120, -16) };

        // 2. Setup Profiles
        int profileCount = Enum.GetNames(typeof(VocalCharacter)).Length;
        _profiles = new VocalProfile[profileCount];

        _profiles[(int)VocalCharacter.Power] = new() { Name = "Power", FormantShift = 1.08f, Breathiness = 0.01f, Tension = 0.8f, MasterGain = 0.9f };
        _profiles[(int)VocalCharacter.Soft] = new() { Name = "Soft", FormantShift = 0.95f, Breathiness = 0.15f, Tension = 0.1f, MasterGain = 1.1f };
        _profiles[(int)VocalCharacter.Pure] = new() { Name = "Pure", FormantShift = 1.12f, Breathiness = 0.02f, Tension = 0.4f, MasterGain = 1.0f };
        _profiles[(int)VocalCharacter.Dark] = new() { Name = "Dark", FormantShift = 0.88f, Breathiness = 0.08f, Tension = 0.3f, MasterGain = 1.2f };
        _profiles[(int)VocalCharacter.Cute] = new() { Name = "Cute", FormantShift = 1.25f, Breathiness = 0.02f, Tension = 0.7f, MasterGain = 0.9f, VibratoAmount = 1.2f };
        _profiles[(int)VocalCharacter.Opera] = new() { Name = "Opera", FormantShift = 0.96f, Breathiness = 0.1f, Tension = 0.6f, MasterGain = 1.0f, VibratoAmount = 2.0f };
        _profiles[(int)VocalCharacter.Robot] = new() { Name = "Robot", FormantShift = 1.0f, Breathiness = 0.0f, Tension = 0.9f, MasterGain = 0.8f, VibratoAmount = 0.0f };

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

    /// <summary>
    /// Generates a specific vocal tone.
    /// <param name="pitchVariance">Randomness in pitch when played. Default 0 for precise singing.</param>
    /// </summary>
    public static SFXResource GenerateVocal(int midi, VowelType vowel, float duration = 0.25f, VocalCharacter character = VocalCharacter.Power, float pitchVariance = 0.0f)
    {
        InitialisePresets();
        duration = Mathf.Max(duration, 0.35f); 

        var profile = _profiles[(int)character];
        var targetFormants = _baseVowels[(int)vowel];
        
        float f0 = GetNoteFreq(midi);
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];
        
        // Formant setup
        int formantCount = targetFormants.Count;
        float[] f_fc = new float[formantCount];
        float[] f_bw = new float[formantCount];
        float[] f_gain = new float[formantCount];

        for(int i = 0; i < formantCount; i++)
        {
            f_fc[i] = targetFormants[i].Freq * profile.FormantShift;
            f_bw[i] = targetFormants[i].BW;
            f_gain[i] = Mathf.Pow(10, targetFormants[i].GainDb / 20.0f);
        }

        // Phase & Vibrato
        float[] phaseAccum = new float[totalSamples];
        float currentPhase = 0f;
        float vibratoRate = 5.5f;
        float vibratoDepth = 0.08f * profile.VibratoAmount;

        for(int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            float vib = Mathf.Sin(2 * Mathf.Pi * vibratoRate * t) * vibratoDepth;
            float freqMod = f0 * Mathf.Pow(2.0f, vib / 12.0f);
            currentPhase += 2 * Mathf.Pi * freqMod / SAMPLE_RATE;
            phaseAccum[i] = currentPhase;
        }

        // Harmonics
        int n = 1;
        float nyquist = SAMPLE_RATE / 2f;
        float slopeBase = -12.0f + (profile.Tension * 4.0f);

        while (true)
        {
            float freqN = f0 * n;
            if (freqN >= nyquist || freqN > 11000) break;

            float sourceAmp = Mathf.Pow(n, slopeBase / 6.0f);
            float filtAmp = 0.0f;
            
            for(int k=0; k < formantCount; k++)
            {
                filtAmp += KlattGain(freqN, f_fc[k], f_bw[k]) * f_gain[k];
            }

            float finalAmp = sourceAmp * filtAmp;
            if (finalAmp > 0.0001f)
            {
                for(int i=0; i < totalSamples; i++) wave[i] += finalAmp * Mathf.Sin(n * phaseAccum[i]);
            }
            n++;
        }

        // Breath
        if (profile.Breathiness > 0.001f)
        {
            var rng = new Random();
            for(int i=0; i<totalSamples; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.2f; 
                float breathMod = 0.5f + 0.5f * Mathf.Cos(phaseAccum[i]);
                wave[i] += noise * breathMod * profile.Breathiness;
            }
        }

        // Envelope
        int atk = (int)(0.01f * SAMPLE_RATE);
        int rel = (int)(0.05f * SAMPLE_RATE);
        
        for(int i=0; i<totalSamples; i++)
        {
            float env = 1.0f;
            if (i < atk) env = (float)i / atk;
            else if (i > totalSamples - rel) env = 1.0f - ((float)(i - (totalSamples - rel)) / rel);
            wave[i] *= env;
        }

        // Normalize
        float peak = 0f;
        for(int i=0; i<totalSamples; i++) { float abs = Math.Abs(wave[i]); if(abs > peak) peak = abs; }
        if (peak > 0.00001f) 
        {
            float normFactor = (1.0f / peak) * 0.25f * profile.MasterGain;
            for(int i=0; i<totalSamples; i++) wave[i] *= normFactor;
        }

        // Stereo Haas
        float[] right = new float[totalSamples];
        int delaySamples = (int)(0.005f * SAMPLE_RATE); 
        if (totalSamples > delaySamples)
        {
            Array.Copy(wave, 0, right, delaySamples, totalSamples - delaySamples);
            Array.Copy(wave, totalSamples - delaySamples, right, 0, delaySamples);
        }

        // --- NEW: Apply Settings ---
        var res = new SFXResource();
        res.Clips = new AudioStream[] { FloatsToWav(wave, right) };
        res.Volume = 1.0f;
        res.PitchVariance = pitchVariance; // Explicitly Set Randomness
        return res;
    }

    /// <summary>
    /// Generates drum sounds.
    /// <param name="pitchVariance">Default 0.05f for slight organic feel.</param>
    /// </summary>
    public static SFXResource GenerateDrums(InstrumentType type, float pitchVariance = 0.05f)
    {
        float duration = 0.1f;
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];
        var rng = new Random();

        bool isKick = type == InstrumentType.Kick;
        bool isSnare = type == InstrumentType.Snare;

        if (!isKick && !isSnare) return GenerateInstrument(type, 60, 0.0f); // Fallback

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            
            if (isKick)
            {
                float freqSweep = 150f * Mathf.Exp(-60f * t);
                wave[i] = Mathf.Sin(2 * Mathf.Pi * freqSweep * t) * Mathf.Exp(-10f * t);
            }
            else
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                wave[i] = noise * Mathf.Exp(-30f * t);
            }
            
            wave[i] *= 0.4f;
        }

        var res = new SFXResource();
        res.Clips = new AudioStream[] { FloatsToWav(wave, wave) };
        res.Volume = 1.0f;
        res.PitchVariance = pitchVariance; // Set Randomness
        return res;
    }

    /// <summary>
    /// Generates instrumental tones.
    /// <param name="pitchVariance">Default 0.0f for precise tuning.</param>
    /// </summary>
    public static SFXResource GenerateInstrument(InstrumentType type, int midi, float pitchVariance = 0.0f)
    {
        float freq = GetNoteFreq(midi);
        float duration = type == InstrumentType.Piano ? 0.3f : 0.2f;
        if (type == InstrumentType.Other) duration = 0.15f;
        
        int totalSamples = (int)(SAMPLE_RATE * duration);
        float[] wave = new float[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            float sample = 0f;

            switch(type)
            {
                case InstrumentType.Square:
                    float phaseSq = 2 * Mathf.Pi * freq * t;
                    float raw = ((phaseSq % (2 * Mathf.Pi)) < Mathf.Pi) ? 1.0f : -1.0f;
                    sample = raw * Mathf.Exp(-12f * t) * 0.3f;
                    break;

                case InstrumentType.Bass:
                    sample = 2 * Mathf.Abs(2 * ((t * freq) % 1) - 1) - 1;
                    sample *= Mathf.Exp(-10f * t) * 0.35f;
                    break;
                
                case InstrumentType.Piano:
                    float modIdx = 2.0f * Mathf.Exp(-15f * t);
                    sample = Mathf.Sin(2 * Mathf.Pi * freq * t + modIdx * Mathf.Sin(2 * Mathf.Pi * freq * 2 * t));
                    sample *= Mathf.Exp(-5f * t) * 0.3f;
                    break;

                case InstrumentType.Other:
                    sample = Mathf.Sign(Mathf.Sin(2 * Mathf.Pi * freq * 1.5f * t));
                    float decay = 3.0f / duration;
                    sample *= Mathf.Exp(-decay * t) * 0.25f;
                    break;
            }
            wave[i] = sample;
        }

        var res = new SFXResource();
        res.Clips = new AudioStream[] { FloatsToWav(wave, wave) };
        res.Volume = 1.0f;
        res.PitchVariance = pitchVariance; // Set Randomness
        return res;
    }

    #endregion
}