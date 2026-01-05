using Godot;
using System.Collections.Generic;
using AudioSystem;
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    #region Configuration
    [Export] private int _voicePoolSize = 64;
    [Export] private string _masterBusName = "Master";
    [Export] private string _musicBusName = "Music";
    [Export] private string _sfxBusName = "SFX";
    #endregion

    #region Debug
    // Visible in Remote Tree
    public int ActiveVoiceCount => _activeVoiceCount;
    public string CurrentMusicName { get; private set; } = "None";
    private int _activeVoiceCount = 0;
    #endregion

    #region Internal Structures

    private class ActiveVoice
    {
        public AudioStreamPlayer Player2D;
        public AudioStreamPlayer3D Player3D;
        
        public int PoolIndex;
        public int Id; // Generation ID
        public int Priority;
        public Node3D FollowTarget; // Only used for 3D
        public bool IsClaimed;
        
        // Helper to check if currently outputting sound
        public bool IsPlaying => (Player2D != null && Player2D.Playing) || (Player3D != null && Player3D.Playing);
        
        public void Stop()
        {
            if (Player2D != null) Player2D.Stop();
            if (Player3D != null) Player3D.Stop();
        }
    }

    private ActiveVoice[] _voicePool;
    private int _globalVoiceIdCounter = 0;

    // Music
    private AudioStreamPlayer _musicSourceA;
    private AudioStreamPlayer _musicSourceB;
    private bool _isUsingMusicA = true;
    private Tween _musicFadeTween;

    // Playlist
    private MusicPlaylist _activePlaylist;
    private List<int> _playlistQueue = new();
    private bool _playlistActive = false;

    // Sequence & Coalescing
    private Dictionary<int, int> _sequenceIndices = new();
    private Dictionary<SFXResource, List<Vector3>> _coalesceBuffer = new();
    #endregion

    #region Lifecycle
    public override void _EnterTree()
    {
        if (Instance != null)
        {
            QueueFree();
            return;
        }
        Instance = this;
        InitialisePool();
        InitialiseMusic();
    }

    public override void _Process(double delta)
    {
        // Spatial Coalescing Reset
        foreach (var list in _coalesceBuffer.Values) list.Clear();
        
        // Voice Cleanup / Follow Logic
        for (int i = 0; i < _voicePoolSize; i++)
        {
            var voice = _voicePool[i];
            if (!voice.IsClaimed) continue;

            // Auto-release if finished
            if (!voice.IsPlaying && !IsVoicePaused(voice)) 
            {
                ReleaseVoice(voice);
                continue;
            }

            // Follow Target Logic (Only for 3D)
            if (voice.FollowTarget != null && IsInstanceValid(voice.FollowTarget) && voice.Player3D.Playing)
            {
                voice.Player3D.GlobalPosition = voice.FollowTarget.GlobalPosition;
            }
        }
        
        // Playlist Logic
        if (_playlistActive && _activePlaylist != null)
        {
            UpdatePlaylistLogic();
        }
    }
    
    // Helper to distinguish "Finished" from "Paused" (Godot doesn't distinguish nicely in StreamPlayer.Playing)
    private bool IsVoicePaused(ActiveVoice v) 
    {
        bool p2 = v.Player2D != null && v.Player2D.StreamPaused;
        bool p3 = v.Player3D != null && v.Player3D.StreamPaused;
        return p2 || p3;
    }
    #endregion

    #region Initialization
    private void InitialisePool()
    {
        _voicePool = new ActiveVoice[_voicePoolSize];
        Node poolRoot = new Node { Name = "SFX_Pool" };
        AddChild(poolRoot);

        for (int i = 0; i < _voicePoolSize; i++)
        {
            var p2d = new AudioStreamPlayer { Name = $"V{i}_2D", Bus = _sfxBusName };
            var p3d = new AudioStreamPlayer3D { Name = $"V{i}_3D", Bus = _sfxBusName };
            
            poolRoot.AddChild(p2d);
            poolRoot.AddChild(p3d);

            _voicePool[i] = new ActiveVoice
            {
                Player2D = p2d,
                Player3D = p3d,
                PoolIndex = i,
                IsClaimed = false,
                Id = -1
            };
        }
    }

    private void InitialiseMusic()
    {
        Node musicRoot = new Node { Name = "Music_System" };
        AddChild(musicRoot);

        _musicSourceA = new AudioStreamPlayer { Name = "MusicA", Bus = _musicBusName };
        _musicSourceB = new AudioStreamPlayer { Name = "MusicB", Bus = _musicBusName };
        
        musicRoot.AddChild(_musicSourceA);
        musicRoot.AddChild(_musicSourceB);
    }
    #endregion

    #region Public API: Volume (Linear 0-1)
    
    public void SetMasterVolume(float linear) => SetBusVolume(_masterBusName, linear);
    public void SetMusicVolume(float linear) => SetBusVolume(_musicBusName, linear);
    public void SetSFXVolume(float linear) => SetBusVolume(_sfxBusName, linear);

    private void SetBusVolume(string busName, float linear)
    {
        int idx = AudioServer.GetBusIndex(busName);
        if (idx == -1) return;

        float db = linear > 0.0001f ? Mathf.LinearToDb(linear) : -80f;
        AudioServer.SetBusVolumeDb(idx, db);
    }
    #endregion

    #region Public API: SFX

    public AudioHandle PlaySFX(SFXResource data, Vector3 position = default, Node3D followTarget = null)
    {
        if (data == null) return AudioHandle.Invalid;

        bool is2D = data.BypassSpatial || (position == Vector3.Zero && followTarget == null);
        Vector3 startPos = followTarget != null ? followTarget.GlobalPosition : position;

        // Spatial Coalescing
        if (!is2D && data.UseSpatialCoalescing)
        {
            if (!_coalesceBuffer.ContainsKey(data)) _coalesceBuffer[data] = new List<Vector3>();
            var playedPositions = _coalesceBuffer[data];
            float sqrThreshold = data.MinSpatialSeparation * data.MinSpatialSeparation;

            foreach(var p in playedPositions)
            {
                if (p.DistanceSquaredTo(startPos) < sqrThreshold) return AudioHandle.Invalid;
            }
            playedPositions.Add(startPos);
        }

        return PlayInternal(data, startPos, followTarget, is2D, 1f);
    }

    public AudioHandle PlaySequence(SFXSequence seq, Vector3 position = default, Node3D followTarget = null)
    {
        if (seq == null || seq.Steps.Count == 0) return AudioHandle.Invalid;

        int idHash = seq.GetHashCode();
        int indexToPlay = 0;

        if (seq.Mode == SFXSequenceMode.Random)
        {
            indexToPlay = GD.RandRange(0, seq.Steps.Count - 1);
        }
        else if (seq.Mode == SFXSequenceMode.RandomNoRepeat)
        {
            if (seq.Steps.Count > 1)
            {
                int lastIndex = _sequenceIndices.ContainsKey(idHash) ? _sequenceIndices[idHash] : -1;
                do { indexToPlay = GD.RandRange(0, seq.Steps.Count - 1); } while (indexToPlay == lastIndex);
            }
        }
        else // Sequential
        {
            int lastIndex = _sequenceIndices.ContainsKey(idHash) ? _sequenceIndices[idHash] : -1;
            indexToPlay = (lastIndex + 1) % seq.Steps.Count;
        }

        _sequenceIndices[idHash] = indexToPlay;
        SFXResource step = seq.Steps[indexToPlay];
        
        bool is2D = step.BypassSpatial || (position == Vector3.Zero && followTarget == null);
        Vector3 startPos = followTarget != null ? followTarget.GlobalPosition : position;

        return PlayInternal(step, startPos, followTarget, is2D, seq.SequenceVolume);
    }
    
    public void StopAllSFX()
    {
        foreach(var v in _voicePool)
        {
            if (v.IsClaimed) ReleaseVoice(v);
        }
    }
    
    public void PauseAllSFX()
    {
        foreach(var v in _voicePool)
        {
            if (v.IsClaimed && v.Player2D.Playing) v.Player2D.StreamPaused = true;
            if (v.IsClaimed && v.Player3D.Playing) v.Player3D.StreamPaused = true;
        }
    }

    public void ResumeAllSFX()
    {
        foreach(var v in _voicePool)
        {
            if (v.IsClaimed)
            {
                if (v.Player2D.StreamPaused) v.Player2D.StreamPaused = false;
                if (v.Player3D.StreamPaused) v.Player3D.StreamPaused = false;
            }
        }
    }
    #endregion

    #region Public API: Music

    public void PlayMusic(MusicResource music)
    {
        StopPlaylist();
        PlayMusicInternal(music, true);
    }

    public void PlayPlaylist(MusicPlaylist playlist)
    {
        if (playlist == null || playlist.Tracks.Count == 0) return;
        if (_activePlaylist == playlist && _playlistActive) return;

        StopPlaylist();
        _activePlaylist = playlist;
        RefillPlaylistQueue();
        _playlistActive = true;
        
        // Trigger first track immediately
        PlayNextInPlaylist(); 
    }

    public void StopMusic(float fadeOutDuration = 1.0f)
    {
        StopPlaylist();
        
        AudioStreamPlayer active = _isUsingMusicA ? _musicSourceA : _musicSourceB;
        if (!active.Playing) return;

        if (_musicFadeTween != null && _musicFadeTween.IsValid()) _musicFadeTween.Kill();
        _musicFadeTween = CreateTween();
        
        _musicFadeTween.TweenProperty(active, "volume_db", -80f, fadeOutDuration);
        _musicFadeTween.TweenCallback(Callable.From(active.Stop));
        
        CurrentMusicName = "None";
    }

    private void StopPlaylist()
    {
        _playlistActive = false;
        _activePlaylist = null;
        _playlistQueue.Clear();
    }
    #endregion

    #region Internal Logic (SFX)

    private AudioHandle PlayInternal(SFXResource data, Vector3 pos, Node3D follow, bool force2D, float volumeMult)
    {
        if (data.Clips == null || data.Clips.Length == 0) return AudioHandle.Invalid;

        ActiveVoice voice = GetBestVoice(data.Priority);
        if (voice == null) return AudioHandle.Invalid; // Pool exhausted and priorities failed

        // Select Clip
        AudioStream clip = data.Clips[GD.RandRange(0, data.Clips.Length - 1)];
        
        // Calc properties
        float finalVol = Mathf.Clamp(data.Volume + (float)GD.RandRange(-data.VolumeVariance, data.VolumeVariance), 0f, 1f) * volumeMult;
        float finalPitch = Mathf.Clamp(data.Pitch + (float)GD.RandRange(-data.PitchVariance, data.PitchVariance), 0.1f, 3f);
        float dbVol = Mathf.LinearToDb(finalVol);

        // State Update
        voice.IsClaimed = true;
        voice.Priority = data.Priority;
        voice.Id = ++_globalVoiceIdCounter;
        voice.FollowTarget = follow;
        _activeVoiceCount++;

        // Apply to hardware
        if (force2D)
        {
            voice.Player3D.Stop(); // Ensure other side is silent
            
            voice.Player2D.Stream = clip;
            voice.Player2D.VolumeDb = dbVol;
            voice.Player2D.PitchScale = finalPitch;
            voice.Player2D.Bus = string.IsNullOrEmpty(data.BusName) ? _sfxBusName : data.BusName;
            voice.Player2D.Play();
        }
        else
        {
            voice.Player2D.Stop();

            voice.Player3D.Stream = clip;
            voice.Player3D.VolumeDb = dbVol;
            voice.Player3D.PitchScale = finalPitch;
            voice.Player3D.Bus = string.IsNullOrEmpty(data.BusName) ? _sfxBusName : data.BusName;
            
            // Spatial settings
            voice.Player3D.UnitSize = data.MinDistance;
            voice.Player3D.MaxDistance = data.MaxDistance;
            voice.Player3D.GlobalPosition = pos;
            voice.Player3D.Play();
        }

        return new AudioHandle(this, voice.PoolIndex, voice.Id);
    }

    private ActiveVoice GetBestVoice(int priority)
    {
        ActiveVoice bestCandidate = null;
        int lowestPriorityFound = -1; // 0 is high, 256 is low.

        // Linear scan is fine for < 100 items
        for (int i = 0; i < _voicePool.Length; i++)
        {
            var v = _voicePool[i];
            if (!v.IsClaimed) return v;

            if (v.Priority > priority) // v is less important than requested sound
            {
                if (v.Priority > lowestPriorityFound)
                {
                    lowestPriorityFound = v.Priority;
                    bestCandidate = v;
                }
            }
        }

        if (bestCandidate != null)
        {
            // Steal
            bestCandidate.Stop();
            // Don't modify ActiveVoiceCount, we are swapping
            return bestCandidate;
        }

        return null;
    }

    private void ReleaseVoice(ActiveVoice voice)
    {
        voice.Stop();
        voice.IsClaimed = false;
        voice.FollowTarget = null;
        _activeVoiceCount--;
    }

    #endregion

    #region Internal Logic (Music)

    private void PlayMusicInternal(MusicResource music, bool loop)
    {
        if (music == null || music.Clip == null) return;

        AudioStreamPlayer active = _isUsingMusicA ? _musicSourceA : _musicSourceB;
        // If same song, just update loop
        if (active.Playing && active.Stream == music.Clip)
        {
            // Godot streams generally bake looping into the import settings,
            // but we can simulate it if the resource has loop points or via logic.
            // For now, we assume Stream Import settings handle internal looping,
            // or we manually re-trigger in playlist logic.
            return; 
        }

        CurrentMusicName = music.Clip.ResourceName;

        // Swap Sources
        AudioStreamPlayer fadingOut = _isUsingMusicA ? _musicSourceA : _musicSourceB;
        AudioStreamPlayer fadingIn = _isUsingMusicA ? _musicSourceB : _musicSourceA;
        _isUsingMusicA = !_isUsingMusicA;

        // Setup Fade
        if (_musicFadeTween != null && _musicFadeTween.IsValid()) _musicFadeTween.Kill();
        _musicFadeTween = CreateTween();
        
        // Setup In
        fadingIn.Stream = music.Clip;
        fadingIn.Bus = string.IsNullOrEmpty(music.BusName) ? _musicBusName : music.BusName;
        fadingIn.VolumeDb = -80f;
        fadingIn.Play();

        float targetDb = Mathf.LinearToDb(music.Volume);
        
        // Parallel Fade
        _musicFadeTween.SetParallel(true);
        _musicFadeTween.TweenProperty(fadingIn, "volume_db", targetDb, music.FadeTime);
        _musicFadeTween.TweenProperty(fadingOut, "volume_db", -80f, music.FadeTime);
        
        // Cleanup Out
        _musicFadeTween.Chain().TweenCallback(Callable.From(fadingOut.Stop));
    }

    private void UpdatePlaylistLogic()
    {
        AudioStreamPlayer active = _isUsingMusicA ? _musicSourceA : _musicSourceB;
        
        // Check if song finished. 
        // Note: Godot's 'Playing' property goes false when finished.
        if (!active.Playing)
        {
            PlayNextInPlaylist();
        }
    }

    private void PlayNextInPlaylist()
    {
        if (_playlistQueue.Count == 0) RefillPlaylistQueue();
        
        int trackIndex = _playlistQueue[0];
        _playlistQueue.RemoveAt(0);
        
        MusicResource nextTrack = _activePlaylist.Tracks[trackIndex];
        // Playlist tracks generally shouldn't loop internally
        PlayMusicInternal(nextTrack, loop: false); 
    }

    private void RefillPlaylistQueue()
    {
        _playlistQueue.Clear();
        for(int i=0; i<_activePlaylist.Tracks.Count; i++) _playlistQueue.Add(i);

        if (_activePlaylist.Mode == PlaybackMode.Shuffle)
        {
            // Fisher-Yates
            int n = _playlistQueue.Count;
            while (n > 1)
            {
                n--;
                int k = GD.RandRange(0, n);
                int value = _playlistQueue[k];
                _playlistQueue[k] = _playlistQueue[n];
                _playlistQueue[n] = value;
            }
        }
    }
    #endregion

    #region Handle Logic
    public void StopVoice(int poolIndex, int id)
    {
        if (poolIndex < 0 || poolIndex >= _voicePoolSize) return;
        var v = _voicePool[poolIndex];
        if (v.IsClaimed && v.Id == id) ReleaseVoice(v);
    }

    public bool IsVoicePlaying(int poolIndex, int id)
    {
        if (poolIndex < 0 || poolIndex >= _voicePoolSize) return false;
        var v = _voicePool[poolIndex];
        return v.IsClaimed && v.Id == id && v.IsPlaying;
    }

    public void SetVoiceVolume(int poolIndex, int id, float linear)
    {
        if (poolIndex < 0 || poolIndex >= _voicePoolSize) return;
        var v = _voicePool[poolIndex];
        if (v.IsClaimed && v.Id == id)
        {
            float db = Mathf.LinearToDb(linear);
            if (v.Player2D.Playing) v.Player2D.VolumeDb = db;
            if (v.Player3D.Playing) v.Player3D.VolumeDb = db;
        }
    }
    
    public void SetVoicePitch(int poolIndex, int id, float pitch)
    {
        if (poolIndex < 0 || poolIndex >= _voicePoolSize) return;
        var v = _voicePool[poolIndex];
        if (v.IsClaimed && v.Id == id)
        {
            if (v.Player2D.Playing) v.Player2D.PitchScale = pitch;
            if (v.Player3D.Playing) v.Player3D.PitchScale = pitch;
        }
    }
    #endregion
}
