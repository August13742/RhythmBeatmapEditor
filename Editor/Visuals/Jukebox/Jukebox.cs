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
            
            _songList.SongSelected += (name) => 
            {
                _songInspector.Inspect(name);
                PlaySongPreview(name);
            };
            _btnBack.Pressed += OnBackInternal; // avoid name collision with OnBack
        }
        
        private void PlaySongPreview(string songName)
        {
            if (string.IsNullOrEmpty(songName)) return;

            string path = $"res://Music/{songName}.mp3";
            if (!ResourceLoader.Exists(path))
            {
                GD.PrintErr($"[Jukebox] Song not found: {path}");
                return;
            }

            var stream = ResourceLoader.Load<AudioStream>(path);
            var musicRes = new MusicResource
            {
                Clip = stream,
                Volume = 1.0f,
                FadeTime = 1.0f, // smooth crossfade
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
