using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public struct RandomMotion : IComponentData
{
    public float CurrentTime;
    public float3 InitialPosition;
    public float3 DesiredPosition;
    public float Speed;
    public float Tolerance;
    public float3 Range;
}

// This behavior will set a dynamic body's linear velocity to get to randomly selected
// point in space. When the body gets with a specified tolerance of the random position,
// a new random position is chosen and the body starts header there instead.
public class RandomMotionAuthoring : MonoBehaviour
{
    public float3 Range = new float3(1);
}

class RandomMotionAuthoringBaker : Baker<RandomMotionAuthoring>
{
    public override void Bake(RandomMotionAuthoring authoring)
    {
        var length = math.length(authoring.Range);
        var transform = GetComponent<Transform>();
        AddComponent(new RandomMotion
        {
            InitialPosition = transform.position,
            DesiredPosition = transform.position,
            Speed = length * 0.001f,
            Tolerance = length * 0.1f,
            Range = authoring.Range,
        });
    }
}


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(PhysicsSystemGroup))]
public partial class RandomMotionSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<RandomMotion>();
    }

    protected override void OnUpdate()
    {
        var random = new Random();
        float deltaTime = SystemAPI.Time.DeltaTime;
        var stepComponent = HasSingleton<PhysicsStep>() ? GetSingleton<PhysicsStep>() : PhysicsStep.Default;

        Entities
            .WithName("ApplyRandomMotion")
            .WithBurst()
            .ForEach((ref RandomMotion motion, ref PhysicsVelocity velocity, in Translation position, in PhysicsMass mass) =>
            {
                motion.CurrentTime += deltaTime;

                random.InitState((uint)(motion.CurrentTime * 1000));
                var currentOffset = position.Value - motion.InitialPosition;
                var desiredOffset = motion.DesiredPosition - motion.InitialPosition;
                // If we are close enough to the destination pick a new destination
                if (math.lengthsq(position.Value - motion.DesiredPosition) < motion.Tolerance)
                {
                    var min = new float3(-math.abs(motion.Range));
                    var max = new float3(math.abs(motion.Range));
                    desiredOffset = random.NextFloat3(min, max);
                    motion.DesiredPosition = desiredOffset + motion.InitialPosition;
                }
                var offset = desiredOffset - currentOffset;
                // Smoothly change the linear velocity
                velocity.Linear = math.lerp(velocity.Linear, offset, motion.Speed);
                if (mass.InverseMass != 0)
                {
                    velocity.Linear -= stepComponent.Gravity * deltaTime;
                }
            }).Schedule();
    }
}
