using Godot;
using System;

namespace RhythmBeatmapEditor.UI
{
    public partial class MainMenu : Control
    {
         private Button _btnPlay;
         private Button _btnQuit;
         
         public override void _Ready()
         {
             _btnPlay = GetNode<Button>("%BtnPlay");
             _btnQuit = GetNode<Button>("%BtnQuit");
             
             _btnPlay.Pressed += OnPlayPressed;
             _btnQuit.Pressed += OnQuitPressed;
         }
         
         private void OnPlayPressed()
         {
             // GetTree().ChangeSceneToFile("uid://jukebox");
             Utility.CrossfadeManager.Instance.LoadScene("uid://jukebox");
         }
         
         private void OnQuitPressed()
         {
             GetTree().Quit();
         }
    }
}
