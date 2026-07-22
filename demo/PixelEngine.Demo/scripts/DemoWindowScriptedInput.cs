using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 窗口短跑的固定输入脚本，用于在真实窗口相位链路中自动触发玩法动作。
/// </summary>
internal sealed class DemoWindowScriptedInput(EngineProbeApi probe, bool routeProbe = false) : IEnginePhaseDriver
{
    private readonly EngineProbeApi _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly bool _routeProbe = routeProbe;
    private readonly Key[] _keys = new Key[3];
    private readonly MouseButton[] _buttons = new MouseButton[2];
    private PlayerController? _routePlayer;

    /// <summary>
    /// 已注入输入的帧数。
    /// </summary>
    public int FramesInjected { get; private set; }

    /// <summary>
    /// 真实窗口脚本已把 Web UI 从主菜单切换到 gameplay HUD。
    /// </summary>
    public bool UiGameplayStarted { get; private set; }

    /// <summary>
    /// 材质笔刷目标世界坐标。
    /// </summary>
    public Point2F BrushTargetWorld { get; } = new(40f, 240f);

    /// <summary>
    /// 爆破工具目标世界坐标。
    /// </summary>
    public Point2F ExplosionTargetWorld { get; } = new(90f, 240f);

    /// <summary>
    /// 木桥擦除目标世界坐标。
    /// </summary>
    public Point2F BridgeCutTargetWorld { get; } = new(209f, 250f);

    /// <summary>
    /// 完整路线探针中用于打开起点右侧石柱通道的目标世界坐标。
    /// </summary>
    public Point2F RouteColumnTargetWorld { get; } = new(154f, 262f);

    /// <summary>
    /// 可玩 Demo 普通脚本短跑中用于验证射击坍塌的右侧山体目标。
    /// </summary>
    public Point2F PlayableCollapseTargetWorld { get; } = new(360f, 280f);

    /// <summary>
    /// 注册输入注入相位；该 hook 应在 Silk 输入采样之后注册，以便覆盖采样结果。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.InputAndTime, Inject);
    }

    private void Inject(EngineTickContext context)
    {
        _ = context;
        int frame = FramesInjected++;
        StartGameplayUi(frame);
        if (_routeProbe)
        {
            InjectRouteProbe(frame);
            return;
        }

        // 可玩短跑脚本：分段注入移动、跳跃、切枪与射击瞄准
        int keyCount = 0;
        int buttonCount = 0;
        Point2F target = BrushTargetWorld;
        float wheelY = 0f;

        if (frame == 2)
        {
            _keys[keyCount++] = Key.Digit1;
        }
        else if (frame is >= 7 and <= 118)
        {
            _keys[keyCount++] = Key.D;
            if (frame is 28 or 29 or 74 or 75)
            {
                _keys[keyCount++] = Key.Space;
            }
        }
        else if (frame is >= 124 and <= 146)
        {
            if (frame == 124)
            {
                _keys[keyCount++] = Key.Digit1;
            }

            target = PlayableCollapseTargetWorld;
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame is >= 150 and <= 165)
        {
            if (frame == 150)
            {
                _keys[keyCount++] = Key.Digit2;
            }

            target = new Point2F(PlayableCollapseTargetWorld.X + ((frame - 150) % 4 * 4f), PlayableCollapseTargetWorld.Y + ((frame - 150) / 4 * 4f));
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame is >= 166 and <= 176)
        {
            if (frame == 166)
            {
                _keys[keyCount++] = Key.Digit3;
            }

            target = new Point2F(PlayableCollapseTargetWorld.X + 18f, PlayableCollapseTargetWorld.Y - 12f);
            if (frame == 170)
            {
                _buttons[buttonCount++] = MouseButton.Left;
            }
        }
        else if (frame is >= 168 and <= 196)
        {
            _keys[keyCount++] = Key.D;
            if (frame == 176)
            {
                _keys[keyCount++] = Key.Space;
            }
        }
        Point2F screen = _probe.Camera.WorldToScreen(target.X, target.Y);
        _probe.Input.Update(
            _keys.AsSpan(0, keyCount),
            _buttons.AsSpan(0, buttonCount),
            screen.X,
            screen.Y,
            wheelY);
    }

    private void StartGameplayUi(int frame)
    {
        if (UiGameplayStarted ||
            frame < 2 ||
            !_probe.TryGetScriptScene(out PixelEngine.Scripting.Scene? scene) ||
            !scene.TryGetFirstComponent(out GameUiDemoController? controller) ||
            controller.MainScreen.Value == 0)
        {
            return;
        }

        controller.HandleUiEvent(new UiEvent(default, default, GameUiDemoController.Action("start_game"), default));
        UiGameplayStarted = controller.MainScreen.Value == 0 && controller.HudScreenHandle.Value != 0;
    }

    private void InjectRouteProbe(int frame)
    {
        // 全路线探针：沿关卡关键坐标扫射以验证通道开辟与终点推进
        int keyCount = 0;
        int buttonCount = 0;
        Point2F target = RouteColumnTargetWorld;
        float wheelY = 0f;
        bool recoveryLevitation = false;

        if (frame == 2)
        {
            _keys[keyCount++] = Key.Digit1;
        }
        else if (frame is >= 8 and <= 23)
        {
            target = new Point2F(118f + ((frame - 8) * 2f), 262f);
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame == 24)
        {
            _keys[keyCount++] = Key.Digit2;
        }
        else if (frame is >= 28 and <= 83)
        {
            int sweep = frame - 28;
            float sweepX = 136f + (sweep % 8 * 6f);
            float sweepY = 232f + (sweep / 8 * 6f);
            target = new Point2F(sweepX, sweepY);
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame is >= 84 and <= 147)
        {
            int sweep = frame - 84;
            float sweepX = 250f + (sweep % 8 * 12f);
            float sweepY = 276f + (sweep / 8 * 4f);
            target = new Point2F(sweepX, sweepY);
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame is >= 148 and <= 211)
        {
            int sweep = frame - 148;
            float sweepX = 410f + (sweep % 8 * 10f);
            float sweepY = 224f + (sweep / 8 * 6f);
            target = new Point2F(sweepX, sweepY);
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame is >= 212 and <= 267)
        {
            int sweep = frame - 212;
            float sweepX = 486f + (sweep % 7 * 7f);
            float sweepY = 198f + (sweep / 7 * 8f);
            target = new Point2F(sweepX, sweepY);
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame is >= 268 and <= 331)
        {
            int sweep = frame - 268;
            float sweepX = 336f + (sweep % 8 * 9f);
            float sweepY = 258f + (sweep / 8 * 5f);
            target = new Point2F(sweepX, sweepY);
            _buttons[buttonCount++] = MouseButton.Left;
        }
        else if (frame >= 356)
        {
            _keys[keyCount++] = Key.D;
            if (frame == 356)
            {
                _keys[keyCount++] = Key.Digit2;
            }

            ResolveRoutePlayer();
            bool clearingRoute = _routePlayer is not null;
            target = clearingRoute
                ? new Point2F(_routePlayer!.CenterX + 96f, _routePlayer.CenterY)
                : new Point2F(
                    MathF.Min(604f, 298f + ((frame - 356) * 0.28f)),
                    260f + (frame / 24 % 3 * 12f));
            recoveryLevitation = clearingRoute && (frame - 356) % 54 is >= 0 and <= 8;

            if (clearingRoute ? frame % 18 is >= 0 and <= 3 : frame % 9 is 0 or 1)
            {
                _buttons[buttonCount++] = MouseButton.Left;
            }
        }

        if (frame is >= 24 and <= 179)
        {
            _keys[keyCount++] = Key.D;
        }

        bool earlyLevitation = frame is >= 24 and <= 179 && (frame - 24) % 64 is >= 8 and <= 28;
        bool lateLevitation =
            (frame >= 356 && (frame - 356) % 72 is >= 0 and <= 26) ||
            recoveryLevitation;
        if (earlyLevitation || lateLevitation)
        {
            _keys[keyCount++] = Key.Space;
        }

        if (frame is 44 or 92 or 140)
        {
            target = new Point2F(190f + (frame - 44), 272f);
            _buttons[buttonCount++] = MouseButton.Left;
        }

        if (frame is 180 or 236)
        {
            _keys[keyCount++] = Key.Digit3;
            target = frame == 180 ? new Point2F(392f, 176f) : new Point2F(506f, 234f);
            _buttons[buttonCount++] = MouseButton.Left;
        }

        if (frame is 188 or 244)
        {
            _keys[keyCount++] = Key.Digit2;
        }

        Point2F screen = _probe.Camera.WorldToScreen(target.X, target.Y);
        _probe.Input.Update(
            _keys.AsSpan(0, keyCount),
            _buttons.AsSpan(0, buttonCount),
            screen.X,
            screen.Y,
            wheelY);
    }

    private void ResolveRoutePlayer()
    {
        if (_routePlayer is not null ||
            !_probe.TryGetScriptScene(out PixelEngine.Scripting.Scene? scene) ||
            !scene.TryGetFirstComponent(out PlayerController? player))
        {
            return;
        }

        _routePlayer = player;
    }
}

/// <summary>
/// Demo 脚本化窗口短跑的运行态探针，累计会被后续帧覆盖的瞬时结果。
/// </summary>
internal sealed class DemoWindowScriptedProbe(
    EngineProbeApi probe) : IEnginePhaseDriver
{
    private readonly EngineProbeApi _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private PlayerController? _player;

    /// <summary>
    /// 短跑期间观测到的最大刚体销毁数量。
    /// </summary>
    public int MaxDestroyedBodies { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大刚体创建数量。
    /// </summary>
    public int MaxCreatedBodies { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大活跃刚体数量。
    /// </summary>
    public int MaxActiveBodies { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大自由粒子数量。
    /// </summary>
    public int MaxParticles { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大点光数量。
    /// </summary>
    public int MaxLights { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大纯视觉爆炸烟尘 burst 数量。
    /// </summary>
    public int MaxTransientBursts { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大音频抽取事件数量。
    /// </summary>
    public long MaxAudioDrained { get; private set; }

    /// <summary>
    /// 短跑期间观测到的最大音频播放数量。
    /// </summary>
    public long MaxAudioPlayed { get; private set; }

    /// <summary>
    /// 已采样的相机窗口帧数。
    /// </summary>
    public int CameraSamples { get; private set; }

    /// <summary>
    /// 玩家中心 X 的最小观测值。
    /// </summary>
    public float PlayerMinX { get; private set; }

    /// <summary>
    /// 玩家中心 X 的最大观测值。
    /// </summary>
    public float PlayerMaxX { get; private set; }

    /// <summary>
    /// 玩家中心 Y 的最小观测值。
    /// </summary>
    public float PlayerMinY { get; private set; }

    /// <summary>
    /// 玩家中心 Y 的最大观测值。
    /// </summary>
    public float PlayerMaxY { get; private set; }

    /// <summary>
    /// 玩家处于地面的采样帧数。
    /// </summary>
    public int PlayerGroundedSamples { get; private set; }

    /// <summary>
    /// 玩家处于空中的采样帧数。
    /// </summary>
    public int PlayerAirborneSamples { get; private set; }

    /// <summary>
    /// 玩家空中阶段中心 X 的最小观测值。
    /// </summary>
    public float PlayerAirMinX { get; private set; }

    /// <summary>
    /// 玩家空中阶段中心 X 的最大观测值。
    /// </summary>
    public float PlayerAirMaxX { get; private set; }

    /// <summary>
    /// 脚本相机中心 X 的最小观测值。
    /// </summary>
    public float CameraMinX { get; private set; }

    /// <summary>
    /// 脚本相机中心 X 的最大观测值。
    /// </summary>
    public float CameraMaxX { get; private set; }

    /// <summary>
    /// Rendering 相机左上角 X 的最小观测值。
    /// </summary>
    public float RenderOriginMinX { get; private set; }

    /// <summary>
    /// Rendering 相机左上角 X 的最大观测值。
    /// </summary>
    public float RenderOriginMaxX { get; private set; }

    /// <summary>
    /// 所有采样中脚本相机快照是否与 Rendering 相机状态一致。
    /// </summary>
    public bool RenderCameraSynced { get; private set; } = true;

    /// <summary>
    /// 玩家与相机在窗口短跑中是否均发生水平移动。
    /// </summary>
    public bool CameraFollowed => CameraSamples > 1 &&
        PlayerMaxX - PlayerMinX > 1f &&
        CameraMaxX - CameraMinX > 1f;

    /// <summary>
    /// 玩家在真实窗口短跑中是否离开过地面。
    /// </summary>
    public bool PlayerLeftGround => PlayerAirborneSamples > 0;

    /// <summary>
    /// 玩家真实窗口短跑中的垂直活动范围。
    /// </summary>
    public float PlayerYRange => CameraSamples == 0 ? 0f : PlayerMaxY - PlayerMinY;

    /// <summary>
    /// 玩家真实窗口短跑中空中阶段的水平活动范围。
    /// </summary>
    public float PlayerAirXRange => PlayerAirborneSamples == 0 ? 0f : PlayerAirMaxX - PlayerAirMinX;

    /// <summary>
    /// 玩家在空中时是否仍保有可观测的水平控制。
    /// </summary>
    public bool PlayerAirControl => PlayerAirborneSamples > 1 && PlayerAirXRange > 4f;

    /// <summary>
    /// 注册物理与音频之后的采样相位。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.PhysicsSync, Capture);
        phases.Register(EnginePhase.BuildRenderBuffer, CaptureAudio);
        phases.Register(EnginePhase.BuildRenderBuffer, CaptureCamera);
    }

    private void Capture(EngineTickContext context)
    {
        _ = context;
        PhysicsSystemStats physics = _probe.PhysicsStats;
        MaxDestroyedBodies = Math.Max(MaxDestroyedBodies, physics.LastDestructionResult.DestroyedBodies);
        MaxCreatedBodies = Math.Max(MaxCreatedBodies, physics.LastDestructionResult.CreatedBodies);
        MaxActiveBodies = Math.Max(MaxActiveBodies, physics.ActiveBodyCount);
        MaxParticles = Math.Max(MaxParticles, _probe.ActiveParticles);
        MaxLights = Math.Max(MaxLights, _probe.PointLights.Length);
        MaxTransientBursts = Math.Max(MaxTransientBursts, TransientParticleBurst.ActiveCount(_probe.ScriptScene));
    }

    private void CaptureAudio(EngineTickContext context)
    {
        MaxAudioDrained = Math.Max(MaxAudioDrained, context.Context.Counters.AudioDrained);
        MaxAudioPlayed = Math.Max(MaxAudioPlayed, context.Context.Counters.AudioPlayed);
    }

    private void CaptureCamera(EngineTickContext context)
    {
        _ = context;
        PlayerController? player = ResolvePlayer();
        if (player is null)
        {
            return;
        }

        CharacterState state = player.State;
        if (state.Width <= 0f || state.Height <= 0f)
        {
            return;
        }

        CameraState renderCamera = _probe.CameraSynchronizer.Current;
        CameraSnapshot scriptSnapshot = _probe.Camera.Snapshot();
        RenderCameraSynced &= Math.Abs(renderCamera.OriginWorldX - scriptSnapshot.OriginWorldX) <= 0.05f &&
            Math.Abs(renderCamera.OriginWorldY - scriptSnapshot.OriginWorldY) <= 0.05f &&
            Math.Abs(renderCamera.CellsPerPixel - scriptSnapshot.CellsPerPixel) <= 0.001f &&
            renderCamera.ViewportWidth == scriptSnapshot.ViewportWidth &&
            renderCamera.ViewportHeight == scriptSnapshot.ViewportHeight;

        float playerX = player.CenterX;
        float playerY = player.CenterY;
        if (CameraSamples == 0)
        {
            PlayerMinX = playerX;
            PlayerMaxX = playerX;
            PlayerMinY = playerY;
            PlayerMaxY = playerY;
            CameraMinX = _probe.Camera.CenterX;
            CameraMaxX = _probe.Camera.CenterX;
            RenderOriginMinX = renderCamera.OriginWorldX;
            RenderOriginMaxX = renderCamera.OriginWorldX;
        }
        else
        {
            PlayerMinX = Math.Min(PlayerMinX, playerX);
            PlayerMaxX = Math.Max(PlayerMaxX, playerX);
            PlayerMinY = Math.Min(PlayerMinY, playerY);
            PlayerMaxY = Math.Max(PlayerMaxY, playerY);
            CameraMinX = Math.Min(CameraMinX, _probe.Camera.CenterX);
            CameraMaxX = Math.Max(CameraMaxX, _probe.Camera.CenterX);
            RenderOriginMinX = Math.Min(RenderOriginMinX, renderCamera.OriginWorldX);
            RenderOriginMaxX = Math.Max(RenderOriginMaxX, renderCamera.OriginWorldX);
        }

        if (state.OnGround)
        {
            PlayerGroundedSamples++;
        }
        else
        {
            if (PlayerAirborneSamples == 0)
            {
                PlayerAirMinX = playerX;
                PlayerAirMaxX = playerX;
            }
            else
            {
                PlayerAirMinX = Math.Min(PlayerAirMinX, playerX);
                PlayerAirMaxX = Math.Max(PlayerAirMaxX, playerX);
            }

            PlayerAirborneSamples++;
        }

        CameraSamples++;
    }

    private PlayerController? ResolvePlayer()
    {
        if (_player is not null)
        {
            return _player;
        }

        if (!_probe.ScriptScene.TryGetFirstComponent(out PlayerController? player))
        {
            return null;
        }

        _player = player;
        return player;
    }
}
