using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 玩家 kinematic AABB 控制器，消费脚本公开输入与角色控制器 API。
/// </summary>
public sealed class PlayerController : Behaviour
{
    private CharacterHandle _body;
    private bool _hasBody;
    private float _velocityX;
    private float _velocityY;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private float _footstepTimer;
    private float _rigidImpactCooldown;
    private float _levitationRechargeDelay;
    private Transform? _transform;
    private PlayerHealth? _health;
    private IDisposable? _postPhysicsSubscription;

    /// <summary>
    /// 出生点 X 坐标。
    /// </summary>
    public float SpawnX { get; set; } = 32f;

    /// <summary>
    /// 出生点 Y 坐标。
    /// </summary>
    public float SpawnY { get; set; } = 32f;

    /// <summary>
    /// AABB 宽度。
    /// </summary>
    public float Width { get; set; } = 6f;

    /// <summary>
    /// AABB 高度。
    /// </summary>
    public float Height { get; set; } = 12f;

    /// <summary>
    /// 地面最大水平速度，单位像素/秒。
    /// </summary>
    public float MaxRunSpeed { get; set; } = 120f;

    /// <summary>
    /// 地面水平加速度，单位像素/秒平方。
    /// </summary>
    public float GroundAcceleration { get; set; } = 1_650f;

    /// <summary>
    /// 空中水平加速度，单位像素/秒平方。
    /// </summary>
    public float AirAcceleration { get; set; } = 1_050f;

    /// <summary>
    /// 地面无输入时水平摩擦减速度，单位像素/秒平方。
    /// </summary>
    public float GroundFriction { get; set; } = 2_800f;

    /// <summary>
    /// 空中无输入时水平阻尼减速度，单位像素/秒平方。
    /// </summary>
    public float AirFriction { get; set; } = 180f;

    /// <summary>
    /// 重力加速度，单位像素/秒平方；Y 正方向向下。
    /// </summary>
    public float Gravity { get; set; } = 860f;

    /// <summary>
    /// 最大下落速度，单位像素/秒。
    /// </summary>
    public float MaxFallSpeed { get; set; } = 300f;

    /// <summary>
    /// 跳跃初速度，单位像素/秒。
    /// </summary>
    public float JumpSpeed { get; set; } = 245f;

    /// <summary>
    /// 离开地面后仍允许跳跃的宽限时间，单位秒。
    /// </summary>
    public float CoyoteTime { get; set; } = 0.08f;

    /// <summary>
    /// 跳跃提前输入缓冲时间，单位秒。
    /// </summary>
    public float JumpBufferTime { get; set; } = 0.10f;

    /// <summary>
    /// 满额时可持续悬浮的时间，单位秒。
    /// </summary>
    public float LevitationCapacitySeconds { get; set; } = 2.5f;

    /// <summary>
    /// 悬浮向上的加速度，单位像素/秒平方。
    /// </summary>
    public float LevitationAcceleration { get; set; } = 1_450f;

    /// <summary>
    /// 悬浮最大上升速度，单位像素/秒。
    /// </summary>
    public float MaxLevitationRiseSpeed { get; set; } = 190f;

    /// <summary>
    /// 未悬浮时每秒恢复的悬浮时间，单位秒/秒。
    /// </summary>
    public float LevitationRechargePerSecond { get; set; } = 1.75f;

    /// <summary>
    /// 离地停止悬浮后开始恢复前的等待时间，单位秒。
    /// </summary>
    public float LevitationRechargeDelaySeconds { get; set; } = 0.20f;

    /// <summary>
    /// 贴墙滑落最大速度，单位像素/秒。
    /// </summary>
    public float WallSlideSpeed { get; set; } = 70f;

    /// <summary>
    /// 蹬墙水平初速度，单位像素/秒。
    /// </summary>
    public float WallJumpXSpeed { get; set; } = 150f;

    /// <summary>
    /// 玩家奔跑脚步音效间隔，单位秒。
    /// </summary>
    public float FootstepIntervalSeconds { get; set; } = 0.18f;

    /// <summary>
    /// 动态刚体碎块主动压入玩家 AABB 时的撞击伤害。
    /// </summary>
    public float RigidImpactDamage { get; set; } = 35f;

    /// <summary>
    /// 动态刚体碎块撞击伤害冷却时间，单位秒。
    /// </summary>
    public float RigidImpactCooldownSeconds { get; set; } = 0.25f;

    /// <summary>
    /// 被动态碎块压入后寻找最近安全 AABB 位置的最大距离，单位 cell。
    /// </summary>
    public int RigidOverlapResolveDistance { get; set; } = 18;

    /// <summary>
    /// 是否启用玩家逃出关卡边界后的自动重生。
    /// </summary>
    public bool EnableEscapeRespawn { get; set; }

    /// <summary>
    /// 自动重生边界左侧 X 坐标。
    /// </summary>
    public float EscapeMinX { get; set; } = -256f;

    /// <summary>
    /// 自动重生边界右侧 X 坐标。
    /// </summary>
    public float EscapeMaxX { get; set; } = 896f;

    /// <summary>
    /// 自动重生边界上侧 Y 坐标。
    /// </summary>
    public float EscapeMinY { get; set; } = -256f;

    /// <summary>
    /// 自动重生边界下侧 Y 坐标。
    /// </summary>
    public float EscapeMaxY { get; set; } = 512f;

    /// <summary>
    /// 最近一次角色状态。
    /// </summary>
    public CharacterState State { get; private set; }

    /// <summary>
    /// 当前剩余悬浮时间，单位秒。
    /// </summary>
    public float LevitationRemainingSeconds { get; private set; }

    /// <summary>
    /// 当前悬浮燃料比例，范围 0..1。
    /// </summary>
    public float LevitationFraction
    {
        get
        {
            float capacity = NonNegativeFinite(LevitationCapacitySeconds);
            return capacity <= 0f
                ? 0f
                : Math.Clamp(LevitationRemainingSeconds / capacity, 0f, 1f);
        }
    }

    /// <summary>
    /// 本帧是否正在消耗燃料悬浮。
    /// </summary>
    public bool IsLevitating { get; private set; }

    /// <summary>
    /// 角色中心 X 坐标。
    /// </summary>
    public float CenterX => State.X + (State.Width * 0.5f);

    /// <summary>
    /// 角色中心 Y 坐标。
    /// </summary>
    public float CenterY => State.Y + (State.Height * 0.5f);

    /// <summary>
    /// 重生到出生点并清空速度。
    /// </summary>
    public void Respawn()
    {
        EnsureBody();
        _velocityX = 0f;
        _velocityY = 0f;
        _coyoteTimer = 0f;
        _jumpBufferTimer = 0f;
        _footstepTimer = 0f;
        _rigidImpactCooldown = 0f;
        LevitationRemainingSeconds = NonNegativeFinite(LevitationCapacitySeconds);
        _levitationRechargeDelay = 0f;
        IsLevitating = false;
        State = Context.Character.SetPosition(_body, SpawnX, SpawnY);
        SyncTransform();
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        EnsureBody();
        _postPhysicsSubscription ??= Context.PhysicsEvents.SubscribePostStep(HandlePostPhysicsStep);
        Respawn();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _postPhysicsSubscription?.Dispose();
        _postPhysicsSubscription = null;
        // CharacterHandle 属于当前 Play session；Hosting 重开 world 后由 OnStart 创建新代理。
        _body = default;
        _hasBody = false;
        State = default;
        LevitationRemainingSeconds = 0f;
        _levitationRechargeDelay = 0f;
        IsLevitating = false;
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        EnsureBody();
        ResolveHealth();
        _rigidImpactCooldown = MathF.Max(0f, _rigidImpactCooldown - dt);
        CharacterState previousState = State;
        State = Context.Character.GetState(_body);
        // 越界自动重生（可玩关卡默认关闭）
        if (TryRespawnWhenEscaped())
        {
            return;
        }

        ResolveVelocityAfterCollision(State);
        EmitMovementAudio(previousState, State, dt);
        float axis = Context.Input.Axis(Axis.Horizontal);
        bool jumpPressed = Context.Input.WasPressed(Key.Space) || Context.Input.WasPressed(Key.W) || Context.Input.WasPressed(Key.Up);
        bool levitationHeld = Context.Input.IsDown(Key.Space) || Context.Input.IsDown(Key.W) || Context.Input.IsDown(Key.Up);

        if (jumpPressed)
        {
            _jumpBufferTimer = JumpBufferTime;
        }

        // Coyote time 与 jump buffer：平台动作手感核心
        _jumpBufferTimer = MathF.Max(0f, _jumpBufferTimer - dt);
        _coyoteTimer = State.OnGround ? CoyoteTime : MathF.Max(0f, _coyoteTimer - dt);

        ApplyHorizontal(axis, dt);
        ApplyVertical(dt, levitationHeld);
        TryConsumeJump();

        // 单帧位移钳制，避免高速穿模
        float dx = ClampDisplacement(_velocityX * dt);
        float dy = ClampDisplacement(_velocityY * dt);
        CharacterState moved = Context.Character.MoveNow(_body, dx, dy);
        ResolveVelocityAfterCollision(moved);
        State = moved;
        ResolveRigidOwnedOverlap();
        _ = TryRespawnWhenEscaped();
        SyncTransform();
    }

    // 物理步后处理：刚体压顶伤害与 RigidOwned 重叠解脱
    private void HandlePostPhysicsStep()
    {
        if (!_hasBody)
        {
            return;
        }

        ResolveHealth();
        State = Context.Character.GetState(_body);
        if (TryRespawnWhenEscaped())
        {
            return;
        }

        if (Context.PhysicsEvents.LastCharacterImpactCount > 0)
        {
            ApplyRigidImpactDamage();
        }

        ResolveRigidOwnedOverlap();
        SyncTransform();
    }

    private void EnsureBody()
    {
        if (_hasBody)
        {
            return;
        }

        _body = Context.Character.Create(SpawnX, SpawnY, Width, Height);
        State = Context.Character.GetState(_body);
        _hasBody = true;
        SyncTransform();
    }

    private void SyncTransform()
    {
        if (_transform is null)
        {
            if (!Entity.TryGetComponent(out Transform transform))
            {
                return;
            }

            _transform = transform;
        }

        _transform.SetPosition(CenterX, CenterY);
    }

    private void ResolveHealth()
    {
        if (_health is not null)
        {
            return;
        }

        if (Entity.TryGetComponent(out PlayerHealth health))
        {
            _health = health;
        }
    }

    private void ApplyHorizontal(float axis, float dt)
    {
        if (MathF.Abs(axis) > 0.01f)
        {
            float acceleration = State.OnGround ? GroundAcceleration : AirAcceleration;
            _velocityX = MoveTowards(_velocityX, axis * MaxRunSpeed, acceleration * dt);
            return;
        }

        if (State.OnGround)
        {
            _velocityX = MoveTowards(_velocityX, 0f, GroundFriction * dt);
            return;
        }

        _velocityX = MoveTowards(_velocityX, 0f, AirFriction * dt);
    }

    private void ApplyVertical(float dt, bool levitationHeld)
    {
        _velocityY = MathF.Min(MaxFallSpeed, _velocityY + (Gravity * dt));
        float capacity = NonNegativeFinite(LevitationCapacitySeconds);
        LevitationRemainingSeconds = Math.Clamp(LevitationRemainingSeconds, 0f, capacity);
        IsLevitating = levitationHeld && !State.OnGround && LevitationRemainingSeconds > 0f;
        if (IsLevitating)
        {
            float consumed = Math.Min(LevitationRemainingSeconds, dt);
            LevitationRemainingSeconds -= consumed;
            _levitationRechargeDelay = NonNegativeFinite(LevitationRechargeDelaySeconds);
            float acceleration = NonNegativeFinite(LevitationAcceleration);
            float maxRiseSpeed = NonNegativeFinite(MaxLevitationRiseSpeed);
            float acceleratedVelocity = MathF.Max(-maxRiseSpeed, _velocityY - (acceleration * consumed));
            _velocityY = MathF.Min(_velocityY, acceleratedVelocity);
            IsLevitating = LevitationRemainingSeconds > 0f;
        }
        else
        {
            _levitationRechargeDelay = State.OnGround
                ? 0f
                : MathF.Max(0f, _levitationRechargeDelay - dt);
            if (_levitationRechargeDelay <= 0f)
            {
                float recharge = NonNegativeFinite(LevitationRechargePerSecond) * dt;
                LevitationRemainingSeconds = MathF.Min(capacity, LevitationRemainingSeconds + recharge);
            }
        }

        if (!State.OnGround && State.OnWall && _velocityY > WallSlideSpeed)
        {
            _velocityY = WallSlideSpeed;
        }
    }

    private void TryConsumeJump()
    {
        if (_jumpBufferTimer <= 0f)
        {
            return;
        }

        if (_coyoteTimer > 0f)
        {
            _velocityY = -JumpSpeed;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            Context.Audio.PlayAt("player_jump.wav", CenterX, CenterY);
            return;
        }

        if (State.OnWall && !State.OnGround)
        {
            float direction = State.OnWallLeft ? 1f : -1f;
            _velocityX = direction * WallJumpXSpeed;
            _velocityY = -JumpSpeed;
            _jumpBufferTimer = 0f;
            Context.Audio.PlayAt("player_jump.wav", CenterX, CenterY);
        }
    }

    private void ResolveVelocityAfterCollision(in CharacterState moved)
    {
        if ((moved.OnWallLeft && _velocityX < 0f) || (moved.OnWallRight && _velocityX > 0f))
        {
            _velocityX = 0f;
        }

        if (moved.OnCeiling && _velocityY < 0f)
        {
            _velocityY = 0f;
        }

        if (moved.OnGround && _velocityY > 0f)
        {
            _velocityY = 0f;
        }
    }

    private void ResolveRigidOwnedOverlap()
    {
        if (!TryGetRigidOwnedOverlap(State.X, State.Y, out RigidOverlap overlap))
        {
            return;
        }

        if (TryFindSafePosition(State.X, State.Y, in overlap, out float safeX, out float safeY))
        {
            State = Context.Character.SetPosition(_body, safeX, safeY);
            _velocityX = 0f;
            if (overlap.CenterY < CenterY && _velocityY < 0f)
            {
                _velocityY = 0f;
            }
            else if (overlap.CenterY >= CenterY && _velocityY > 0f)
            {
                _velocityY = 0f;
            }
        }

        ApplyRigidImpactDamage();
    }

    private bool TryFindSafePosition(float currentX, float currentY, in RigidOverlap overlap, out float safeX, out float safeY)
    {
        int maxDistance = Math.Clamp(RigidOverlapResolveDistance, 1, 64);
        int verticalAway = overlap.CenterY < CenterY ? 1 : -1;
        int horizontalAway = overlap.CenterX < CenterX ? 1 : -1;
        for (int distance = 1; distance <= maxDistance; distance++)
        {
            ReadOnlySpan<(int X, int Y)> candidates =
            [
                (0, verticalAway * distance),
                (horizontalAway * distance, 0),
                (0, -verticalAway * distance),
                (-horizontalAway * distance, 0),
                (horizontalAway * distance, verticalAway * distance),
                (-horizontalAway * distance, verticalAway * distance),
            ];
            for (int i = 0; i < candidates.Length; i++)
            {
                float candidateX = currentX + candidates[i].X;
                float candidateY = currentY + candidates[i].Y;
                if (!OverlapsSolid(candidateX, candidateY))
                {
                    safeX = candidateX;
                    safeY = candidateY;
                    return true;
                }
            }
        }

        safeX = currentX;
        safeY = currentY;
        return false;
    }

    private bool TryRespawnWhenEscaped()
    {
        if (!EnableEscapeRespawn)
        {
            return false;
        }

        if (State.X + State.Width < EscapeMinX ||
            State.X > EscapeMaxX ||
            State.Y + State.Height < EscapeMinY ||
            State.Y > EscapeMaxY)
        {
            Respawn();
            return true;
        }

        return false;
    }

    private void ApplyRigidImpactDamage()
    {
        if (_rigidImpactCooldown > 0f || RigidImpactDamage <= 0f)
        {
            return;
        }

        ResolveHealth();
        _health?.ApplyExternalDamage(RigidImpactDamage);
        _rigidImpactCooldown = MathF.Max(0.01f, RigidImpactCooldownSeconds);
    }

    private bool TryGetRigidOwnedOverlap(float x, float y, out RigidOverlap overlap)
    {
        int minX = (int)MathF.Floor(x + 0.05f);
        int minY = (int)MathF.Floor(y + 0.05f);
        int maxX = (int)MathF.Ceiling(x + Width - 0.05f) - 1;
        int maxY = (int)MathF.Ceiling(y + Height - 0.05f) - 1;
        int count = 0;
        int sumX = 0;
        int sumY = 0;
        int rigidMinX = int.MaxValue;
        int rigidMinY = int.MaxValue;
        int rigidMaxX = int.MinValue;
        int rigidMaxY = int.MinValue;
        for (int cy = minY; cy <= maxY; cy++)
        {
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (!TryIsRigidOwned(cx, cy))
                {
                    continue;
                }

                count++;
                sumX += cx;
                sumY += cy;
                rigidMinX = Math.Min(rigidMinX, cx);
                rigidMinY = Math.Min(rigidMinY, cy);
                rigidMaxX = Math.Max(rigidMaxX, cx);
                rigidMaxY = Math.Max(rigidMaxY, cy);
            }
        }

        if (count == 0)
        {
            overlap = default;
            return false;
        }

        overlap = new RigidOverlap(
            sumX / (float)count,
            sumY / (float)count,
            rigidMinX,
            rigidMinY,
            rigidMaxX,
            rigidMaxY);
        return true;
    }

    private bool OverlapsSolid(float x, float y)
    {
        int minX = (int)MathF.Floor(x + 0.05f);
        int minY = (int)MathF.Floor(y + 0.05f);
        int maxX = (int)MathF.Ceiling(x + Width - 0.05f) - 1;
        int maxY = (int)MathF.Ceiling(y + Height - 0.05f) - 1;
        for (int cy = minY; cy <= maxY; cy++)
        {
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (IsCharacterBlockingCell(cx, cy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsCharacterBlockingCell(int x, int y)
    {
        if (TryIsRigidOwned(x, y))
        {
            return true;
        }

        MaterialId material = GetMaterialOrInvalid(x, y);
        if (!material.IsValid || material.Value == 0)
        {
            return false;
        }

        MaterialInfo info = Context.Materials.GetInfo(material);
        return info.BlocksCharacter;
    }

    private bool TryIsRigidOwned(int x, int y)
    {
        try
        {
            return Context.Cells.IsRigidOwned(x, y);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private MaterialId GetMaterialOrInvalid(int x, int y)
    {
        try
        {
            return Context.Cells.GetMaterial(x, y);
        }
        catch (InvalidOperationException)
        {
            return MaterialId.Invalid;
        }
    }

    private void EmitMovementAudio(in CharacterState previous, in CharacterState current, float dt)
    {
        if (!previous.OnGround && current.OnGround && current.AppliedDeltaY >= 0f)
        {
            Context.Audio.PlayAt("player_land.wav", CenterX, CenterY, 0.75f);
        }

        _footstepTimer = MathF.Max(0f, _footstepTimer - dt);
        if (!current.OnGround || MathF.Abs(_velocityX) < MaxRunSpeed * 0.35f)
        {
            return;
        }

        if (_footstepTimer > 0f)
        {
            return;
        }

        Context.Audio.PlayAt("footstep_stone.wav", CenterX, CenterY, 0.35f);
        _footstepTimer = MathF.Max(0.05f, FootstepIntervalSeconds);
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        return MathF.Abs(target - current) <= maxDelta
            ? target
            : current + (MathF.Sign(target - current) * maxDelta);
    }

    private static float NonNegativeFinite(float value)
    {
        return float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    private static float ClampDisplacement(float value)
    {
        return Math.Clamp(value, -31.5f, 31.5f);
    }

    private readonly record struct RigidOverlap(
        float CenterX,
        float CenterY,
        int MinX,
        int MinY,
        int MaxX,
        int MaxY);
}
