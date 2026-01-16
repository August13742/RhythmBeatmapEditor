using Godot;
using System;
using RhythmBeatmapEditor.Core.Editor;
using RhythmBeatmapEditor.Core.Models;
using AudioSystem;

namespace RhythmBeatmapEditor.Editor.Visuals
{
    public partial class NoteInspector : PanelContainer
    {
        private EditorContext _context;
        
        // Inspector UI
        private Control _inspectorContainer; // The VBox inside
        
        // Time Controls
        private Button _btnTimeMinusBig, _btnTimeMinusSmall;
        private Button _btnTimePlusSmall, _btnTimePlusBig;
        private Label _lblInspectorTime;
        
        // Pitch Controls
        private Button _btnPitchMinusOct, _btnPitchMinus;
        private Button _btnPitchPlusOct, _btnPitchPlus;
        private Label _lblInspectorPitch;
        
        private Button _btnPlaySFX; 
        private Button _btnRevertSelected; 
        private Button _btnApply;
        
        public override void _Ready()
        {
            // Bind Nodes
            _inspectorContainer = GetNode<Control>("%Inspector");
            
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
            
            SetupLogic();
        }

        public void Initialise(EditorContext context)
        {
            _context = context;
            _context.OnSelectionChanged += OnSelectionChanged;
            
            // Initial State
            OnSelectionChanged();
        }
        
        public override void _ExitTree()
        {
            if (_context != null)
            {
                _context.OnSelectionChanged -= OnSelectionChanged;
            }
        }
        
        private void SetupLogic()
        {
             // Revert Logic
            _btnRevertSelected.Pressed += () => _context?.RevertEdits(_context.SelectedNotes);
            
            _btnPlaySFX.Pressed += () => PlayNoteSFX(System.Linq.Enumerable.FirstOrDefault(_context.SelectedNotes));
            
            // Time Inputs
            _btnTimeMinusBig.Pressed += () => ModifyTime(-0.05f);
            _btnTimeMinusSmall.Pressed += () => ModifyTime(-0.01f);
            _btnTimePlusSmall.Pressed += () => ModifyTime(0.01f);
            _btnTimePlusBig.Pressed += () => ModifyTime(0.05f);
            
            // Pitch Inputs
            _btnPitchMinusOct.Pressed += () => ModifyPitch(-12);
            _btnPitchMinus.Pressed += () => ModifyPitch(-1);
            _btnPitchPlus.Pressed += () => ModifyPitch(1);
            _btnPitchPlusOct.Pressed += () => ModifyPitch(12);
            
            _btnApply.Pressed += ApplyEdits; 
            
            // Focus Management
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

        private void OnSelectionChanged()
        {
            if (_context == null) return;
            
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
                    // Multi Select: Disable specific property editing (for now)
                    _lblInspectorTime.Text = "---";
                    _lblInspectorPitch.Text = "---";
                    
                    _btnPlaySFX.Disabled = false;
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
            
            if (_context.IsPlaying) _context.TogglePlay();
            
            var note = System.Linq.Enumerable.First(_context.SelectedNotes);
            
            if (note.State != NoteEvent.NoteState.Dirty)
            {
                 _context.CaptureSnapshot(_context.SelectedNotes);
            }
            
            note.Time += delta;
            if (note.Time < 0) note.Time = 0;
            
            _context.CurrentBeatmap.Sort();
            _context.RefreshSelectionUI(); 
            _context.EmitSignal(EditorContext.SignalName.BeatmapLoaded); 
        }
        
        private void ModifyPitch(int delta)
        {
            if (_context == null || _context.SelectedNotes.Count != 1) return;
            
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
        }
        
        private void PlayNoteSFX(NoteEvent note)
        {
            if (_context == null || note == null) return;
            
            var src = (note.Source ?? "").ToLower();
            int midi = (int)note.Pitch;
            SFXResource res = null;
            
            if (src.Contains("drum"))
            {
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
                _context.EmitSignal(EditorContext.SignalName.BeatmapLoaded); 
            }
        }
    }
}
