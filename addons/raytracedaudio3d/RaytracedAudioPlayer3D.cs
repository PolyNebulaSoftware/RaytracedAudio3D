using Godot;

[GlobalClass]
public partial class RaytracedAudioPlayer3D : AudioStreamPlayer3D {
    public static readonly StringName GROUP_NAME = new("raytraced_audio_player_3d");
    public static readonly StringName ENABLED_GROUP_NAME = new("enabled_raytraced_audio_player_3d");

    const float LOWPASS_MIN_HZ = 250;
    const float LOWPASS_MAX_HZ = 20000;

    static readonly float LOG2 = Mathf.Log(2f);
    static readonly float LOG_MIN_HZ = Mathf.Log(LOWPASS_MIN_HZ) / LOG2;
    static readonly float LOG_MAX_HZ = Mathf.Log(LOWPASS_MAX_HZ) / LOG2;


    [Signal]
    public delegate void EnabledEventHandler();
    [Signal]
    public delegate void DisabledEventHandler();
    [Signal]
    public delegate void AudibleDistanceUpdatedEventHandler(float distance);
    

    [Export]
    private float audibilityThresholdDb = -30;
    public float AudibilityThresholdDb {
        get => audibilityThresholdDb;
        set {
            audibilityThresholdDb = value;

            if (IsNodeReady() && MaxDistance == 0) {
                MaxDistance = CalculateAudibleDistanceThreshold();
                EmitSignal(SignalName.AudibleDistanceUpdated, MaxDistance);
            }
        }
    }

    public int LowPassRaysCount = 0;
    public bool isEnabled = false;

    public override void _EnterTree() {
        AddToGroup(GROUP_NAME);
        Bus = ProjectSettings.GetSetting("raytraced_audio/reverb_bus").ToString();
    }

    public override void _Ready() {
        if (MaxDistance == 0) {
            MaxDistance = CalculateAudibleDistanceThreshold();
            EmitSignal(SignalName.AudibleDistanceUpdated);
        }
    }

    void SetEnable() {
        if (isEnabled) return;

        isEnabled = true;

        int i = CreateBus();
        Bus = AudioServer.GetBusName(i);
        AddToGroup(ENABLED_GROUP_NAME);

        EmitSignal(SignalName.Enabled);
    }

    void SetDisable() {
        if (!isEnabled) return;

        if (Bus == ProjectSettings.GetSetting("raytraced_audio/reverb_bus").ToString()) {
            _setDisable();
            return;
        }

        int idx = AudioServer.GetBusIndex(Bus);
        if (idx == -1) {
            GD.PrintErr($"Audio bus {Bus} not found");
            _setDisable();
            return;
        }

        _setDisable();
        AudioServer.RemoveBus(idx);
    }

    void _setDisable() {
        isEnabled = false;
        Bus = ProjectSettings.GetSetting("raytraced_audio/reverb_bus").ToString();
        RemoveFromGroup(ENABLED_GROUP_NAME);
        LowPassRaysCount = 0;

        EmitSignal(SignalName.Disabled);
    }

    int CreateBus() {
        int i = AudioServer.BusCount;

        AudioServer.AddBus();
        AudioServer.SetBusName(i, GenerateBusName());
        AudioServer.SetBusSend(i, ProjectSettings.GetSetting("raytraced_audio/reverb_bus").ToString());
        AudioServer.AddBusEffect(i, new AudioEffectLowPassFilter());

        return i;
    }

    string GenerateBusName() {
        return $"RTAudioPlayer3D_{Name}{GD.Randi()}";
    }

    bool IsEnabled() {
        return isEnabled;
    }

    public void Update(RaytracedAudioListener3D listener) {
        if (isEnabled) {
            _update(listener.RaysCount, listener.MuffleInterpolation);
        }

        LowPassRaysCount = 0;

        float distSq = GlobalPosition.DistanceSquaredTo(listener.GlobalPosition);
        if (distSq > MaxDistance * MaxDistance || !Playing) {
            SetDisable();
        } else {
            SetEnable();
        }

    }

    void _update(int raysCount, float interpolation) {
        if (Bus == ProjectSettings.GetSetting("raytraced_audio/reverb_bus").ToString()) {
            _setDisable();
            return;
        }
        
        int idx = AudioServer.GetBusIndex(Bus);
        if (idx == -1) {
            GD.PrintErr($"Audio bus {Bus} not found");
            _setDisable();

        } else {
            float ratio = LowPassRaysCount / raysCount;
            AudioEffectLowPassFilter lowPass = (AudioEffectLowPassFilter) AudioServer.GetBusEffect(idx, 0);

            float log_t = Mathf.Lerp(LOG_MIN_HZ, LOG_MAX_HZ, ratio);
            float log_hz = Mathf.Log(lowPass.CutoffHz) / LOG2;

            log_hz = Mathf.Lerp(log_hz, log_t, interpolation);
            lowPass.CutoffHz = Mathf.Pow(2, log_hz);
        }
    }

    float GetVolumeDbFromPos(Vector3 fromPos) {
        float CMP_EPSILON = 0.001f;

        float dist = fromPos.DistanceTo(GlobalPosition);
        float vol = 0;

        switch(AttenuationModel) {
            case AttenuationModelEnum.InverseDistance:
                vol = Mathf.LinearToDb(1f / ((dist / UnitSize) + CMP_EPSILON));
                break;
            case AttenuationModelEnum.InverseSquareDistance:
                float d = (dist / UnitSize);
                vol = Mathf.LinearToDb(1 / (d*d + CMP_EPSILON));
                break;
            case AttenuationModelEnum.Logarithmic:
                vol = -20.0f * Mathf.Log(dist / UnitSize + CMP_EPSILON);
                break;
            default:
                GD.PrintErr($"Unknown attenuation type: {AttenuationModel}");
                break;
        }

        vol = Mathf.Min(vol + VolumeDb, MaxDb);
        return vol;
    }

    float CalculateAudibleDistanceThreshold() {
        if (MaxDistance > 0) return MaxDistance;

        switch(AttenuationModel) {
            case AttenuationModelEnum.InverseDistance:
                float t_lin = Mathf.DbToLinear(AudibilityThresholdDb - VolumeDb);
                return UnitSize / t_lin;

            default:
                GD.PrintErr($"Unknown attenuation model: {AttenuationModel}");
                return 0f;
        }
    }

    bool isAudible(Vector3 fromPos) {
        return GetVolumeDbFromPos(fromPos) >= audibilityThresholdDb;
    }
}