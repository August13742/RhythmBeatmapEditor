using Godot;
using System;
using System.IO;
using System.Collections.Generic;

namespace RhythmBeatmapEditor.Editor.Visuals.Jukebox
{
    public partial class SongList : ScrollContainer
    {
        [Export] public PackedScene SongItemScene { get; set; }
        
        [Signal] public delegate void SongSelectedEventHandler(string songName);
        
        private VBoxContainer _container;
        
        public override void _Ready()
        {
            _container = new VBoxContainer();
            _container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _container.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            AddChild(_container);
            
            // Default scene if not assigned
            if (SongItemScene == null)
            {
                 SongItemScene = GD.Load<PackedScene>("uid://songlistitem");
            }
            
            Refresh();
        }
        
        public void Refresh()
        {
            // Clear current
            foreach(var child in _container.GetChildren()) child.QueueFree();
            
            // Scan Music folder
            string musicPath = ProjectSettings.GlobalizePath("res://Music");
            if (!Directory.Exists(musicPath))
            {
                GD.PrintErr($"[SongList] Music directory not found: {musicPath}");
                return;
            }
            
            var files = Directory.GetFiles(musicPath, "*.mp3");
            foreach(var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var item = SongItemScene.Instantiate<SongListItem>();
                item.Initialise(name, file); // passing full path just in case
                item.Selected += OnItemSelected;
                _container.AddChild(item);
            }
        }
        
        private void OnItemSelected(SongListItem item)
        {
            EmitSignal(SignalName.SongSelected, item.SongName);
        }
    }
}
