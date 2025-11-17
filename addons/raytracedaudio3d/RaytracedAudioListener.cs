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


    private bool isEnabled = true;
    [Export]
    bool IsEnabled {
        get => isEnabled;
        set {
            if (isEnabled == value) return;
            isEnabled = value;

            if (isEnabled) {
                if (IsNodeReady()) {
                    Setup();
                }
                EmitSignal(SignalName.Enabled);
            } else {
                EmitSignal(SignalName.Disabled);
                if (IsNodeReady()) {
                    Clear();
                }
            }
        }
    }

    private bool autoUpdate = true;
    [Export]
    bool AutoUpdate {
        get => autoUpdate;
        set {
            autoUpdate = value;
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

    private float maxRaycastDist = SPEED_OF_SOUND;
    [Export]
    float MaxRaycastDistance {
        get => maxRaycastDist;
        set {
            maxRaycastDist = value;

            UpdateRayConfiguration();
            EmitSignal(SignalName.RayConfigurationChanged);
        }
    }
    private int maxBounces = 3;
    [Export]
    int MaxBounces {
        get => maxBounces;
        set {
            maxBounces = value;
            
            UpdateRayConfiguration();
            EmitSignal(SignalName.RayConfigurationChanged);
        }
    }
    private RayScatterModel rayScatterModel = RayScatterModel.RANDOM;
    [Export]
    RayScatterModel RaytraceScatterModel {
        get => rayScatterModel;
        set {
            rayScatterModel = value;

            UpdateRayConfiguration();
            EmitSignal(SignalName.RayConfigurationChanged);
        }
    }


    [ExportCategory("Muffle")]
    [Export]
    bool muffleEnabled = true;
    [Export]
    public float MuffleInterpolation = 0.01f;
    [ExportCategory("Echo")]
    [Export]
    bool echoEnabled = true;
    [Export]
    float echoRoomSizeMultiplier = 2f;
    [Export]
    float echoInterpolation = 0.01f;
    [ExportCategory("Ambient")]
    [Export]
    bool ambientEnabled = true;
    [Export]
    float ambientPanInterpolation = 0.02f;
    [Export]
    float ambientPanStrength = 1;
    [Export]
    float ambientVolumeInterpolation = 0.01f;
    [Export]
    float ambientVolumeAttenuation = 0.998f;

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
            echoEnabled = false;
        } else {
            reverbEffect = (AudioEffectReverb) AudioServer.GetBusEffect(i, 0);
        }

        // Pan
        i = AudioServer.GetBusIndex(ProjectSettings.GetSetting("raytraced_audio/ambient_bus").ToString());
        if (i == -1) {
            GD.PrintErr($"Failed to get ambient bus for raytraced audio. Disabling ambience...");
            ambientEnabled = false;
        } else {
            panEffect = (AudioEffectPanner) AudioServer.GetBusEffect(i, 0);
        }

        if (isEnabled) {
            Setup();
        }

        SetProcess(AutoUpdate);
        if (isEnabled) {
            MakeCurrent();
        }
    }

    void Setup() {
        for(int a=0; a< _raysCount; a++) {
            AudioRay3D rc = new(maxRaycastDist, maxBounces);
            rc.SetScatterModel(rayScatterModel);
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
        if (!autoUpdate) {
            SetProcess(AutoUpdate);
            return;
        }

        Update();
    }

    void Update() {
        rayCastsThisTick = 0;
        if (!isEnabled) return;

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

        if (muffleEnabled) {
            updateMuffle();
        }
        if (echoEnabled) {
            updateEcho(echo, echoCount, bouncesThisTick);
        }
        if (ambientEnabled) {
            updateAmbient(escapedStrength, escapedDir);
        }
    }

    void updateMuffle() {
        foreach(RaytracedAudioPlayer3D player in GetTree().GetNodesInGroup(RaytracedAudioPlayer3D.GROUP_NAME)) {
            player.Update(this);
        }
    }

    void updateEcho(float echo, int echoCount, int bounces) {
        roomSize = Mathf.Lerp(roomSize, echo, echoInterpolation);
        float e = (roomSize * echoRoomSizeMultiplier) / SPEED_OF_SOUND;

        reverbEffect.RoomSize = Mathf.Lerp(reverbEffect.RoomSize, Mathf.Clamp(e, 0, 1), echoInterpolation);
        reverbEffect.PredelayMsec = Mathf.Lerp(reverbEffect.PredelayMsec, e=1000, echoInterpolation);
        reverbEffect.PredelayFeedback = Mathf.Lerp(reverbEffect.PredelayFeedback, Mathf.Clamp(e, 0, 0.98f), echoInterpolation);

        float returnRatio = bounces == 0 ? 0 : (echoCount/bounces);
        reverbEffect.Hipass = Mathf.Lerp(reverbEffect.Hipass, 1-returnRatio, echoInterpolation);
    }

    void updateAmbient(float escapedStrength, Vector3 escapedDir) {
        float ambienceRatio = escapedStrength / RaysCount;

        if (escapedStrength > 0) {
            ambience = Mathf.Lerp(ambience, 1, ambienceRatio);
        } else {
            ambience *= ambientVolumeAttenuation;
        }

        int ambientBusIdx = AudioServer.GetBusIndex(ProjectSettings.GetSetting("raytraced_audio/ambient_bus").ToString());
        float volume = AudioServer.GetBusVolumeLinear(ambientBusIdx);
        AudioServer.SetBusVolumeLinear(ambientBusIdx, Mathf.Lerp(volume, ambience, ambientVolumeInterpolation));

        ambientDir = ambientDir.Lerp(escapedDir, ambientPanInterpolation);
        float targetPan = ambientDir.IsZeroApprox() ? 0 : ((RaytracedAudioListener3D) Owner).Transform.Basis.X.Dot(ambientDir.Normalized());
        panEffect.Pan = targetPan * ambientPanStrength;
    }

    void UpdateRayConfiguration() {
        foreach(AudioRay3D ray in rays) {
            ray.CastDistance = maxRaycastDist;
            ray.MaxBounces = maxBounces;
            ray.SetScatterModel(rayScatterModel);
        }
    }
}