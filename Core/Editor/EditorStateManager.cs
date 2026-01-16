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
            
            // 1. Inspector Visibility
            if (Inspector != null)
            {
                // Force Hide in Play Mode.
                // In Edit Mode, it depends on selection (managed by Inspector logic)
                Inspector.SetEditMode(isEditMode);
                
                if (!isEditMode) 
                {
                    Inspector.Visible = false; 
                }
                else
                {
                    Inspector.Visible = true;
                } 
            }
            
            // 2. Timeline Input
            if (Timeline != null)
            {
                // Timeline might want to disable input during playback?
                // Currently handled by `if (!IsEditMode) return` checks in Input.
            }
            
            // 3. Song Panel
            if (SongPanel != null)
            {
                // Song Panel stays visible? User didn't request hiding it.
            }
        }
    }
}
