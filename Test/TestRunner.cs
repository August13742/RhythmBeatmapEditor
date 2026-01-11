using Godot;
using System;
using RhythmBeatmapEditor.Core.Editor;
using RhythmBeatmapEditor.Core.Models;

namespace RhythmBeatmapEditor.Test;

public partial class TestRunner : Node
{
    public override void _Ready()
    {
        GD.Print("[TestRunner] Starting verification...");
        RunJsonParseTest();
    }

    private void RunJsonParseTest()
    {
        // Real path from user context
        string path = @"c:\Users\augus\Desktop\PythonHelperScripts\RhythmGameVisualiser\rhythm_engine\stems\betelgeuse\beatmap\HARD.json";
        
        if (!System.IO.File.Exists(path))
        {
            GD.PrintErr($"[TestRunner] Test file NOT FOUND at: {path}");
            return;
        }

        string json = System.IO.File.ReadAllText(path);
        
        // Manual deserialization test to ensure our models match the python output
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true,
                IncludeFields = true 
            };
            var data = System.Text.Json.JsonSerializer.Deserialize<BeatmapData>(json, options);
            
            if (data == null)
            {
                GD.PrintErr("[TestRunner] Failed to deserialize JSON (Result is null)");
            }
            else
            {
                GD.Print($"[TestRunner] SUCCESS! Loaded Beatmap.");
                GD.Print($"   - Note Count: {data.Notes.Count}");
                GD.Print($"   - BPM: {data.BPM}");
                
                if (data.Notes.Count > 0)
                {
                    var first = data.Notes[0];
                    GD.Print($"   - First Note: Time={first.Time}, Pitch={first.Pitch}, Source={first.Source}");
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TestRunner] EXCEPTION: {e.Message}");
        }
    }
}
