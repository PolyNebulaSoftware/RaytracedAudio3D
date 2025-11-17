#if TOOLS
using Godot;

[Tool]
public partial class RaytracedAudio3DPlugin : EditorPlugin {
    static readonly StringName REVERB_SETTING_KEY = new("RaytracedReverb");
    static readonly StringName AMBIENT_SETTING_KEY = new("RaytracedAmbient");
    
    public override void _EnterTree() {
        SetupSettings();
        SetupAudioBuses();
    }

    public override void _ExitTree() {
        CleanUpSettings();
        CleanUpAudioBuses();
    }

    static void SetupSettings() {
		ProjectSettings.SetSetting("raytraced_audio/reverb_bus", REVERB_SETTING_KEY);
		ProjectSettings.SetSetting("raytraced_audio/ambient_bus", AMBIENT_SETTING_KEY);

		if ((float) ProjectSettings.GetSetting("audio/general/3d_panning_strength") < 1f) {
            GD.Print($"[INFO] RaytracedAudio: I recommend setting Audio/General/3d_panning_strength in Project Settings to 1.0 or above");
        }
    }

    static void CleanUpSettings() {
		ProjectSettings.SetSetting("raytraced_audio/reverb_bus", Variant.CreateFrom<string>(null));
		ProjectSettings.SetSetting("raytraced_audio/ambient_bus", Variant.CreateFrom<string>(null));
    }

    static void SetupAudioBuses() {
		GD.Print($"[INFO] Raytraced Audio: setting up audio buses");

        CleanUpAudioBuses();
		
		// Reverb
		int i = AudioServer.BusCount;
		AudioServer.AddBus();
		AudioServer.SetBusName(i, (string) ProjectSettings.GetSetting("raytraced_audio/reverb_bus", REVERB_SETTING_KEY));
		AudioServer.SetBusSend(i, "Master");

        AudioEffectReverb reverb = new() {
            Hipass = 1f,
            ResourceName = "reverb"
        };
        AudioServer.AddBusEffect(i, reverb);


		// Ambient
		i = AudioServer.BusCount;
		AudioServer.AddBus();
		AudioServer.SetBusName(i, (string) ProjectSettings.GetSetting("raytraced_audio/ambient_bus", AMBIENT_SETTING_KEY));
		AudioServer.SetBusSend(i, "Master");

        AudioEffectPanner panner = new() {
            ResourceName = "pan"
        };
        AudioServer.AddBusEffect(i, panner);

        // Godot bug causing ambient bus not show up properly
        // https://github.com/WhoStoleMyCoffee/raytraced-audio/blob/4a09784b908263482a17ad2fc0d7f768cd2b9ba1/addons/raytraced_audio/plugin.gd#L69
		i = AudioServer.BusCount;
		AudioServer.AddBus();
        AudioServer.RemoveBus(i);
    }

    static void CleanUpAudioBuses() {
		int i = AudioServer.GetBusIndex((StringName) ProjectSettings.GetSetting("raytraced_audio/reverb_bus", REVERB_SETTING_KEY));
		if (i != -1) {
            AudioServer.RemoveBus(i);
        }

		i = AudioServer.GetBusIndex((StringName) ProjectSettings.GetSetting("raytraced_audio/ambient_bus", AMBIENT_SETTING_KEY));
		if (i != -1) {
            AudioServer.RemoveBus(i);
        }
    }
}

#endif
