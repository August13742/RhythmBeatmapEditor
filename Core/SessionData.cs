using System;

namespace RhythmBeatmapEditor.Core
{
    public static class SessionData
    {
        public static string CurrentSongPath { get; set; }
        public static string CurrentMapPath { get; set; }
        
        public static void Clear()
        {
            CurrentSongPath = null;
            CurrentMapPath = null;
        }
    }
}
