using System.Runtime.CompilerServices;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using Xunit;

namespace PixelEngine.Physics.Tests;

/// <summary>
/// Box2D task bridge 验收测试。
/// 不变式：任务桥接验收场景覆盖动态/静态体与确定性步进。
/// </summary>
public sealed unsafe class Box2DTaskBridgeAcceptanceTests
{
    private const int SerialBodyCount = 8;
    private const int ParallelBodyCount = 48;
    private const int StepCount = 36;
    private const float TimeStep = 1f / 60f;

    /// <summary>
    /// 验证 forceSingleThread + workerCount=1 的 Box2D world step 可复现同初态结果。
    /// </summary>
    [Fact]
    public void ForceSingleThreadWorldStepReplaysSameBodyState()
    {
        // Arrange：准备输入与初始状态
        using JobSystem firstJobs = new(workerCount: 1);
        using Box2DTaskBridge firstBridge = new(firstJobs, forceSingleThread: true);
        SceneSummary first = RunScene(firstBridge, SerialBodyCount);

        using JobSystem secondJobs = new(workerCount: 1);
        using Box2DTaskBridge secondBridge = new(secondJobs, forceSingleThread: true);
        SceneSummary second = RunScene(secondBridge, SerialBodyCount);

        // Assert：验证预期结果
        Assert.Equal(1, first.WorkerCount);
        Assert.Equal(1, second.WorkerCount);
        Assert.Equal(0, first.FaultedCallbackCount);
        Assert.Equal(0, second.FaultedCallbackCount);
        Assert.Equal(first.BodyCount, second.BodyCount);

        for (int i = 0; i < first.BodyCount; i++)
        {
            AssertClose(first.Positions[i].X, second.Positions[i].X, 0.00001f);
            AssertClose(first.Positions[i].Y, second.Positions[i].Y, 0.00001f);
            AssertClose(first.Velocities[i].X, second.Velocities[i].X, 0.00001f);
            AssertClose(first.Velocities[i].Y, second.Velocities[i].Y, 0.00001f);
        }
    }

    /// <summary>
    /// 验证多 worker bridge 注入 worldDef 后，可推进多体场景且统计结果接近串行同场景。
    /// </summary>
    [Fact]
    public void MultiWorkerWorldStepMatchesSerialSceneStatistics()
    {
        // Arrange：准备输入与初始状态
        using JobSystem serialJobs = new(workerCount: 1);
        using Box2DTaskBridge serialBridge = new(serialJobs, forceSingleThread: true);
        SceneSummary serial = RunScene(serialBridge, ParallelBodyCount);

        using JobSystem parallelJobs = new(workerCount: 4);
        using Box2DTaskBridge parallelBridge = new(parallelJobs);
        SceneSummary parallel = RunScene(parallelBridge, ParallelBodyCount);

        // Assert：验证预期结果
        Assert.Equal(4, parallel.WorkerCount);
        Assert.Equal(0, parallel.FaultedCallbackCount);
        Assert.Equal(serial.BodyCount, parallel.BodyCount);
        AssertClose(serial.PositionYSum, parallel.PositionYSum, 0.05f);
        AssertClose(serial.VelocityYSum, parallel.VelocityYSum, 0.05f);
        AssertClose(serial.SpeedSum, parallel.SpeedSum, 0.05f);
    }

    /// <summary>
    /// 验证 EnqueueTask 压力调用不会给出越界 workerIndex，也不会并发复用同一 workerIndex。
    /// </summary>
    [Fact]
    public void EnqueueTaskKeepsWorkerIndexInRangeAndExclusive()
    {
        // Arrange：准备输入与初始状态
        using JobSystem jobs = new(workerCount: 4);
        using Box2DTaskBridge bridge = new(jobs);
        WorkerIndexStats stats = default;

        delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<int, int, uint, void*, void>, int, int, void*, void*, void*> enqueue =
            &Box2DTaskBridge.EnqueueTask;
        delegate* unmanaged[Cdecl]<void*, void*, void> finish = &Box2DTaskBridge.FinishTask;

        void* handle = enqueue(&StressWorkerIndexTask, 4096, 1, &stats, bridge.UserTaskContext);
        finish(handle, bridge.UserTaskContext);

        // Assert：验证预期结果
        Assert.Equal((nint)bridge.UserTaskContext, (nint)handle);
        Assert.Equal(4096, stats.TotalItems);
        Assert.True(stats.CallbackCount > 0);
        Assert.Equal(0, stats.OutOfRangeWorkerCount);
        Assert.Equal(0, stats.ConcurrentReuseCount);
        Assert.Equal(0, bridge.FaultedCallbackCount);
    }

    private static SceneSummary RunScene(Box2DTaskBridge bridge, int bodyCount)
    {
        PhysicsScale.ConfigureBox2DLengthUnits();
        B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
        worldDef.Gravity = new B2Vec2 { X = 0f, Y = 10f };
        worldDef.EnableSleep = 0;
        bridge.ConfigureWorldDef(ref worldDef);

        B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);
        B2BodyId[] bodyIds = new B2BodyId[bodyCount];

        try
        {
            B2Polygon box = CreateBoxPolygon(0.18f, 0.18f);
            for (int i = 0; i < bodyIds.Length; i++)
            {
                bodyIds[i] = CreateDynamicBox(worldId, in box, i);
            }

            for (int i = 0; i < StepCount; i++)
            {
                Box2D.b2World_Step(worldId, TimeStep, subStepCount: 4);
            }

            return CaptureSummary(bodyIds, bridge);
        }
        finally
        {
            Box2D.b2DestroyWorld(worldId);
        }
    }

    private static B2BodyId CreateDynamicBox(B2WorldId worldId, in B2Polygon box, int index)
    {
        int column = index % 12;
        int row = index / 12;
        int velocityBand = index % 5;
        int verticalBand = index % 3;
        int angularBand = index % 7;

        B2BodyDef bodyDef = Box2D.b2DefaultBodyDef();
        bodyDef.Type = B2BodyType.DynamicBody;
        bodyDef.Position = new B2Vec2
        {
            X = -12f + (column * 2.2f),
            Y = -8f - (row * 1.4f),
        };
        bodyDef.LinearVelocity = new B2Vec2
        {
            X = (velocityBand - 2) * 0.07f,
            Y = verticalBand * 0.03f,
        };
        bodyDef.AngularVelocity = (angularBand - 3) * 0.02f;
        bodyDef.EnableSleep = 0;
        bodyDef.IsAwake = 1;

        B2BodyId bodyId = Box2D.b2CreateBody(worldId, in bodyDef);
        B2ShapeDef shapeDef = Box2D.b2DefaultShapeDef();
        shapeDef.Density = 1f;
        _ = Box2D.b2CreatePolygonShape(bodyId, in shapeDef, in box);
        Box2D.b2Body_ApplyMassFromShapes(bodyId);
        return bodyId;
    }

    private static B2Polygon CreateBoxPolygon(float halfWidth, float halfHeight)
    {
        B2Vec2* points = stackalloc B2Vec2[4];
        points[0] = new B2Vec2 { X = -halfWidth, Y = -halfHeight };
        points[1] = new B2Vec2 { X = halfWidth, Y = -halfHeight };
        points[2] = new B2Vec2 { X = halfWidth, Y = halfHeight };
        points[3] = new B2Vec2 { X = -halfWidth, Y = halfHeight };

        B2Hull hull = Box2D.b2ComputeHull(points, 4);
        Assert.Equal(4, hull.Count);
        return PhysicsScale.MakeSharpPolygon(in hull);
    }

    private static SceneSummary CaptureSummary(B2BodyId[] bodyIds, Box2DTaskBridge bridge)
    {
        B2Vec2[] positions = new B2Vec2[bodyIds.Length];
        B2Vec2[] velocities = new B2Vec2[bodyIds.Length];
        double positionYSum = 0d;
        double velocityYSum = 0d;
        double speedSum = 0d;

        for (int i = 0; i < bodyIds.Length; i++)
        {
            B2Vec2 position = Box2D.b2Body_GetPosition(bodyIds[i]);
            B2Vec2 velocity = Box2D.b2Body_GetLinearVelocity(bodyIds[i]);
            positions[i] = position;
            velocities[i] = velocity;
            positionYSum += position.Y;
            velocityYSum += velocity.Y;
            speedSum += Math.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y));
        }

        return new SceneSummary(
            bodyIds.Length,
            bridge.WorkerCount,
            bridge.FaultedCallbackCount,
            positions,
            velocities,
            positionYSum,
            velocityYSum,
            speedSum);
    }

    private static void AssertClose(float expected, float actual, float tolerance)
    {
        Assert.InRange(Math.Abs(actual - expected), 0f, tolerance);
    }

    private static void AssertClose(double expected, double actual, double tolerance)
    {
        Assert.InRange(Math.Abs(actual - expected), 0d, tolerance);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StressWorkerIndexTask(int start, int end, uint workerIndex, void* context)
    {
        WorkerIndexStats* stats = (WorkerIndexStats*)context;
        _ = Interlocked.Add(ref stats->TotalItems, end - start);
        _ = Interlocked.Increment(ref stats->CallbackCount);

        if (workerIndex >= 8)
        {
            _ = Interlocked.Increment(ref stats->OutOfRangeWorkerCount);
            return;
        }

        int worker = (int)workerIndex;
        if (Interlocked.Exchange(ref stats->InUse[worker], 1) != 0)
        {
            _ = Interlocked.Increment(ref stats->ConcurrentReuseCount);
        }

        Thread.SpinWait(2048);
        Volatile.Write(ref stats->InUse[worker], 0);
    }

    private sealed record SceneSummary(
        int BodyCount,
        int WorkerCount,
        int FaultedCallbackCount,
        B2Vec2[] Positions,
        B2Vec2[] Velocities,
        double PositionYSum,
        double VelocityYSum,
        double SpeedSum);

    private struct WorkerIndexStats
    {
        public int TotalItems;
        public int CallbackCount;
        public int OutOfRangeWorkerCount;
        public int ConcurrentReuseCount;
        public fixed int InUse[8];
    }
}
