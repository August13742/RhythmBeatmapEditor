using Godot;
using System;

namespace RhythmBeatmapEditor.Utility
{
    public partial class CrossfadeManager : CanvasLayer
    {
        public static CrossfadeManager Instance { get; private set; }

        private ColorRect _fadeRect;

        public override void _Ready()
        {
            if (Instance != null)
            {
                QueueFree();
                return;
            }
            Instance = this;
            
            // Ensure high sorting order to be on top of everything
            Layer = 9999;
            
            // Create the simple black rect
            _fadeRect = new ColorRect();
            _fadeRect.Color = Colors.Black;
            _fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore; // Start ignoring input
            _fadeRect.Modulate = new Color(0, 0, 0, 0); // Start transparent
            AddChild(_fadeRect);
        }

        /// <summary>
        /// Orchestrates a smooth scene transition: Fade In -> Change Scene -> Fade Out
        /// </summary>
        public void LoadScene(string scenePath, float duration = 0.5f)
        {
            if (_fadeRect == null) return;
            
            // Block input during transition
            _fadeRect.MouseFilter = Control.MouseFilterEnum.Stop;
            
            var tween = CreateTween();
            
            // 1. Fade To Black
            tween.TweenProperty(_fadeRect, "modulate:a", 1.0f, duration)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
                
            // 2. Change Scene (Callback)
            tween.TweenCallback(Callable.From(() => 
            {
                GetTree().ChangeSceneToFile(scenePath);
            }));
            
            // 3. Fade From Black
            tween.TweenProperty(_fadeRect, "modulate:a", 0.0f, duration)
                 .SetTrans(Tween.TransitionType.Quad)
                 .SetEase(Tween.EaseType.In);
                 
            // 4. Cleanup
            tween.TweenCallback(Callable.From(() => 
            {
                _fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore;
            }));
        }

        /// <summary>
        /// Overload for PackedScene
        /// </summary>
        public void LoadScene(PackedScene scene, float duration = 0.5f)
        {
            if (_fadeRect == null) return;

            _fadeRect.MouseFilter = Control.MouseFilterEnum.Stop;
            var tween = CreateTween();
            
            tween.TweenProperty(_fadeRect, "modulate:a", 1.0f, duration)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
                
            tween.TweenCallback(Callable.From(() => 
            {
                GetTree().ChangeSceneToPacked(scene);
            }));
            
            tween.TweenProperty(_fadeRect, "modulate:a", 0.0f, duration)
                 .SetTrans(Tween.TransitionType.Quad)
                 .SetEase(Tween.EaseType.In);
                 
            tween.TweenCallback(Callable.From(() => 
            {
                _fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore;
            }));
        }

        /// <summary>
        /// Manual Fade To Black (e.g. for Quit)
        /// </summary>
        public Tween FadeToBlack(float duration = 1.0f)
        {
            _fadeRect.MouseFilter = Control.MouseFilterEnum.Stop;
            var t = CreateTween();
            t.TweenProperty(_fadeRect, "modulate:a", 1.0f, duration);
            return t;
        }

        /// <summary>
        /// Manual Fade From Black
        /// </summary>
        public Tween FadeFromBlack(float duration = 1.0f)
        {
            var t = CreateTween();
            t.TweenProperty(_fadeRect, "modulate:a", 0.0f, duration);
            t.TweenCallback(Callable.From(() => _fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore));
            return t;
        }
    }
}
