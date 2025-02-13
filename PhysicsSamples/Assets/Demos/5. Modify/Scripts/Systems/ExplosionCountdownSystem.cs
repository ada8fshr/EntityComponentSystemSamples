using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Unity.Transforms;

public struct ExplosionCountdown : IComponentData
{
    public Entity Source;
    public int Countdown;
    public float3 Center;
    public float Force;
}

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(PhysicsSystemGroup))]
public partial class ExplosionCountdownSystem : SystemBase
{
    private EndFixedStepSimulationEntityCommandBufferSystem m_CommandBufferSystem;

    protected override void OnCreate()
    {
        m_CommandBufferSystem = World.GetOrCreateSystemManaged<EndFixedStepSimulationEntityCommandBufferSystem>();
        RequireForUpdate<ExplosionCountdown>();
    }

    protected override void OnUpdate()
    {
        var commandBufferParallel = m_CommandBufferSystem.CreateCommandBuffer().AsParallelWriter();

        var timeStep = SystemAPI.Time.DeltaTime;
        var up = math.up();

        var positions = GetComponentLookup<Translation>(true);

        Entities
            .WithName("ExplosionCountdown_Tick")
            .WithReadOnly(positions)
            .WithBurst()
            .ForEach((Entity entity, ref ExplosionCountdown explosion) =>
            {
                explosion.Countdown--;
                bool bang = explosion.Countdown <= 0;
                if (bang && !explosion.Source.Equals(Entity.Null))
                {
                    explosion.Center = positions[explosion.Source].Value;
                }
            }).ScheduleParallel();

        Entities
            .WithName("ExplosionCountdown_Bang")
            .WithBurst()
            .ForEach((int entityInQueryIndex, Entity entity,
                ref ExplosionCountdown explosion, ref PhysicsVelocity pv,
                in PhysicsMass pm, in PhysicsCollider collider,
                in Translation pos, in Rotation rot) =>
                {
                    if (0 < explosion.Countdown) return;

                    pv.ApplyExplosionForce(pm, collider, pos, rot,
                        explosion.Force, explosion.Center, 0, timeStep, up);

                    commandBufferParallel.RemoveComponent<ExplosionCountdown>(entityInQueryIndex, entity);
                }).Schedule();
    }
}
