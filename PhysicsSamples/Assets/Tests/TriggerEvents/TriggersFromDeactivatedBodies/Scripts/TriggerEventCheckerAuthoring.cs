using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

public struct TriggerEventChecker : IComponentData
{
    public int NumExpectedEvents;
}

[UnityEngine.DisallowMultipleComponent]
public class TriggerEventCheckerAuthoring : UnityEngine.MonoBehaviour
{
    [RegisterBinding(typeof(TriggerEventChecker), "NumExpectedEvents")]
    public int NumExpectedEvents;

    class TriggerEventCheckerBaker : Baker<TriggerEventCheckerAuthoring>
    {
        public override void Bake(TriggerEventCheckerAuthoring authoring)
        {
            TriggerEventChecker component = default(TriggerEventChecker);
            component.NumExpectedEvents = authoring.NumExpectedEvents;
            AddComponent(component);
        }
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
public partial class TriggerEventCheckerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate(GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { typeof(TriggerEventChecker) }
        }));
    }

    public struct CollectTriggerEventsJob : ITriggerEventsJob
    {
        public NativeList<TriggerEvent> m_TriggerEvents;

        public void Execute(TriggerEvent triggerEvent)
        {
            m_TriggerEvents.Add(triggerEvent);
        }
    }

    protected override void OnUpdate()
    {
        // Complete the simulation
        Dependency.Complete();

        NativeList<TriggerEvent> triggerEvents = new NativeList<TriggerEvent>(Allocator.TempJob);

        var collectTriggerEventsJob = new CollectTriggerEventsJob
        {
            m_TriggerEvents = triggerEvents
        };

        // Collect all events
        var handle = collectTriggerEventsJob.Schedule(SystemAPI.GetSingleton<SimulationSingleton>(), Dependency);
        handle.Complete();

        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
        int expectedNumberOfTriggerEvents = 0;

        Entities
            .WithName("ValidateTriggerEventsJob")
            .WithReadOnly(physicsWorld)
            .WithReadOnly(triggerEvents)
            .WithoutBurst()
            .ForEach((ref Entity entity, ref TriggerEventChecker component) =>
            {
                int numTriggerEvents = 0;
                TriggerEvent triggerEvent = default;
                expectedNumberOfTriggerEvents += component.NumExpectedEvents;

                for (int i = 0; i < triggerEvents.Length; i++)
                {
                    if (triggerEvents[i].EntityA == entity || triggerEvents[i].EntityB == entity)
                    {
                        triggerEvent = triggerEvents[i];
                        numTriggerEvents++;
                    }
                }

                Assert.IsTrue(numTriggerEvents == component.NumExpectedEvents, "Missing events!");

                if (numTriggerEvents == 0)
                {
                    return;
                }

                // Even if component.NumExpectedEvents is > 1, we still take one trigger event, and not all, because the only
                // difference will be in ColliderKeys which we're not checking here
                int nonTriggerBodyIndex = triggerEvent.EntityA == entity ? triggerEvent.BodyIndexA : triggerEvent.BodyIndexB;
                int triggerBodyIndex = triggerEvent.EntityA == entity ? triggerEvent.BodyIndexB : triggerEvent.BodyIndexA;

                Assert.IsTrue(nonTriggerBodyIndex == physicsWorld.GetRigidBodyIndex(entity), "Wrong body index!");

                RigidBody nonTriggerBody = physicsWorld.Bodies[nonTriggerBodyIndex];
                RigidBody triggerBody = physicsWorld.Bodies[triggerBodyIndex];

                bool isTrigger = false;
                unsafe
                {
                    ConvexCollider* colliderPtr = (ConvexCollider*)triggerBody.Collider.GetUnsafePtr();
                    var material = colliderPtr->Material;

                    isTrigger = colliderPtr->Material.CollisionResponse == CollisionResponsePolicy.RaiseTriggerEvents;
                }

                Assert.IsTrue(isTrigger, "Event doesn't have valid trigger index");

                float distance = math.distance(triggerBody.WorldFromBody.pos, nonTriggerBody.WorldFromBody.pos);

                Assert.IsTrue(distance < 10.0f, "The trigger index is wrong!");
            }).Run();

        Assert.IsTrue(expectedNumberOfTriggerEvents == triggerEvents.Length, "Incorrect number of events: Expected: " + expectedNumberOfTriggerEvents + " Actual: " + triggerEvents.Length);

        triggerEvents.Dispose();
    }
}
