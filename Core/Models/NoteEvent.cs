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
    public float Time { get; set; }

    /// <summary>
    /// Duration in seconds.
    /// </summary>
    [JsonPropertyName("dur")]
    public float Duration { get; set; }

    /// <summary>
    /// Lane index (0-3 typically, or more).
    /// </summary>
    public int Lane { get; set; }

    /// <summary>
    /// MIDI Pitch or Frequency.
    /// </summary>
    [JsonPropertyName("midi")]
    public float Pitch { get; set; }

    /// <summary>
    /// Source stem identifier (e.g., "vocals", "drums").
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Volume coefficient (0.0 to 1.0+).
    /// </summary>
    [JsonPropertyName("vol")]
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// Runtime selection state. Not recognized during serialization usually.
    /// </summary>
    [NonSerialized]
    public bool Selected = false;

    // Default Constructor
    public NoteEvent() { }

    public NoteEvent(float time, float duration, int lane, float pitch, string source)
    {
        Time = time;
        Duration = duration;
        Lane = lane;
        Pitch = pitch;
        Source = source;
    }

    /// <summary>
    /// Runtime edit state.
    /// </summary>
    public enum NoteState
    {
        Normal,
        Dirty,  // Edited but not confirmed (Paused)
        Edited, // Confirmed (Played), but differs from session start
        Deleted // Marked for deletion (Ghost)
    }

    [JsonIgnore]
    public NoteState State { get; set; } = NoteState.Normal;
}
