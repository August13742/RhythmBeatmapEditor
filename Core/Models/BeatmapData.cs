namespace RhythmBeatmapEditor.Core.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Metadata for the beatmap. Matches generator output schema.
/// </summary>
[Serializable]
public class BeatmapMetadata
{
    /// <summary>Difficulty level: EASY, NORMAL, HARD, ALT_HARD</summary>
    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = "NORMAL";
    
    /// <summary>Beats per minute</summary>
    [JsonPropertyName("bpm")]
    public float BPM { get; set; } = 120.0f;
    
    /// <summary>Focus mode: main or alt</summary>
    [JsonPropertyName("focus")]
    public string Focus { get; set; } = "main";
    
    /// <summary>Number of lanes (4 or 6 typically)</summary>
    [JsonPropertyName("lanes")]
    public int Lanes { get; set; } = 4;
    
    /// <summary>Generation profile</summary>
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "STANDARD";
    
    // Legacy fields for backward compatibility
    [JsonPropertyName("title")]
    public string Title { get; set; }
    
    [JsonPropertyName("artist")]
    public string Artist { get; set; }
    
    [JsonPropertyName("mapper")]
    public string Mapper { get; set; }
}

/// <summary>
/// Represents a full rhythm game chart.
/// </summary>
[Serializable]
public class BeatmapData
{
    [JsonPropertyName("metadata")]
    public BeatmapMetadata Metadata { get; set; } = new();
    
    [JsonPropertyName("notes")]
    public List<NoteEvent> Notes { get; set; } = new();
    
    /// <summary>
    /// BPM at root level for legacy compat. Prefer Metadata.BPM.
    /// </summary>
    [JsonPropertyName("bpm")]
    public float BPM { get; set; } = 120.0f;

    /// <summary>
    /// Gets effective BPM (prefers Metadata, falls back to root).
    /// </summary>
    [JsonIgnore]
    public float EffectiveBPM => Metadata?.BPM > 0 ? Metadata.BPM : BPM;
    
    /// <summary>
    /// Gets effective lane count from metadata.
    /// </summary>
    [JsonIgnore]
    public int LaneCount => Metadata?.Lanes > 0 ? Metadata.Lanes : 4;

    /// <summary>
    /// Sorts notes by time.
    /// </summary>
    public void Sort()
    {
        Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
    
    /// <summary>
    /// Returns a subset of notes visible within a specific time window.
    /// </summary>
    public IEnumerable<NoteEvent> GetNotesInWindow(float startTime, float endTime)
    {
        foreach (var note in Notes)
        {
            if (note.Time + note.Duration >= startTime && note.Time <= endTime)
            {
                yield return note;
            }
        }
    }
    
    /// <summary>
    /// Returns only visual notes (excludes ghost notes).
    /// </summary>
    public IEnumerable<NoteEvent> GetVisualNotes()
    {
        foreach (var note in Notes)
        {
            if (note.IsVisual)
                yield return note;
        }
    }
}
