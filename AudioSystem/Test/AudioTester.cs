using Godot;
using AudioSystem;
public partial class AudioTester : Node3D
{
    [ExportGroup("Resources")]
    [Export] public SFXResource SfxOneShot;
    [Export] public SFXResource SfxLoop;
    [Export] public SFXSequence SfxSequence;
    [Export] public MusicResource MusicTrackA;
    [Export] public MusicResource MusicTrackB;
    [Export] public MusicPlaylist Playlist;

    private Node3D _movingTarget;
    private float _moveTime;
    private AudioHandle _loopHandle;

    public override void _Ready()
    {
        _movingTarget = new Node3D { Name = "MovingTarget" };
        var meshInstance = new MeshInstance3D 
        { 
            Mesh = new BoxMesh { Size = new Vector3(1, 1, 1) } 
        };
        _movingTarget.AddChild(meshInstance);
        AddChild(_movingTarget);

        PrintManual();
    }

    public override void _Process(double delta)
    {
        // Only visual logic remains here
        _moveTime += (float)delta;
        _movingTarget.GlobalPosition = new Vector3(Mathf.Sin(_moveTime) * 10f, 0, 0);
    }

    public override void _Input(InputEvent @event)
    {
        // Filter for Key events only
        if (@event is not InputEventKey keyEvent) return;
        
        // We only want the moment the key is pressed, ignoring release and "Echo" (holding down)
        if (!keyEvent.Pressed || keyEvent.Echo) return;

        switch (keyEvent.Keycode)
        {
            // --- SFX TESTS ---
            case Key.Space:
                GD.Print("Playing 2D SFX (Global)");
                AudioManager.Instance.PlaySFX(SfxOneShot);
                break;

            case Key.Key1:
                GD.Print($"Playing 3D SFX at {_movingTarget.GlobalPosition}");
                AudioManager.Instance.PlaySFX(SfxOneShot, _movingTarget.GlobalPosition);
                break;

            case Key.Key2:
                GD.Print("Playing Attached 3D SFX (Following Target)");
                AudioManager.Instance.PlaySFX(SfxOneShot, Vector3.Zero, _movingTarget);
                break;

            case Key.Key3:
                GD.Print("Testing Spatial Coalescing (Spamming 10 sounds)");
                // We purposefully spam here to test the manager's protection
                for (int i = 0; i < 10; i++)
                {
                    AudioManager.Instance.PlaySFX(SfxOneShot, _movingTarget.GlobalPosition);
                }
                break;

            case Key.Key4:
                GD.Print("Playing Sequence");
                AudioManager.Instance.PlaySequence(SfxSequence, _movingTarget.GlobalPosition);
                break;

            // --- HANDLE CONTROL TESTS ---
            case Key.Key5:
                ToggleLoop();
                break;

            case Key.Key6:
                if (_loopHandle.IsPlaying())
                {
                    float newPitch = (float)GD.RandRange(0.5, 2.0);
                    GD.Print($"Modulating Pitch to {newPitch:F1}");
                    _loopHandle.SetPitch(newPitch);
                }
                else GD.PrintErr("Start Loop (Key 5) first!");
                break;

            // --- MUSIC TESTS ---
            case Key.Q:
                GD.Print("Crossfading to Music A");
                AudioManager.Instance.PlayMusic(MusicTrackA);
                break;

            case Key.W:
                GD.Print("Crossfading to Music B");
                AudioManager.Instance.PlayMusic(MusicTrackB);
                break;

            case Key.E:
                GD.Print("Stopping Music (Fade out)");
                AudioManager.Instance.StopMusic(2.0f);
                break;

            case Key.R:
                GD.Print("Starting Playlist");
                AudioManager.Instance.PlayPlaylist(Playlist);
                break;

            // --- GLOBAL UTILS ---
            case Key.A:
                GD.Print("Pausing All SFX");
                AudioManager.Instance.PauseAllSFX();
                break;

            case Key.S:
                GD.Print("Resuming All SFX");
                AudioManager.Instance.ResumeAllSFX();
                break;

            case Key.D:
                GD.Print("Panic: Stop All SFX");
                AudioManager.Instance.StopAllSFX();
                break;
        }
    }

    private void ToggleLoop()
    {
        if (!_loopHandle.IsPlaying())
        {
            GD.Print("Starting Looped SFX with Handle");
            _loopHandle = AudioManager.Instance.PlaySFX(SfxLoop, Vector3.Zero, _movingTarget);
        }
        else
        {
            GD.Print("Stopping Looped SFX via Handle");
            _loopHandle.Stop();
        }
    }

    private void PrintManual()
    {
        GD.PrintRich("[b]--- AUDIO MANAGER TEST MANUAL ---[/b]");
        GD.PrintRich("[color=green]--- SFX ---[/color]");
        GD.Print("SPACE: Play 2D Global SFX");
        GD.Print("1:     Play 3D SFX at Target Position");
        GD.Print("2:     Play 3D SFX Attached to Target (Moving)");
        GD.Print("3:     Test Spatial Coalescing (Spam 10x)");
        GD.Print("4:     Play SFX Sequence");
        GD.PrintRich("[color=yellow]--- HANDLES ---[/color]");
        GD.Print("5:     Toggle Looping SFX (Start/Stop)");
        GD.Print("6:     Randomize Loop Pitch");
        GD.PrintRich("[color=cyan]--- MUSIC ---[/color]");
        GD.Print("Q:     Play Music A");
        GD.Print("W:     Play Music B");
        GD.Print("E:     Stop Music");
        GD.Print("R:     Start Playlist");
        GD.PrintRich("[color=red]--- GLOBALS ---[/color]");
        GD.Print("A:     Pause All SFX");
        GD.Print("S:     Resume All SFX");
        GD.Print("D:     Stop All SFX (Panic)");
    }
}