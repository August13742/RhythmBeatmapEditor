using Godot;
using RhythmBeatmapEditor.Editor.Visuals;

namespace RhythmBeatmapEditor.Core.Editor
{
    public partial class EditorStateManager : Node
    {
        [Export] public EditorContext Context { get; set; }
        
        [ExportGroup("UI Components")]
        [Export] public NoteInspector Inspector { get; set; }
        [Export] public SongControlPanel SongPanel { get; set; }
        [Export] public TimelineController Timeline { get; set; }
        
        public void Initialise(EditorContext context)
        {
            Context = context;
            if (Context != null)
            {
                Context.ModeChanged += OnModeChanged;
                // Initialise state matching context (default is Edit Mode usually, unless Audio starts playing)
                OnModeChanged(!Context.IsPlaying);
            }
        }
        
        public override void _ExitTree()
        {
            if (Context != null)
                Context.ModeChanged -= OnModeChanged;
        }

        private void OnModeChanged(bool isEditMode)
        {
            GD.Print($"[StateManager] Mode Changed: {(isEditMode ? "EDIT" : "PLAY")}");
            
            // 1. Handle Inspector Visibility (Causes UI Shift)
            if (Inspector != null)
            {
                Inspector.SetEditMode(isEditMode);
                Inspector.Visible = isEditMode;
            }
            
        }
    }
}
