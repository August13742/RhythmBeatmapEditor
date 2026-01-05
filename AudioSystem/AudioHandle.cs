using Godot;
namespace AudioSystem{
    public struct AudioHandle
    {
        private AudioManager _manager;
        private int _poolIndex;
        private int _id;

        public static AudioHandle Invalid => new AudioHandle(null, -1, -1);

        public AudioHandle(AudioManager manager, int poolIndex, int id)
        {
            _manager = manager;
            _poolIndex = poolIndex;
            _id = id;
        }

        public bool IsValid => _manager != null && IsInstanceValid(_manager) && _id != -1;

        private static bool IsInstanceValid(GodotObject obj) => obj != null && GodotObject.IsInstanceValid(obj);

        public void Stop()
        {
            if (IsValid) _manager.StopVoice(_poolIndex, _id);
        }

        public bool IsPlaying()
        {
            if (!IsValid) return false;
            return _manager.IsVoicePlaying(_poolIndex, _id);
        }

        public void SetVolume(float linear)
        {
            if (IsValid) _manager.SetVoiceVolume(_poolIndex, _id, Mathf.Clamp(linear, 0f, 1f));
        }

        public void SetPitch(float pitch)
        {
            if (IsValid) _manager.SetVoicePitch(_poolIndex, _id, pitch);
        }
    }
}