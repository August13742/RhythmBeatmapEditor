namespace RhythmBeatmapEditor.Core.Models;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// Represents a single musical note event. 
/// Pure POCO for performance and serialization ease.
/// </summary>
[Serializable]
public class NoteEvent
{
    /// <summary>
    /// Start time in seconds.
    /// </summary>
    [JsonPropertyName("time")]
    public float Time { get; set; }

    /// <summary>
    /// Duration in seconds.
    /// </summary>
    [JsonPropertyName("dur")]
    public float Duration { get; set; }

    /// <summary>
    /// Lane index (0-N, or -1 for ghost notes).
    /// </summary>
    [JsonPropertyName("lane")]
    public int Lane { get; set; }

    /// <summary>
    /// MIDI Pitch (visual/primary pitch).
    /// </summary>
    [JsonPropertyName("midi")]
    public float Pitch { get; set; }

    /// <summary>
    /// Source stem identifier (e.g., "vocals", "drums").
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>
    /// Volume coefficient (0.0 to 1.0+).
    /// </summary>
    [JsonPropertyName("vol")]
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// Audio pool: array of MIDI pitches for polyphonic playback.
    /// When visual density is reduced, coalesced notes are preserved here.
    /// </summary>
    [JsonPropertyName("audio_pool")]
    public int[] AudioPool { get; set; }

    /// <summary>
    /// Runtime selection state. Not serialized.
    /// </summary>
    [NonSerialized]
    public bool Selected = false;

    /// <summary>
    /// Note Type Enum.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NoteType
    {
        [JsonPropertyName("tap")]
        Tap,
        [JsonPropertyName("hold")]
        Hold,
        [JsonPropertyName("ghost")]
        Ghost
    }

    /// <summary>
    /// Note Type: Tap, Hold, or Ghost (audio-only).
    /// </summary>
    [JsonPropertyName("type")]
    public NoteType Type { get; set; } = NoteType.Tap;

    // Default Constructor
    public NoteEvent() { }

    public NoteEvent(float time, float duration, int lane, float pitch, string source, NoteType type = NoteType.Tap)
    {
        Time = time;
        Duration = duration;
        Lane = lane;
        Pitch = pitch;
        Source = source;
        Type = type;
    }

    /// <summary>
    /// Returns MIDI pitches for audio playback.
    /// Uses AudioPool if available, otherwise falls back to single Pitch.
    /// </summary>
    public int[] GetAudioPitches()
    {
        if (AudioPool != null && AudioPool.Length > 0)
            return AudioPool;
        return new[] { (int)Pitch };
    }

    /// <summary>
    /// Returns true if this note is visual (should be rendered).
    /// Ghost notes (lane=-1 or Type=Ghost) are audio-only.
    /// </summary>
    [JsonIgnore]
    public bool IsVisual => Lane >= 0 && Type != NoteType.Ghost;

    /// <summary>
    /// Runtime edit state.
    /// </summary>
    public enum NoteState
    {
        Normal,
        Dirty,  // Edited but not confirmed (Paused)
        Edited, // Confirmed (Played), but differs from session start
        Deleted // Marked for deletion
    }

    [JsonIgnore]
    public NoteState State { get; set; } = NoteState.Normal;
}
