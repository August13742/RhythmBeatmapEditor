using Godot;
using System;

namespace RhythmBeatmapEditor.Editor.Visuals.Jukebox
{
    public partial class Jukebox : Control
    {
        private SongList _songList;
        private SongInspector _songInspector;
        private Button _btnBack;
        
        public override void _Ready()
        {
            _songList = GetNode<SongList>("%SongList");
            _songInspector = GetNode<SongInspector>("%SongInspector");
            _btnBack = GetNode<Button>("%BtnBack");
            
            _songList.SongSelected += (name) => _songInspector.Inspect(name);
            _btnBack.Pressed += OnBackInternal; // avoid name collision with OnBack
        }
        
        private void OnBackInternal()
        {
            GetTree().ChangeSceneToFile("res://UI/MainMenu.tscn");
        }
    }
}
