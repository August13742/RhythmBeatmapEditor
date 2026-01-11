namespace RhythmBeatmapEditor.Core.Models;

using System;
using System.Collections.Generic;

/// <summary>
/// Metadata for the beatmap.
/// </summary>
[Serializable]
public class BeatmapMetadata
{
    public string Title { get; set; } = "Unknown";
    public string Artist { get; set; } = "Unknown";
    public string Mapper { get; set; } = "Unknown";
    public string DifficultyName { get; set; } = "Normal";
}

/// <summary>
/// Represents a full rhythm game chart.
/// </summary>
[Serializable]
public class BeatmapData
{
    public BeatmapMetadata Metadata { get; set; } = new();
    public List<NoteEvent> Notes { get; set; } = new();
    public float BPM { get; set; } = 120.0f;

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
        // Simple linear scan for now. Optimization: Binary search on sorted list.
        foreach (var note in Notes)
        {
            if (note.Time + note.Duration >= startTime && note.Time <= endTime)
            {
                yield return note;
            }
        }
    }
}
