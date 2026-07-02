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
    private Transform? _transform;

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
    /// 最近一次角色状态。
    /// </summary>
    public CharacterState State { get; private set; }

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
        State = Context.Character.SetPosition(_body, SpawnX, SpawnY);
        SyncTransform();
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        EnsureBody();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        EnsureBody();
        CharacterState previousState = State;
        State = Context.Character.GetState(_body);
        ResolveVelocityAfterCollision(State);
        EmitMovementAudio(previousState, State, dt);
        float axis = Context.Input.Axis(Axis.Horizontal);
        bool jumpPressed = Context.Input.WasPressed(Key.Space) || Context.Input.WasPressed(Key.W) || Context.Input.WasPressed(Key.Up);

        if (jumpPressed)
        {
            _jumpBufferTimer = JumpBufferTime;
        }

        _jumpBufferTimer = MathF.Max(0f, _jumpBufferTimer - dt);
        _coyoteTimer = State.OnGround ? CoyoteTime : MathF.Max(0f, _coyoteTimer - dt);

        ApplyHorizontal(axis, dt);
        ApplyVertical(dt);
        TryConsumeJump();

        float dx = ClampDisplacement(_velocityX * dt);
        float dy = ClampDisplacement(_velocityY * dt);
        CharacterState moved = Context.Character.MoveNow(_body, dx, dy);
        ResolveVelocityAfterCollision(moved);
        State = moved;
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

    private void ApplyVertical(float dt)
    {
        _velocityY = MathF.Min(MaxFallSpeed, _velocityY + (Gravity * dt));
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

    private static float ClampDisplacement(float value)
    {
        return Math.Clamp(value, -31.5f, 31.5f);
    }
}
