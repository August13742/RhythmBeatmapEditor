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
        
        // Inspector
        private VBoxContainer _inspectorContainer;
        
        // Time Controls
        private Button _btnTimeMinusBig, _btnTimeMinusSmall;
        private Button _btnTimePlusBig, _btnTimePlusSmall;
        private Label _lblInspectorTime;
        
        // Pitch Controls
        private Button _btnPitchMinusOct, _btnPitchMinus;
        private Button _btnPitchPlusOct, _btnPitchPlus;
        private Label _lblInspectorPitch;
        
        private Button _btnPlaySFX; 
        private Button _btnRevertSelected; 
        private Button _btnApply;
        
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
            
            _inspectorContainer = GetNode<VBoxContainer>("%Inspector");
            
            // Time
            _btnTimeMinusBig = GetNode<Button>("%BtnTimeMinusBig");
            _btnTimeMinusSmall = GetNode<Button>("%BtnTimeMinusSmall");
            _btnTimePlusSmall = GetNode<Button>("%BtnTimePlusSmall");
            _btnTimePlusBig = GetNode<Button>("%BtnTimePlusBig");
            _lblInspectorTime = GetNode<Label>("%LblInspectorTime");
            
            // Pitch
            _btnPitchMinusOct = GetNode<Button>("%BtnPitchMinusOct");
            _btnPitchMinus = GetNode<Button>("%BtnPitchMinus");
            _btnPitchPlus = GetNode<Button>("%BtnPitchPlus");
            _btnPitchPlusOct = GetNode<Button>("%BtnPitchPlusOct");
            _lblInspectorPitch = GetNode<Label>("%LblInspectorPitch");
            
            _btnPlaySFX = GetNode<Button>("%BtnPlaySFX");
            _btnRevertSelected = GetNode<Button>("%BtnRevertSelected");
            _btnApply = GetNode<Button>("%BtnApply");
            
            _confirmRevert = GetNode<ConfirmationDialog>("%ConfirmRevert");
            
            SetupLogic();
            ApplyStyling();
        }

        public void Initialise(EditorContext context)
        {
            _context = context;
            
            // Connect to Signals/Events
            _context.OnSelectionChanged += OnSelectionChanged;
            _context.Connect(EditorContext.SignalName.PlaybackTimeUpdated, Callable.From<float>(OnTimeUpdated));
        }
        
        public override void _ExitTree()
        {
            if (_context != null)
            {
                _context.OnSelectionChanged -= OnSelectionChanged;
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
            _btnRevertSelected.Pressed += () => _context?.RevertEdits(_context.SelectedNotes);
            
            _btnPlaySFX.Pressed += () => PlayNoteSFX(System.Linq.Enumerable.FirstOrDefault(_context.SelectedNotes));
            
            _btnPlaySFX.Pressed += () => PlayNoteSFX(System.Linq.Enumerable.FirstOrDefault(_context.SelectedNotes));
            
            // New Inputs
            _btnTimeMinusBig.Pressed += () => ModifyTime(-0.05f);
            _btnTimeMinusSmall.Pressed += () => ModifyTime(-0.01f);
            _btnTimePlusSmall.Pressed += () => ModifyTime(0.01f);
            _btnTimePlusBig.Pressed += () => ModifyTime(0.05f);
            
            _btnPitchMinusOct.Pressed += () => ModifyPitch(-12);
            _btnPitchMinus.Pressed += () => ModifyPitch(-1);
            _btnPitchPlus.Pressed += () => ModifyPitch(1);
            _btnPitchPlusOct.Pressed += () => ModifyPitch(12);
            
            _btnApply.Pressed += ApplyEdits; 
            
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
            _btnPlaySFX.FocusMode = FocusModeEnum.None;
            _btnRevertSelected.FocusMode = FocusModeEnum.None;
            _btnApply.FocusMode = FocusModeEnum.None;
            
            _btnTimeMinusBig.FocusMode = FocusModeEnum.None;
            _btnTimeMinusSmall.FocusMode = FocusModeEnum.None;
            _btnTimePlusSmall.FocusMode = FocusModeEnum.None;
            _btnTimePlusBig.FocusMode = FocusModeEnum.None;
            
            _btnPitchMinusOct.FocusMode = FocusModeEnum.None;
            _btnPitchMinus.FocusMode = FocusModeEnum.None;
            _btnPitchPlus.FocusMode = FocusModeEnum.None;
            _btnPitchPlusOct.FocusMode = FocusModeEnum.None;
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
        private void OnSelectionChanged()
        {
            var notes = _context.SelectedNotes;
            if (notes.Count > 0)
            {
                _inspectorContainer.Visible = true;
                
                // Common Actions
                _btnRevertSelected.Disabled = false;
                _btnApply.Disabled = false;
                
                if (notes.Count == 1)
                {
                    // Single Select: Enable Editing
                    var note = System.Linq.Enumerable.First(notes);
                    _lblInspectorTime.Text = $"{note.Time:F3}";
                    _lblInspectorPitch.Text = $"{note.Pitch:0}";
                    
                    _btnPlaySFX.Disabled = false;
                    SetEditControlsEnabled(true);
                }
                else
                {
                    // Multi Select: Disable specific property editing, Enable Bulk Actions
                    _lblInspectorTime.Text = "---";
                    _lblInspectorPitch.Text = "---";
                    
                    // Optional: Play SFX for multiple? add later: play selected notes with their internal timing diff

                    _btnPlaySFX.Disabled = false; // Let it play first note or we can update handler to play all.
                    // Handler currently plays FirstOrDefault. That's acceptable for "Preview".
                    
                    SetEditControlsEnabled(false);
                }
            }
            else
            {
                _inspectorContainer.Visible = false;
            }
        }
        
        private void SetEditControlsEnabled(bool enabled)
        {
            _btnTimeMinusBig.Disabled = !enabled;
            _btnTimeMinusSmall.Disabled = !enabled;
            _btnTimePlusSmall.Disabled = !enabled;
            _btnTimePlusBig.Disabled = !enabled;
            
            _btnPitchMinusOct.Disabled = !enabled;
            _btnPitchMinus.Disabled = !enabled;
            _btnPitchPlus.Disabled = !enabled;
            _btnPitchPlusOct.Disabled = !enabled;
        }
        
        private void ModifyTime(float delta)
        {
            if (_context == null || _context.SelectedNotes.Count != 1) return;
            
            // Enforce Edit Mode
            if (_context.IsPlaying) _context.TogglePlay();
            
            var note = System.Linq.Enumerable.First(_context.SelectedNotes);
            
            // Snapshot if needed (Logic handles Dirty check internally?)
            // We should ensure Snapshot is captured before first edit.
            if (note.State != NoteEvent.NoteState.Dirty)
            {
                 _context.CaptureSnapshot(_context.SelectedNotes);
            }
            
            note.Time += delta;
            if (note.Time < 0) note.Time = 0;
            
            // Sort & Update
            _context.CurrentBeatmap.Sort();
            _context.RefreshSelectionUI(); // Updates Labels
            _context.EmitSignal(EditorContext.SignalName.BeatmapLoaded); // Refresh Visuals
        }
        
        private void ModifyPitch(int delta)
        {
            if (_context == null || _context.SelectedNotes.Count != 1) return;
            
            // Enforce Edit Mode
            if (_context.IsPlaying) _context.TogglePlay();
            
            var note = System.Linq.Enumerable.First(_context.SelectedNotes);
            
            if (note.State != NoteEvent.NoteState.Dirty)
            {
                 _context.CaptureSnapshot(_context.SelectedNotes);
            }
            
            note.Pitch += delta;
            if (note.Pitch < 0) note.Pitch = 0;
            if (note.Pitch > 127) note.Pitch = 127;
            
            _context.RefreshSelectionUI();


            GD.Print($"[SongControl] Pitch: {note.Pitch}");
        }
        private void PlayNoteSFX(NoteEvent note)
        {
            if (_context == null || note == null) return;
            
            // Map Type
            var src = (note.Source ?? "").ToLower();
            int midi = (int)note.Pitch;
            global::AudioSystem.SFXResource res = null;
            
            if (src.Contains("drum"))
            {
                 // Simple mapping
                 var type = VocalSynthesiser.InstrumentType.Snare;
                 if (midi < 38) type = VocalSynthesiser.InstrumentType.Kick;
                 res = VocalSynthesiser.GenerateDrums(type);
            }
            else if (src.Contains("vocal"))
            {
                 res = VocalSynthesiser.GenerateVocal(midi, VocalSynthesiser.VowelType.A);
            }
            else
            {
                 // Default Instrument
                 res = VocalSynthesiser.GenerateInstrument(VocalSynthesiser.InstrumentType.Piano, midi);
            }
            
            if (res != null && res.Clips.Length > 0 && res.Clips[0] != null)
            {
                 _context.AudioController.PlayOneShot(res.Clips[0]);
            }
        }
        private void ApplyEdits()
        {
            if (_context == null || _context.SelectedNotes.Count == 0) return;
            
            bool changed = false;
            foreach(var note in _context.SelectedNotes)
            {
                if (note.State == NoteEvent.NoteState.Dirty)
                {
                    note.State = NoteEvent.NoteState.Edited;
                    changed = true;
                }
            }
            
            if (changed)
            {
                _context.RefreshSelectionUI();
                _context.EmitSignal(EditorContext.SignalName.BeatmapLoaded); // Refresh Visuals (Hide Ghosts)
                GD.Print("[SongControl] Applied edits. Ghosts hidden.");
            }
        }
    }
}
