using Godot;
using System;
using AudioSystem;

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
            
            // Update lambda to accept both arguments
            _songList.SongSelected += (name, path) => 
            {
                _songInspector.Inspect(name,path);
                PlaySongPreview(path);
            };
            
            _btnBack.Pressed += OnBackInternal;
        }
        
        private void PlaySongPreview(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return;

            if (!ResourceLoader.Exists(resourcePath))
            {
                GD.PrintErr($"[Jukebox] Song not found: {resourcePath}");
                return;
            }

            var stream = ResourceLoader.Load<AudioStream>(resourcePath);
            var musicRes = new MusicResource
            {
                Clip = stream,
                Volume = 1.0f,
                FadeTime = 1.0f,
                Loop = true
            };
        
            AudioManager.Instance?.PlayMusic(musicRes);
        }

        private void OnBackInternal()
        {
            AudioManager.Instance?.StopMusic();
            Utility.CrossfadeManager.Instance.LoadScene("res://UI/MainMenu.tscn");
        }
    }
}