using Godot;
using Godot.Collections;

namespace AudioSystem{
    public enum PlaybackMode { Sequential, Shuffle }

    [GlobalClass]
    public partial class MusicPlaylist : Resource
    {
        [Export] public Array<MusicResource> Tracks { get; set; } = new();
        [Export] public PlaybackMode Mode { get; set; } = PlaybackMode.Shuffle;
    }
}