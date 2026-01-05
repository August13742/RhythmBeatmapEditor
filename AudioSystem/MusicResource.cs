using Godot;
namespace AudioSystem{
    [GlobalClass]
    public partial class MusicResource : Resource
    {
        [Export] public AudioStream Clip { get; set; }
        [Export(PropertyHint.Range, "0,1")] public float Volume { get; set; } = 1f;
        [Export] public bool Loop { get; set; } = true;
        [Export] public float FadeTime { get; set; } = 1.5f;
        [Export] public string BusName { get; set; } = "Music";
    }
}