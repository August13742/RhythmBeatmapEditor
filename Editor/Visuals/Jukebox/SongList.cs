using Godot;
using System;
using System.IO;
using System.Collections.Generic;

namespace RhythmBeatmapEditor.Editor.Visuals.Jukebox
{
    public partial class SongList : ScrollContainer
    {
        [Export] public PackedScene SongItemScene { get; set; }
        
        // Updated signal to pass the full resource path to the player
        [Signal] public delegate void SongSelectedEventHandler(string songName, string resourcePath);
        
        private VBoxContainer _container;
        
        // centralized format definition
        private readonly string[] _supportedFormats = { ".mp3", ".ogg" };
        
        public override void _Ready()
        {
            _container = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            AddChild(_container);
            
            if (SongItemScene == null)
            {
                 SongItemScene = GD.Load<PackedScene>("uid://songlistitem");
            }
            
            Refresh();
        }
        
        public void Refresh()
        {
            // Clear current items
            foreach(var child in _container.GetChildren()) child.QueueFree();
            
            string musicResPath = "res://Music";
            string musicGlobalPath = ProjectSettings.GlobalizePath(musicResPath);

            if (!Directory.Exists(musicGlobalPath))
            {
                GD.PrintErr($"[SongList] Music directory not found: {musicGlobalPath}");
                return;
            }
            
            // Iterate through all supported formats
            foreach (string format in _supportedFormats)
            {
                // Note: Directory.GetFiles is case-sensitive on Linux/Android
                // "*" + format results in "*.mp3", "*.ogg", etc.
                string[] files = Directory.GetFiles(musicGlobalPath, "*" + format);

                foreach(var file in files)
                {
                    string fileName = Path.GetFileName(file); 
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                    
                    // Reconstruct the Godot resource path (safer for the loader than OS paths)
                    string resourcePath = $"{musicResPath}/{fileName}";

                    var item = SongItemScene.Instantiate<SongListItem>();
                    
                    // Pass the resource path into the item so it can return it later
                    item.Initialise(nameWithoutExt, resourcePath); 
                    
                    item.Selected += (clickedItem) => OnItemSelected(clickedItem, resourcePath);
                    _container.AddChild(item);
                }
            }
        }
        
        private void OnItemSelected(SongListItem item, string resourcePath)
        {
            EmitSignal(SignalName.SongSelected, item.SongName, resourcePath);
        }
    }
}