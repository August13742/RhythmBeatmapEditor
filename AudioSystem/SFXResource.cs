using Godot;

namespace AudioSystem{
    [GlobalClass]
    public partial class SFXResource : Resource
    {
        [ExportGroup("Content")]
        [Export] public AudioStream[] Clips { get; set; }
        [Export] public bool Loop { get; set; } = false;

        [ExportGroup("Mixing & Priority")]
        [Export(PropertyHint.Range, "0,256")] public int Priority { get; set; } = 128; // Lower is higher priority
        [Export] public string BusName { get; set; } = "SFX"; // Default Bus

        [ExportGroup("Volume & Pitch")]
        [Export(PropertyHint.Range, "0,1")] public float Volume { get; set; } = 1f;
        [Export(PropertyHint.Range, "0,0.5")] public float VolumeVariance { get; set; } = 0.1f;
        [Export(PropertyHint.Range, "0.1,3")] public float Pitch { get; set; } = 1f;
        [Export(PropertyHint.Range, "0,0.5")] public float PitchVariance { get; set; } = 0.1f;

        [ExportGroup("Spatial Settings")]
        [Export] public float MinDistance { get; set; } = 1f;
        [Export] public float MaxDistance { get; set; } = 25f;
        [Export] public bool BypassSpatial { get; set; } = false; // Force 2D
        
        [Export] public bool UseSpatialCoalescing { get; set; } = true;
        [Export] public float MinSpatialSeparation { get; set; } = 5.0f;
    }
}