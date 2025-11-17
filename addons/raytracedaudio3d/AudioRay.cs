using System.Diagnostics;
using Godot;
using Godot.Collections;

public partial class AudioRay3D : RayCast3D {
    public float CastDistance = 0;
    public int MaxBounces = 1;
    Callable rayScatter;

    public float EchoDistance = 0;
    public int EchoCount = 0;
    public int Bounces = 0;
    public bool HasBouncedThisTick = false;
    public int RayCastsThisTick = 0;
    public bool Escaped = false;
    public Vector3 EscapeDir = Vector3.Zero;

    PhysicsDirectSpaceState3D spaceState;


    public AudioRay3D(float raycastDist, int maxBounceCount) {
        TopLevel = true;
        Enabled = false;
        CastDistance = raycastDist;
        MaxBounces = maxBounceCount;
    }

    public override void _EnterTree() {
        Owner = GetParent();
        Debug.Assert(Owner is RaytracedAudioListener3D);
    }

    public override void _Ready() {
        spaceState = GetWorld3D().DirectSpaceState;
        Reset();
    }

    public void Update() {
        if (Escaped || Bounces > MaxBounces) {
            Reset();
        }

        ForceRaycastUpdate();

        RayCastsThisTick += 1;
        Bounces += 1;

        if (!IsColliding()) {
            Escaped = true;
            GlobalPosition += TargetPosition;

            foreach(RaytracedAudioPlayer3D player in GetTree().GetNodesInGroup(RaytracedAudioPlayer3D.ENABLED_GROUP_NAME)) {
                bool hasLineOfSight = CastRay(player.GlobalPosition).Count <= 0;
                player.LowPassRaysCount += hasLineOfSight ? 1 : 0;
            }

            return;
        }

        Vector3 hitPos = GetCollisionPoint();
        Vector3 hitNormal = GetCollisionNormal();
        GlobalPosition = hitPos + hitNormal * 0.1f;
        TargetPosition = TargetPosition.Bounce(hitNormal);
        HasBouncedThisTick = true;

        foreach(RaytracedAudioPlayer3D player in GetTree().GetNodesInGroup(RaytracedAudioPlayer3D.ENABLED_GROUP_NAME)) {
            bool hasLineOfSight = CastRay(player.GlobalPosition).Count <= 0;
            player.LowPassRaysCount += hasLineOfSight ? 1 : 0;
        }

        Vector3 ownerPos = ((RaytracedAudioPlayer3D) Owner).GlobalPosition;
        if (Bounces == 1) {
            EchoDistance = hitPos.DistanceTo(ownerPos);
            EchoCount += 1;
            EscapeDir = TargetPosition.Normalized();

        } else if(CastRay(ownerPos).Count <= 0) {
            EchoDistance = hitPos.DistanceTo(ownerPos);
            EchoCount += 1;
            EscapeDir = ownerPos.DirectionTo(hitPos);

        }
    }

    void Reset() {
        Vector3 dir = (Vector3) rayScatter.Call();

        GlobalPosition = ((RaytracedAudioListener3D) Owner).GlobalPosition;
        TargetPosition = dir * CastDistance;

        Bounces = 0;
        Escaped = false;
        EscapeDir = dir;
        ResetTickStats();
    }

    public void ResetTickStats() {
        HasBouncedThisTick = false;
        RayCastsThisTick = 0;
        EchoDistance = 0;
        EchoCount = 0;
    }

    public void SetScatterModel(RaytracedAudioListener3D.RayScatterModel model) {
        switch(model) {
            case RaytracedAudioListener3D.RayScatterModel.RANDOM:
                rayScatter = Callable.From(randomDir);
                break;

            case RaytracedAudioListener3D.RayScatterModel.XZ:
                rayScatter = Callable.From(randomDirXZPlane);
                break;
            default:
                GD.PrintErr($"Unknown ray scatter model: {model}");
                rayScatter = Callable.From(randomDir);
                break;
        }
    }

    static Vector3 randomDirXZPlane() {
        float yaw = (float) GD.RandRange(0, Mathf.Tau);

        return new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
    }

    static Vector3 randomDir() {
        float theta = (float) GD.RandRange(0f, float.Tau);
        float y = (float) GD.RandRange(-1f, 1f);
        float k = Mathf.Sqrt(1f - y*y);

        return new Vector3(k * Mathf.Cos(theta), k * Mathf.Sin(theta), y);
    }

    Dictionary CastRay(Vector3 to) {
        PhysicsRayQueryParameters3D parameters = PhysicsRayQueryParameters3D.Create(
            GlobalPosition,
            to,
            0b01
        );

        RayCastsThisTick += 1;
        return spaceState.IntersectRay(parameters);
    }
}