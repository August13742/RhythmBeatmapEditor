using Godot;
using Godot.Collections;

namespace AudioSystem{
    public enum SFXSequenceMode { Sequential, RandomNoRepeat, Random }

    [GlobalClass]
    public partial class SFXSequence : Resource
    {
        [Export] public SFXSequenceMode Mode { get; set; } = SFXSequenceMode.RandomNoRepeat;
        [Export] public Array<SFXResource> Steps { get; set; } = new();
        [Export(PropertyHint.Range, "0,1")] public float SequenceVolume { get; set; } = 1f;
    }
}