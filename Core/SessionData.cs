using System;
using System.Collections.Generic;

namespace RhythmBeatmapEditor.Core
{
    public static class SessionData
    {
        public static string CurrentSongPath { get; set; }
        public static string CurrentMapPath { get; set; }
        
        /// <summary>
        /// Display title for the current song
        /// </summary>
        public static string CurrentSongTitle { get; set; }
        
        /// <summary>
        /// Multiple map paths for compare mode (null if single map)
        /// </summary>
        public static List<string> CurrentMapPaths { get; set; }
        
        /// <summary>
        /// Check if we're loading multiple maps
        /// </summary>
        public static bool IsMultiMapMode => CurrentMapPaths != null && CurrentMapPaths.Count > 1;
        
        public static void Clear()
        {
            CurrentSongPath = null;
            CurrentMapPath = null;
            CurrentSongTitle = null;
            CurrentMapPaths = null;
        }
    }
}
