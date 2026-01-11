using Godot;
using RhythmBeatmapEditor.Core.Models;
using RhythmBeatmapEditor.Core.Editor;

namespace RhythmBeatmapEditor.Editor.Visuals;

public partial class NoteNode : Control
{
    private NoteEvent _data;
    private EditorContext _context;
    
    // Visual settings
    private const float LANE_HEIGHT = 64f;
    
    // Stem Colors (Start with Python specs)
    private static readonly System.Collections.Generic.Dictionary<string, Color> STEM_COLORS = new()
    {
        { "vocals", Colors.Magenta },
        { "vocals_lead", Colors.Magenta },
        { "drums", Colors.Cyan },
        { "bass", new Color(0.6f, 0.4f, 0.2f) }, // Brown/Gold
        { "piano", new Color(0.2f, 0.2f, 1.0f) }, // Deep Blue
        { "guitar", new Color(0.6f, 1.0f, 0.4f) }, // Light Green
        { "other", Colors.LightGray }
    };
    
    private Color _baseColor = Colors.LightGray;

    public void Setup(NoteEvent data, EditorContext context)
    {
        _data = data;
        _context = context;
        CustomMinimumSize = new Vector2(100, LANE_HEIGHT - 4); // Initial placeholder width
    }

    public override void _Ready()
    {
        if (STEM_COLORS.TryGetValue(_data.Source.ToLower(), out var c))
        {
            _baseColor = c;
        }
    
        // Simple visual rect
        var bg = new ColorRect();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.Color = _baseColor;
        bg.MouseFilter = MouseFilterEnum.Pass; // Allow events to bubble if needed
        AddChild(bg);
        
        // Label
        var lbl = new Label();
        lbl.Text = $"{_data.Pitch:F1}";
        lbl.Modulate = Colors.Black;
        AddChild(lbl);
        
        TooltipText = $"Time: {_data.Time:F2}s\nLane: {_data.Lane}\nSource: {_data.Source}";
    }

    public override void _Process(double delta)
    {
        if (_context == null || _data == null) return;
        
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        // Vertical Scrolling (Downscroll)
        // Future notes should be ABOVE the hit line (Lower Y value)
        // Hit Line is roughly at Y = ParentHeight - 100
        
        float hitLineY = GetViewportRect().Size.Y - 100f;
        
        // Time Diff: Positive if future, Negative if past
        float timeDiff = _data.Time - _context.PlaybackTime;
        
        // Y = HitLine - Distance
        float y = hitLineY - (timeDiff * _context.ScrollSpeed);
        
        // Lane Width (e.g. TotalWidth / 4)
        float totalWidth = 400f; // Fixed lane area width for now
        float laneWidth = totalWidth / 4f;
        float startX = (GetViewportRect().Size.X - totalWidth) / 2f; // Center it
        
        float x = startX + (_data.Lane * laneWidth);
        
        // Note Height based on Duration
        float height = _data.Duration * _context.ScrollSpeed;
        
        // Position
        // If it's a hold note, the "Head" is at Y. The body extends UPWARDS? 
        // Usually rhythm games draw holds "up" from the head if future, but Godot Rect grows Down by default.
        // Let's assume (x, y) is Top-Left.
        // If note is falling DOWN, the "Head" (StartTime) is the BOTTOM of the visual rect if we are drawing the tail?
        // No, simplest is: Note Head is at 'y'. Tail is at 'y - height' (above).
        // Let's just draw a simple Block for now: Head at Y.
        // Actually, for accurate hit timing, the "Bottom" of the block should hit the line? 
        // Or "Center"? Standard VSRG: Head hits line.
        // So rect should be at (x, y - height) if we want the "End" to be at Y?
        // No, StartTime is the critical one.
        // Visual: [Head, StartTime] --(Duration)--> [Tail, EndTime]
        // As time passes, Head moves DOWN.
        // Head hits line at Time.
        // So at Time, Head.Y == HitLineY.
        // Tail is "Later", so it should be ABOVE Head.
        // So Rect: Top = HeadY - Height. Bottom = HeadY.
        
        Position = new Vector2(x, y - height);
        Size = new Vector2(laneWidth - 4, Mathf.Max(10f, height));
        
        // Update Color based on hit status (optional)
        if (timeDiff <= 0 && timeDiff + _data.Duration >= 0)
        {
             // Active (Holding)
             Modulate = Colors.White;
        }
        else
        {
             Modulate = _baseColor;
        }

        // Visibility Culling (Vertical)
        Visible = y + Size.Y > -100 && y < GetViewportRect().Size.Y + 100;
    }
}
