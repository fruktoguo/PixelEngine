using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Wand cast 的运行时投射物。它是一个轻量脚本实体，使用公开 Solid/World/Cells/Particles API，
/// 不把玩法逻辑下沉到引擎内部；trigger payload 在生成阶段以单向链挂到父投射物。
/// </summary>
public sealed class WandProjectile : Behaviour
{
    private SpellProjectilePlan _plan;
    private WandProjectile? _firstPayload;
    private WandProjectile? _nextPayload;
    private WandController? _owner;
    private Transform? _transform;
    private MaterialId _material = MaterialId.Invalid;
    private MaterialId _visualMaterial = MaterialId.Invalid;
    private float _velocityX;
    private float _velocityY;
    private float _elapsed;
    private int _bouncesRemaining;
    private bool _activateOnStart;
    private bool _payloadTriggered;
    private bool _destroyRequested;
    private bool _runtimeStarted;

    /// <summary>当前是否已被父节点或 spawn root 激活。</summary>
    public bool IsActive { get; private set; }

    /// <summary>当前是否已归还固定 projectile pool。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>当前世界 X 坐标。</summary>
    public float X { get; private set; }

    /// <summary>当前世界 Y 坐标。</summary>
    public float Y { get; private set; }

    /// <summary>关联的 spell index。</summary>
    public int SpellIndex => _plan.SpellIndex;

    /// <summary>是否为 trigger payload 而非 root projectile。</summary>
    public bool IsPayload { get; private set; }

    /// <summary>当前直接挂接的 trigger payload 数量。</summary>
    public int AttachedPayloadCount { get; private set; }

    /// <summary>初始化 plan；由 Wand spawn system 调用。</summary>
    internal void Initialize(
        in SpellProjectilePlan plan,
        float x,
        float y,
        float directionX,
        float directionY,
        WandController owner,
        bool isPayload)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!IsAvailable)
        {
            throw new InvalidOperationException("只能初始化已归还 projectile pool 的槽位。");
        }

        _plan = plan;
        _owner = owner;
        _firstPayload = null;
        _nextPayload = null;
        IsPayload = isPayload;
        AttachedPayloadCount = 0;
        X = x;
        Y = y;
        _velocityX = directionX;
        _velocityY = directionY;
        _elapsed = 0f;
        _bouncesRemaining = Math.Clamp(plan.Bounces, 0, 32);
        IsActive = false;
        _activateOnStart = !isPayload;
        _payloadTriggered = false;
        _destroyRequested = false;
        IsAvailable = false;
        _material = MaterialId.Invalid;
        _visualMaterial = MaterialId.Invalid;
        Enabled = !isPayload || !_runtimeStarted;
    }

    /// <summary>把预热实体登记为固定池槽位。</summary>
    internal void InitializePoolSlot(WandController owner, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(transform);
        _owner = owner;
        _transform = transform;
        IsAvailable = true;
        IsActive = false;
        _destroyRequested = false;
        Enabled = true;
    }

    /// <summary>在新的 play session 开始时恢复为空闲槽位。</summary>
    internal void ResetForSession(WandController owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _plan = default;
        _owner = owner;
        _firstPayload = null;
        _nextPayload = null;
        _material = MaterialId.Invalid;
        _visualMaterial = MaterialId.Invalid;
        IsActive = false;
        _activateOnStart = false;
        _payloadTriggered = false;
        _destroyRequested = false;
        IsAvailable = true;
        IsPayload = false;
        AttachedPayloadCount = 0;
        Enabled = true;
    }

    /// <summary>把一个预先创建的 payload 挂到当前投射物末尾。</summary>
    internal void AttachPayload(WandProjectile payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (_firstPayload is null)
        {
            _firstPayload = payload;
            AttachedPayloadCount++;
            return;
        }

        WandProjectile tail = _firstPayload;
        while (tail._nextPayload is not null)
        {
            tail = tail._nextPayload;
        }

        tail._nextPayload = payload;
        AttachedPayloadCount++;
    }

    /// <summary>复用且已经完成 OnStart 的 root 可在 spawn system 相位立即激活。</summary>
    internal void ActivatePreparedRoot()
    {
        if (!_runtimeStarted || !_activateOnStart)
        {
            return;
        }

        _activateOnStart = false;
        Activate(X, Y, _velocityX, _velocityY);
    }

    /// <summary>激活 root 或 trigger payload，并按 plan 的角度偏移计算速度。</summary>
    internal void Activate(float x, float y, float directionX, float directionY)
    {
        if (IsActive || _destroyRequested || IsAvailable)
        {
            return;
        }

        float length = MathF.Sqrt((directionX * directionX) + (directionY * directionY));
        if (!float.IsFinite(length) || length <= 0.001f)
        {
            directionX = 1f;
            directionY = 0f;
            length = 1f;
        }

        directionX /= length;
        directionY /= length;
        float radians = _plan.AngleOffsetDegrees * (MathF.PI / 180f);
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float rotatedX = (directionX * cos) - (directionY * sin);
        float rotatedY = (directionX * sin) + (directionY * cos);
        X = x;
        Y = y;
        _velocityX = rotatedX * _plan.Speed;
        _velocityY = rotatedY * _plan.Speed;
        _elapsed = 0f;
        IsActive = true;
        Enabled = true;
        _owner?.NotifyProjectileActivated(IsPayload);
        ResolveMaterials();
        SyncTransform();
        EmitLaunchFeedback();
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        _runtimeStarted = true;
        ResolveMaterials();
        if (_activateOnStart)
        {
            _activateOnStart = false;
            Activate(X, Y, _velocityX, _velocityY);
        }
        else if (IsActive)
        {
            SyncTransform();
        }
        else
        {
            Enabled = false;
        }
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!IsActive || _destroyRequested || !float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        _elapsed += dt;
        if (_plan.Trigger == WandTriggerKind.Timer &&
            !_payloadTriggered &&
            _elapsed >= _plan.TriggerDelaySeconds)
        {
            TriggerPayloads();
        }

        _velocityY += _plan.Gravity * dt;
        float moveX = _velocityX * dt;
        float moveY = _velocityY * dt;
        float distance = MathF.Sqrt((moveX * moveX) + (moveY * moveY));
        float previousX = X;
        float previousY = Y;
        bool raycastAvailable = true;
        bool hitSolid = false;
        RaycastHit hit = default;
        if (distance > 0.001f)
        {
            int destinationX = (int)MathF.Floor(X + moveX);
            int destinationY = (int)MathF.Floor(Y + moveY);
            if (!Context.Cells.IsResident(destinationX, destinationY))
            {
                DestroyProjectile();
                return;
            }

            raycastAvailable = TryRaycast(moveX / distance, moveY / distance, distance, out hit);
            hitSolid = raycastAvailable && hit.Hit;
        }

        if (hitSolid)
        {
            X = hit.X;
            Y = hit.Y;
            DrawTrail(previousX, previousY);
            ResolveImpact();
            return;
        }

        if (!raycastAvailable)
        {
            DestroyProjectile();
            return;
        }

        X += moveX;
        Y += moveY;

        SyncTransform();
        DrawTrail(previousX, previousY);
        AddProjectileLight();
        if (_elapsed >= _plan.LifetimeSeconds)
        {
            if (_plan.Trigger == WandTriggerKind.Death)
            {
                TriggerPayloads();
            }

            DestroyProjectile();
        }
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _runtimeStarted = false;
        IsActive = false;
        _activateOnStart = false;
        _destroyRequested = true;
        _firstPayload = null;
        _nextPayload = null;
        _owner = null;
        IsAvailable = true;
        Enabled = false;
    }

    private void ResolveImpact()
    {
        ApplyWorldEffect();
        if (_plan.Trigger == WandTriggerKind.Hit && !_payloadTriggered)
        {
            TriggerPayloads();
        }

        if (_plan.Trigger == WandTriggerKind.Death && !_payloadTriggered)
        {
            TriggerPayloads();
        }

        if (_bouncesRemaining > 0)
        {
            _bouncesRemaining--;
            // RaycastHit intentionally exposes no normal; reversing both axes is stable for pixel collisions
            // and avoids an extra world readback in the hot projectile path.
            _velocityX *= -0.72f;
            _velocityY *= -0.72f;
            X += _velocityX * (1f / 120f);
            Y += _velocityY * (1f / 120f);
            EmitBounceFeedback();
            return;
        }

        if (_plan.Trigger == WandTriggerKind.Timer && !_payloadTriggered)
        {
            // Timer projectile 若在计时到期前先撞毁，仍在销毁点释放 payload。
            TriggerPayloads();
        }

        DestroyProjectile();
    }

    private void ApplyWorldEffect()
    {
        int radius = _plan.ExplosionRadius > 0
            ? _plan.ExplosionRadius
            : _plan.Projectile switch
            {
                WandProjectileKind.Orb => 2,
                WandProjectileKind.Grenade => 8,
                WandProjectileKind.Dig => 3,
                WandProjectileKind.Material => Math.Max(1, _plan.MaterialRadius),
                WandProjectileKind.Bolt => 1,
                WandProjectileKind.Teleport => 1,
                WandProjectileKind.None => 1,
                _ => 1,
            };
        float damage = MathF.Max(0f, MathF.Max(_plan.Damage, _plan.TerrainDamage));
        DamageKind damageKind = string.Equals(_plan.Material, "acid", StringComparison.Ordinal)
            ? DamageKind.Corrosion
            : DamageKind.Impact;

        switch (_plan.Projectile)
        {
            case WandProjectileKind.Grenade:
                Context.World.Explode(X, Y, Math.Max(1, radius), MathF.Max(1f, damage));
                break;
            case WandProjectileKind.Material:
                if (_material.IsValid)
                {
                    Context.Cells.Paint(
                        (int)MathF.Round(X),
                        (int)MathF.Round(Y),
                        Math.Max(1, _plan.MaterialRadius),
                        _material);
                }

                if (damage > 0f)
                {
                    Context.World.DamageCircle(X, Y, Math.Max(1, radius), damage, falloff: true, damageKind);
                }

                break;
            case WandProjectileKind.Teleport:
                if (Entity.TryGetComponent(out PlayerController localPlayer))
                {
                    localPlayer.TeleportToCenter(X, Y);
                }
                else if (Context.Scene.TryGetFirstComponent(out PlayerController? player))
                {
                    player.TeleportToCenter(X, Y);
                }

                break;
            case WandProjectileKind.Dig:
            case WandProjectileKind.Bolt:
            case WandProjectileKind.Orb:
            case WandProjectileKind.None:
                if (damage > 0f)
                {
                    Context.World.DamageCircle(X, Y, Math.Max(1, radius), damage, falloff: true, damageKind);
                }

                break;
            default:
                throw new InvalidOperationException($"未知 Wand projectile 类型：{_plan.Projectile}。");
        }

        EmitImpactFeedback();
        if (!string.IsNullOrWhiteSpace(_plan.Material) &&
            string.Equals(_plan.Material, "acid", StringComparison.Ordinal))
        {
            Context.Audio.PlayAt("acid_corrode.wav", X, Y, 0.62f);
        }
        else if (_plan.Projectile == WandProjectileKind.Grenade ||
                 _plan.ExplosionRadius > 0)
        {
            Context.Audio.PlayAt("explosion.wav", X, Y, 0.72f);
        }
        else
        {
            Context.Audio.PlayAt("impact_stone.wav", X, Y, 0.42f);
        }
    }

    private void TriggerPayloads()
    {
        if (_payloadTriggered)
        {
            return;
        }

        _payloadTriggered = true;
        float directionX = _velocityX;
        float directionY = _velocityY;
        float length = MathF.Sqrt((directionX * directionX) + (directionY * directionY));
        if (!float.IsFinite(length) || length <= 0.001f)
        {
            directionX = 1f;
            directionY = 0f;
        }
        else
        {
            directionX /= length;
            directionY /= length;
        }

        for (WandProjectile? payload = _firstPayload; payload is not null; payload = payload._nextPayload)
        {
            payload.Activate(
                X + (directionX * 0.75f),
                Y + (directionY * 0.75f),
                directionX,
                directionY);
        }

        if (_firstPayload is not null)
        {
            Context.Lighting.AddPointLight(X, Y, 26f, 0xFF_70_FF_D8, 0.52f);
        }
    }

    private void DestroyProjectile()
    {
        if (_destroyRequested || IsAvailable)
        {
            return;
        }

        _destroyRequested = true;
        WandController? owner = _owner;
        if (IsActive)
        {
            IsActive = false;
            owner?.NotifyProjectileDestroyed();
        }

        // 未触发的 payload 永远不会自行更新，父节点回收时必须递归归还整棵等待树。
        WandProjectile? payload = _firstPayload;
        while (payload is not null)
        {
            WandProjectile? next = payload._nextPayload;
            if (!payload.IsActive)
            {
                payload.DestroyProjectile();
            }

            payload = next;
        }

        _plan = default;
        _firstPayload = null;
        _nextPayload = null;
        _material = MaterialId.Invalid;
        _visualMaterial = MaterialId.Invalid;
        _activateOnStart = false;
        _payloadTriggered = false;
        _destroyRequested = false;
        IsAvailable = true;
        IsPayload = false;
        AttachedPayloadCount = 0;
        Enabled = false;
        _owner = null;
        owner?.ReturnProjectile(this);
    }

    private bool TryRaycast(float dx, float dy, float distance, out RaycastHit hit)
    {
        try
        {
            _ = Context.Solids.Raycast(X, Y, dx, dy, distance, out hit);
            return true;
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("目标 chunk 未驻留", StringComparison.Ordinal))
        {
            hit = default;
            return false;
        }
    }

    private void ResolveMaterials()
    {
        if (!string.IsNullOrWhiteSpace(_plan.Material) && !_material.IsValid)
        {
            _material = Context.Materials.Resolve(_plan.Material);
        }

        if (!_visualMaterial.IsValid)
        {
            _visualMaterial = _material.IsValid
                ? _material
                : Context.Materials.Resolve(_plan.Projectile switch
                {
                    WandProjectileKind.Grenade => "fire",
                    WandProjectileKind.Orb => "crystal",
                    WandProjectileKind.Teleport => "crystal",
                    WandProjectileKind.Bolt => "fire",
                    WandProjectileKind.Material => "smoke",
                    WandProjectileKind.Dig => "smoke",
                    WandProjectileKind.None => "smoke",
                    _ => "smoke",
                });
        }
    }

    private void SyncTransform()
    {
        _transform?.SetPosition(X, Y);
    }

    private void DrawTrail(float previousX, float previousY)
    {
        Point2F previous = Context.Camera.WorldToScreen(previousX, previousY);
        Point2F current = Context.Camera.WorldToScreen(X, Y);
        uint color = ProjectileColor();
        float width = MathF.Max(1f, Context.Camera.Zoom * (_plan.Projectile == WandProjectileKind.Grenade ? 3f : 1.5f));
        Context.Overlay.Line(previous.X, previous.Y, current.X, current.Y, width, color);
        Context.Overlay.SolidRectangle(
            current.X - (width * 0.5f),
            current.Y - (width * 0.5f),
            width,
            width,
            color);
    }

    private void AddProjectileLight()
    {
        if (_plan.LightRadius > 0f && _plan.LightIntensity > 0f)
        {
            Context.Lighting.AddPointLight(
                X,
                Y,
                _plan.LightRadius,
                ProjectileColor(),
                Math.Clamp(_plan.LightIntensity, 0f, 4f));
        }
    }

    private void EmitLaunchFeedback()
    {
        if (!_visualMaterial.IsValid)
        {
            return;
        }

        Context.Particles.Emit(
            X,
            Y,
            _velocityX,
            _velocityY,
            coneRadians: 0.16f,
            minSpeed: 8f,
            maxSpeed: 22f,
            count: 2,
            material: _visualMaterial,
            lifeTicks: 16);
    }

    private void EmitBounceFeedback()
    {
        TransientParticleBurst.Emit(
            Context,
            X,
            Y,
            count: 4,
            speed: 20f,
            lifetime: 22,
            coreColorBgra: ProjectileColor(),
            trailColorBgra: 0xB0_70_70_90,
            lightIntensity: 0.16f);
    }

    private void EmitImpactFeedback()
    {
        TransientParticleBurst.Emit(
            Context,
            X,
            Y,
            count: Math.Clamp(4 + (_plan.ExplosionRadius / 2), 4, 28),
            speed: MathF.Max(18f, _plan.Speed * 0.08f),
            lifetime: _plan.ExplosionRadius > 0 ? (ushort)58 : (ushort)30,
            coreColorBgra: ProjectileColor(),
            trailColorBgra: 0xD0_70_90_B8,
            lightIntensity: _plan.ExplosionRadius > 0 ? 0.62f : 0.30f);
        AddProjectileLight();
    }

    private uint ProjectileColor()
    {
        return _plan.Projectile switch
        {
            WandProjectileKind.Material when string.Equals(_plan.Material, "water", StringComparison.Ordinal) => 0xFF_70_D8_FF,
            WandProjectileKind.Material when string.Equals(_plan.Material, "oil", StringComparison.Ordinal) => 0xFF_48_38_28,
            WandProjectileKind.Material when string.Equals(_plan.Material, "acid", StringComparison.Ordinal) => 0xFF_60_FF_78,
            WandProjectileKind.Material => 0xFF_C0_E8_F8,
            WandProjectileKind.Grenade => 0xFF_70_88_FF,
            WandProjectileKind.Dig => 0xFF_D0_90_58,
            WandProjectileKind.Teleport => 0xFF_E0_78_FF,
            WandProjectileKind.Orb => 0xFF_88_C8_FF,
            WandProjectileKind.Bolt => 0xFF_F8_D0_78,
            WandProjectileKind.None => 0xFF_F8_F4_C8,
            _ => 0xFF_F8_F4_C8,
        };
    }
}
