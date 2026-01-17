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
        
        private OptionButton _optSource;

        public override void _Ready()
        {
            // Bind Nodes
            _inspectorContainer = GetNode<Control>("%Inspector");
            _optSource = GetNode<OptionButton>("%OptSource");
            
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
        
        private bool _isEditMode = true;
        public void SetEditMode(bool enabled)
        {
            _isEditMode = enabled;
            if (!enabled)
            {
                _inspectorContainer.Visible = false;
            }
            else
            {
                // Restore visibility if selected
                OnSelectionChanged();
            }
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
            
            // Source Input
            _optSource.ItemSelected += OnSourceSelected;
            
            _btnApply.Pressed += ApplyEdits; 
            
            // Focus Management
            _btnPlaySFX.FocusMode = FocusModeEnum.None;
            _btnRevertSelected.FocusMode = FocusModeEnum.None;
            _btnApply.FocusMode = FocusModeEnum.None;
            _optSource.FocusMode = FocusModeEnum.None;
            
            _btnTimeMinusBig.FocusMode = FocusModeEnum.None;
            _btnTimeMinusSmall.FocusMode = FocusModeEnum.None;
            _btnTimePlusSmall.FocusMode = FocusModeEnum.None;
            _btnTimePlusBig.FocusMode = FocusModeEnum.None;
            
            _btnPitchMinusOct.FocusMode = FocusModeEnum.None;
            _btnPitchMinus.FocusMode = FocusModeEnum.None;
            _btnPitchPlus.FocusMode = FocusModeEnum.None;
            _btnPitchPlusOct.FocusMode = FocusModeEnum.None;
        }

        // Helper to map index to source string
        private string GetSourceFromIndex(int index)
        {
            return index switch {
                0 => "vocals_lead",
                1 => "guitar",
                2 => "piano",
                3 => "bass",
                4 => "drums",
                _ => "other"
            };
        }
        
        // Helper to map source string to index
        private int GetIndexFromSource(string src)
        {
            if (string.IsNullOrEmpty(src)) return 5;
            src = src.ToLower();
            if (src.Contains("vocal")) return 0;
            if (src.Contains("guitar")) return 1;
            if (src.Contains("piano")) return 2;
            if (src.Contains("bass")) return 3;
            if (src.Contains("drum")) return 4;
            return 5; 
        }

        private void OnSourceSelected(long index)
        {
            if (_context == null || _context.SelectedNotes.Count != 1) return;
            
            var note = System.Linq.Enumerable.First(_context.SelectedNotes);
            if (note.State != NoteEvent.NoteState.Dirty) _context.CaptureSnapshot(_context.SelectedNotes);
            
            note.Source = GetSourceFromIndex((int)index);
            
            _context.RefreshSelectionUI(); 
            _context.EmitSignal(EditorContext.SignalName.BeatmapLoaded); // Trigger redraw
        }

        private void OnSelectionChanged()
        {
            if (_context == null) return;
            if (!_isEditMode) 
            {
                _inspectorContainer.Visible = false;
                return;
            }
            
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
                    _optSource.Select(GetIndexFromSource(note.Source));
                    _optSource.Disabled = false;
                    
                    _btnPlaySFX.Disabled = false;
                    SetEditControlsEnabled(true);
                }
                else
                {
                    // Multi Select: Disable specific property editing (for now)
                    _lblInspectorTime.Text = "---";
                    _lblInspectorPitch.Text = "---";
                    _optSource.Disabled = true;
                    
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
            
            int lane = note.Lane;
            int maxLanes = _context.MaxLanes;

            if (src.Contains("drum"))
            {
                 var type = VocalSynthesiser.InstrumentType.Snare;
                 if (midi < 38) type = VocalSynthesiser.InstrumentType.Kick;
                 res = VocalSynthesiser.GenerateDrums(type, volume: note.Volume, lane: lane, maxLanes: maxLanes);
            }
            else if (src.Contains("vocal"))
            {
                 res = VocalSynthesiser.GenerateVocal(midi, VocalSynthesiser.VowelType.A, volume: note.Volume, lane: lane, maxLanes: maxLanes);
            }
            else
            {
                 res = VocalSynthesiser.GenerateInstrument(midi, duration: 0.15f, volume: note.Volume, lane: lane, maxLanes: maxLanes);
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
