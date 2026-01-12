using Godot;

public partial class LaneAnchor : Control
{
    [Export] public int LaneIndex { get; set; }
    
    // Helper to get the center X position relative to a parent
    public float GetCenterX()
    {
        return Position.X + (Size.X / 2f);
    }
    
    public float GetWidth() => Size.X;
}