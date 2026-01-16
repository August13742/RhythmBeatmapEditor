using Godot;
using System.Collections.Generic;

namespace RhythmBeatmapEditor.Editor.Visuals
{
    public partial class GhostLayer : Control
    {
        public struct GhostData
        {
            public Rect2 Rect;
            public Vector2 ValidTargetCenter; // The center of the 'current' note to draw line to
            public Color Color;
        }

        private List<GhostData> _queue = new();

        public void Clear()
        {
            _queue.Clear();
            QueueRedraw();
        }

        public void AddGhost(Rect2 rect, Vector2 targetCenter, Color color)
        {
            _queue.Add(new GhostData 
            { 
                Rect = rect, 
                ValidTargetCenter = targetCenter, 
                Color = color 
            });
        }
        
        public void Commit()
        {
            QueueRedraw();
        }

        public override void _Draw()
        {
            foreach (var ghost in _queue)
            {
                // Draw Ghost Rect
                DrawRect(ghost.Rect, ghost.Color);
                
                // Draw Connection Line
                var ghostCenter = ghost.Rect.GetCenter();
                var lineColor = new Color(1, 1, 1, 0.4f);
                
                DrawLine(ghostCenter, ghost.ValidTargetCenter, lineColor, 1.5f, true);
                DrawCircle(ghostCenter, 2.0f, lineColor);
            }
        }
        
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }
    }
}
