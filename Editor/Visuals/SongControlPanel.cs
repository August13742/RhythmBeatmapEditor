using Godot;
using System;
using RhythmBeatmapEditor.Core.Editor;
using RhythmBeatmapEditor.Core.Models;

namespace RhythmBeatmapEditor.Editor.Visuals
{
    public partial class SongControlPanel : PanelContainer
    {
        private EditorContext _context;
        
        // --- Assets (Inspector) ---
        [ExportGroup("Icons")]
        [Export] public Texture2D IconPlay { get; set; }
        [Export] public Texture2D IconPause { get; set; }
        [Export] public Texture2D IconRewind { get; set; }
        [Export] public Texture2D IconForward { get; set; }
        [Export] public Texture2D IconBack { get; set; }
        
        [ExportGroup("Styling")]
        [Export] public Texture2D SliderGrabber { get; set; }
        
        // Controls (Bind via Unique Names)
        private Button _btnPlay;
        private Button _btnRewind;
        private Button _btnRewind1; 
        private Button _btnForward;
        private Button _btnForward1; 
        private Button _btnBack;
        private HSlider _sliderProgress;
        private Label _lblTime;
        private Label _lblMode;
        private Button _btnRevertAll; 
        
        // Dialogs
        private ConfirmationDialog _confirmRevert; 

        // State
        private bool _isDraggingSlider = false;

        public override void _Ready()
        {
            // Bind Nodes
            _btnBack = GetNode<Button>("%BtnBack");
            _btnRewind = GetNode<Button>("%BtnRewind");
            _btnRewind1 = GetNode<Button>("%BtnRewind1");
            _btnPlay = GetNode<Button>("%BtnPlay");
            _btnForward = GetNode<Button>("%BtnForward");
            _btnForward1 = GetNode<Button>("%BtnForward1");
            _lblTime = GetNode<Label>("%LblTime");
            _lblMode = GetNode<Label>("%LblMode");
            _btnRevertAll = GetNode<Button>("%BtnRevertAll");
            _sliderProgress = GetNode<HSlider>("%SliderProgress");
            
            _confirmRevert = GetNode<ConfirmationDialog>("%ConfirmRevert");
            
            SetupLogic();
            ApplyStyling();
        }

        public void Initialise(EditorContext context)
        {
            _context = context;
            
            // Connect to Signals/Events
            _context.Connect(EditorContext.SignalName.PlaybackTimeUpdated, Callable.From<float>(OnTimeUpdated));
        }
        
        public override void _ExitTree()
        {
            if (_context != null)
            {
                _context.Disconnect(EditorContext.SignalName.PlaybackTimeUpdated, Callable.From<float>(OnTimeUpdated));
            }
        }
    
        public override void _Process(double delta)
        {
            if (_context == null) return;
            
            // Update Play/Pause Visuals
            bool isPlaying = _context.IsPlaying;
            if (isPlaying)
            {
                if (IconPause != null) _btnPlay.Icon = IconPause;
                else _btnPlay.Text = "||";
                
                if (IconPause != null) _btnPlay.Text = "";
            }
            else
            {
                if (IconPlay != null) _btnPlay.Icon = IconPlay;
                else _btnPlay.Text = ">";
                
                if (IconPlay != null) _btnPlay.Text = "";
            }
            
            // Update Mode Bubble
            if (_lblMode != null)
            {
                _lblMode.Text = _context.IsEditMode ? "EDITING" : "PLAYING";
                _lblMode.Modulate = _context.IsEditMode ? Colors.Orange : Colors.Green;
            }
        }

        private void SetupLogic()
        {
            // Connect Button Signals
            _btnBack.Pressed += () => GD.Print("Back to Song Selector - Not Implemented");
            
            // Seek Buttons
            _btnRewind.Pressed += () => SeekRel(-5);
            _btnRewind1.Pressed += () => SeekRel(-1);
            _btnPlay.Pressed += TogglePlay;
            _btnForward1.Pressed += () => SeekRel(1);
            _btnForward.Pressed += () => SeekRel(5);
            
            // Revert Logic
            _btnRevertAll.Pressed += () => _confirmRevert.PopupCentered();
            _confirmRevert.Confirmed += () => _context?.CancelEdit();
            
            // Slider Logic
            _sliderProgress.DragStarted += () => _isDraggingSlider = true;
            _sliderProgress.DragEnded += (bool val) => 
            {
                _isDraggingSlider = false;
                SeekTo(_sliderProgress.Value);
            };
            
            // Focus Management - Disable focus to prevent spacebar hijacking
            _btnBack.FocusMode = FocusModeEnum.None;
            _btnRewind.FocusMode = FocusModeEnum.None;
            _btnRewind1.FocusMode = FocusModeEnum.None;
            _btnPlay.FocusMode = FocusModeEnum.None;
            _btnForward1.FocusMode = FocusModeEnum.None;
            _btnForward.FocusMode = FocusModeEnum.None;
            _sliderProgress.FocusMode = FocusModeEnum.None;
            _btnRevertAll.FocusMode = FocusModeEnum.None;
            

        }
        
        private void ApplyStyling()
        {
            if (IconBack != null) { _btnBack.Icon = IconBack; _btnBack.Text = ""; }
            if (IconRewind != null) { _btnRewind.Icon = IconRewind; _btnRewind.Text = ""; }
            if (IconForward != null) { _btnForward.Icon = IconForward; _btnForward.Text = ""; }
            
            if (SliderGrabber != null)
            {
                _sliderProgress.AddThemeIconOverride("grabber", SliderGrabber);
                _sliderProgress.AddThemeIconOverride("grabber_highlight", SliderGrabber);
                _sliderProgress.AddThemeIconOverride("grabber_disabled", SliderGrabber);
            }
        } 

        // --- Logic ---
        
        private void TogglePlay() => _context?.TogglePlay();
        
        private void SeekRel(float delta)
        {
            if (_context == null) return;
            float target = _context.PlaybackTime + delta;
            if (target < 0) target = 0;
            _context.Seek(target);
        }
        
        private void SeekTo(double value)
        {
             if (_context == null) return;
             _context.Seek((float)value);
        }

        private void OnTimeUpdated(float time)
        {
            // Update Label
            if (_lblTime != null)
            {
                var ts = TimeSpan.FromSeconds(time);
                _lblTime.Text = $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
            }
            
            // Update Slider only if NOT dragging
            if (_sliderProgress != null && !_isDraggingSlider) 
            {
                _sliderProgress.SetValueNoSignal(time);
                
                // Smart MaxValue update
                if (time > _sliderProgress.MaxValue) _sliderProgress.MaxValue = time + 10;
            }
        }
    }
}
