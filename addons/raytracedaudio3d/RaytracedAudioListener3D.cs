using Godot;
using Godot.Collections;

[GlobalClass]
public partial class RaytracedAudioListener3D : AudioListener3D {
    const float SPEED_OF_SOUND = 343;
    static readonly StringName GROUP_NAME = new("raytraced_audio_listener");

    public enum RayScatterModel {
        RANDOM,
        XZ
    }

    [Signal]
    public delegate void EnabledEventHandler();

    [Signal]
    public delegate void DisabledEventHandler();
    [Signal]
    public delegate void RayConfigurationChangedEventHandler();

    Array<AudioRay3D> rays = [];


    private bool _isEnabled = true;
    [Export]
    public bool IsEnabled {
        get => _isEnabled;
        set {
            if (_isEnabled == value) {
                return;
            }

            _isEnabled = value;

            if (!IsNodeReady()) {
                return;
            }

            if (_isEnabled) {
                Setup();
                MakeCurrent();
                EmitSignal(SignalName.Enabled);
            } else {
                Clear();
                EmitSignal(SignalName.Disabled);
            }
        }
    }

    private bool _autoUpdate = true;
    [Export]
    bool AutoUpdate {
        get => _autoUpdate;
        set {
            _autoUpdate = value;
            SetProcess(AutoUpdate);
        }
    }

    private int _raysCount = 4;
    [Export]
    public int RaysCount {
        get => _raysCount;
        set {
            if (value == _raysCount) return;

            _raysCount = Mathf.Max(value, 1);

            Clear();
            Setup();
            EmitSignal(SignalName.RayConfigurationChanged);
        }
    }

    private float _maxRaycastDist = SPEED_OF_SOUND;
    [Export]
    float MaxRaycastDistance {
        get => _maxRaycastDist;
        set {
            _maxRaycastDist = value;

            UpdateRayConfiguration();
            EmitSignal(SignalName.RayConfigurationChanged);
        }
    }
    private int _maxBounces = 3;
    [Export]
    int MaxBounces {
        get => _maxBounces;
        set {
            _maxBounces = value;
            
            UpdateRayConfiguration();
            EmitSignal(SignalName.RayConfigurationChanged);
        }
    }
    private RayScatterModel _rayScatterModel = RayScatterModel.RANDOM;
    [Export]
    RayScatterModel RaytraceScatterModel {
        get => _rayScatterModel;
        set {
            _rayScatterModel = value;

            UpdateRayConfiguration();
            EmitSignal(SignalName.RayConfigurationChanged);
        }
    }


    [ExportCategory("Muffle")]
    [Export]
    public bool MuffleEnabled = true;
    [Export]
    public float MuffleInterpolation = 0.01f;
    [ExportCategory("Echo")]
    [Export]
    public bool EchoEnabled = true;
    [Export]
    public float EchoRoomSizeMultiplier = 2f;
    [Export]
    public float EchoInterpolation = 0.01f;
    [ExportCategory("Ambient")]
    [Export]
    public bool AmbientEnabled = true;
    [Export]
    public float AmbientPanInterpolation = 0.02f;
    [Export]
    public float AmbientPanStrength = 1;
    [Export]
    public float AmbientVolumeInterpolation = 0.01f;
    [Export]
    public float AmbientVolumeAttenuation = 0.998f;

    float roomSize = 0;
    float ambience = 0;

    Vector3 ambientDir = Vector3.Zero;

    int rayCastsThisTick = 0;

    AudioEffectReverb reverbEffect;
    AudioEffectPanner panEffect;

    bool debugMonitorsSetup = false;

    public override void _EnterTree() {
        AddToGroup(GROUP_NAME);
    }

    public override void _Ready() {
        // Reverb
        int i = AudioServer.GetBusIndex(ProjectSettings.GetSetting("raytraced_audio/reverb_bus").ToString());
        if (i == -1) {
            GD.PrintErr($"Failed to get reverb bus for raytraced audio. Disabling echo...");
            EchoEnabled = false;
        } else {
            reverbEffect = (AudioEffectReverb) AudioServer.GetBusEffect(i, 0);
        }

        // Pan
        i = AudioServer.GetBusIndex(ProjectSettings.GetSetting("raytraced_audio/ambient_bus").ToString());
        if (i == -1) {
            GD.PrintErr($"Failed to get ambient bus for raytraced audio. Disabling ambience...");
            AmbientEnabled = false;
        } else {
            panEffect = (AudioEffectPanner) AudioServer.GetBusEffect(i, 0);
        }

        if (_isEnabled) {
            Setup();
        }

        SetProcess(AutoUpdate);
        if (_isEnabled) {
            MakeCurrent();
        }
    }

    void Setup() {
        for(int a=0; a< _raysCount; a++) {
            AudioRay3D rc = new(_maxRaycastDist, _maxBounces);
            rc.SetScatterModel(_rayScatterModel);
            AddChild(rc, false, InternalMode.Back);
            rays.Add(rc);
        }
    }

    void Clear() {
        foreach(AudioRay3D ray in rays) {
            RemoveChild(ray);
            ray.QueueFree();
        }

        rays.Clear();
    }

    public override void _Process(double delta) {
        if (!_autoUpdate) {
            SetProcess(AutoUpdate);
            return;
        }

        Update();
    }

    void Update() {
        rayCastsThisTick = 0;
        if (!_isEnabled) return;

        float echo = 0;
        int echoCount = 0;
        int bouncesThisTick = 0;
        int escapedCount = 0;
        Vector3 escapedDir = Vector3.Zero;
        float escapedStrength = 0;

        foreach(AudioRay3D ray in rays) {
            ray.Update();
            rayCastsThisTick += ray.RayCastsThisTick;

            echo += ray.EchoDistance;
            echoCount += ray.EchoCount;
            bouncesThisTick += ray.HasBouncedThisTick ? 1 : 0;

            if (ray.Escaped) {
                escapedCount += 1;
                escapedStrength += 1 / ray.Bounces;
                escapedDir += ray.EscapeDir;
            }

            ray.ResetTickStats();
        }

        echo = echoCount == 0 ? 0 : (echo / echoCount);
        escapedDir = escapedCount == 0 ? Vector3.Zero : (escapedDir / escapedCount);

        if (MuffleEnabled) {
            UpdateMuffle();
        }
        if (EchoEnabled) {
            UpdateEcho(echo, echoCount, bouncesThisTick);
        }
        if (AmbientEnabled) {
            UpdateAmbient(escapedStrength, escapedDir);
        }
    }

    void UpdateMuffle() {
        foreach(RaytracedAudioPlayer3D player in GetTree().GetNodesInGroup(RaytracedAudioPlayer3D.GROUP_NAME)) {
            player.Update(this);
        }
    }

    void UpdateEcho(float echo, int echoCount, int bounces) {
        roomSize = Mathf.Lerp(roomSize, echo, EchoInterpolation);
        float e = roomSize * EchoRoomSizeMultiplier / SPEED_OF_SOUND;

        reverbEffect.RoomSize = Mathf.Lerp(reverbEffect.RoomSize, Mathf.Clamp(e, 0, 1), EchoInterpolation);
        reverbEffect.PredelayMsec = Mathf.Lerp(reverbEffect.PredelayMsec, e * 1000, EchoInterpolation);
        reverbEffect.PredelayFeedback = Mathf.Lerp(reverbEffect.PredelayFeedback, Mathf.Clamp(e, 0, 0.98f), EchoInterpolation);

        float returnRatio = bounces == 0 ? 0 : ((float) echoCount / (float)bounces);
        reverbEffect.Hipass = Mathf.Lerp(reverbEffect.Hipass, 1-returnRatio, EchoInterpolation);
    }

    void UpdateAmbient(float escapedStrength, Vector3 escapedDir) {
        float ambienceRatio = escapedStrength / RaysCount;

        if (escapedStrength > 0) {
            ambience = Mathf.Lerp(ambience, 1, ambienceRatio);
        } else {
            ambience *= AmbientVolumeAttenuation;
        }

        int ambientBusIdx = AudioServer.GetBusIndex(ProjectSettings.GetSetting("raytraced_audio/ambient_bus").ToString());
        float volume = AudioServer.GetBusVolumeLinear(ambientBusIdx);
        AudioServer.SetBusVolumeLinear(ambientBusIdx, Mathf.Lerp(volume, ambience, AmbientVolumeInterpolation));

        ambientDir = ambientDir.Lerp(escapedDir, AmbientPanInterpolation);
        float targetPan = ambientDir.IsZeroApprox() ? 0 : ((Node3D) Owner).Transform.Basis.X.Dot(ambientDir.Normalized());
        panEffect.Pan = targetPan * AmbientPanStrength;
    }

    void UpdateRayConfiguration() {
        foreach(AudioRay3D ray in rays) {
            ray.CastDistance = _maxRaycastDist;
            ray.MaxBounces = _maxBounces;
            ray.SetScatterModel(_rayScatterModel);
        }
    }
}