using Godot;
using System;

namespace RhythmBeatmapEditor.Editor.Visuals.Jukebox
{
    public partial class SongListItem : Button
    {
        public string SongName { get; private set; }
        public string FolderPath { get; private set; } // Path to Music folder or Beatmap folder
        
        [Signal] public delegate void SelectedEventHandler(SongListItem item);
        
        public void Initialise(string songName, string folderPath)
        {
            SongName = songName;
            FolderPath = folderPath;
            Text = songName;
            
            Pressed += () => EmitSignal(SignalName.Selected, this);
        }
    }
}
