using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 数据驱动武器控制器；负责切换、弹药、冷却、过热与左右键分派。
/// </summary>
public sealed class WeaponController : Behaviour
{
    private const float HeatCooldownPerSecond = 45f;
    private const float LaserHeatPerSecond = 38f;
    private const float OverheatLimit = 100f;
    private const float OverheatRecoveryLimit = 35f;
    private PlayerController? _player;
    private PlayableProjectileTool? _projectile;
    private GrenadeSpawnRequest _pendingGrenade;
    private ExplosionFlashEffect _impactFlash;
    private MaterialId _laserSparkMaterial = MaterialId.Invalid;
    private bool _grenadeSpawnSystemRegistered;

    /// <summary>
    /// 武器配置路径，相对 ContentRoot。
    /// </summary>
    public string CatalogPath { get; set; } = "weapons.json";

    /// <summary>
    /// 当前武器索引。
    /// </summary>
    public int SelectedIndex { get; private set; }

    /// <summary>
    /// 当前加载的武器目录。
    /// </summary>
    public WeaponCatalog? Catalog { get; private set; }

    /// <summary>
    /// 每个武器的剩余弹药。
    /// </summary>
    public int[] Ammo { get; private set; } = [];

    /// <summary>
    /// 当前武器 id。
    /// </summary>
    public string SelectedWeaponId => CurrentWeapon?.Id ?? string.Empty;

    /// <summary>
    /// 当前武器剩余弹药。
    /// </summary>
    public int CurrentAmmo => Ammo.Length == 0 ? 0 : Ammo[SelectedIndex];

    /// <summary>
    /// 所有武器剩余弹药总数。
    /// </summary>
    public int TotalRemainingAmmo
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Ammo.Length; i++)
            {
                total += Math.Max(0, Ammo[i]);
            }

            return total;
        }
    }

    /// <summary>
    /// 当前武器热量。
    /// </summary>
    public float Heat { get; private set; }

    /// <summary>
    /// 当前武器剩余冷却时间。
    /// </summary>
    public float CooldownRemaining { get; private set; }

    /// <summary>
    /// 当前武器剩余换弹时间。
    /// </summary>
    public float ReloadRemaining { get; private set; }

    /// <summary>
    /// 是否正在换弹。
    /// </summary>
    public bool IsReloading => ReloadRemaining > 0f;

    /// <summary>
    /// 是否过热。
    /// </summary>
    public bool IsOverheated { get; private set; }

    /// <summary>
    /// 左键成功分派次数。
    /// </summary>
    public int PrimaryFireCount { get; private set; }

    /// <summary>
    /// 右键成功分派次数。
    /// </summary>
    public int SecondaryFireCount { get; private set; }

    /// <summary>
    /// 最近一次分派的武器类型。
    /// </summary>
    public WeaponKind LastDispatchedKind { get; private set; }

    private WeaponDefinition? CurrentWeapon => Catalog is { Weapons.Length: > 0 } catalog
        ? catalog.Weapons[SelectedIndex]
        : null;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveComponents();
        RegisterGrenadeSpawnSystem();
        LoadCatalog();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        float safeDt = MathF.Max(0f, dt);
        LoadCatalog();
        RegisterGrenadeSpawnSystem();
        _ = _impactFlash.Update(Context, safeDt);
        CoolDown(safeDt);
        HandleSelection();
        HandleReload(safeDt);
        if (CurrentWeapon is null || IsReloading)
        {
            return;
        }

        if (Context.Input.WasPressed(Key.R))
        {
            BeginReload(CurrentWeapon);
            return;
        }

        if (Context.Input.IsMouseDown(MouseButton.Left))
        {
            TryDispatch(CurrentWeapon, secondary: false, safeDt);
        }

        if (Context.Input.WasMousePressed(MouseButton.Right))
        {
            TryDispatch(CurrentWeapon, secondary: true, safeDt);
        }
    }

    private void LoadCatalog()
    {
        if (Catalog is not null)
        {
            return;
        }

        Catalog = Context.Config.Load(CatalogPath, DemoConfigJsonContext.Default.WeaponCatalog);
        Catalog.Validate();
        _laserSparkMaterial = Context.Materials.Resolve("fire");

        Ammo = new int[Catalog.Weapons.Length];
        for (int i = 0; i < Ammo.Length; i++)
        {
            Ammo[i] = Math.Max(0, Catalog.Weapons[i].AmmoMax);
        }
    }

    private void CoolDown(float dt)
    {
        CooldownRemaining = MathF.Max(0f, CooldownRemaining - dt);
        Heat = MathF.Max(0f, Heat - (HeatCooldownPerSecond * dt));
        if (IsOverheated && Heat <= OverheatRecoveryLimit)
        {
            IsOverheated = false;
        }
    }

    private void HandleSelection()
    {
        if (Catalog is null)
        {
            return;
        }

        for (int i = 0; i < Math.Min(6, Catalog.Weapons.Length); i++)
        {
            if (Context.Input.WasPressed((Key)((int)Key.Digit1 + i)))
            {
                Select(i);
                return;
            }
        }

        float wheel = Context.Input.MouseWheelY;
        if (wheel > 0f)
        {
            Select((SelectedIndex + Catalog.Weapons.Length - 1) % Catalog.Weapons.Length);
        }
        else if (wheel < 0f)
        {
            Select((SelectedIndex + 1) % Catalog.Weapons.Length);
        }
    }

    private void Select(int index)
    {
        if (Catalog is null || (uint)index >= (uint)Catalog.Weapons.Length)
        {
            return;
        }

        SelectedIndex = index;
        ReloadRemaining = 0f;
        CooldownRemaining = 0f;
    }

    private void HandleReload(float dt)
    {
        if (ReloadRemaining <= 0f || CurrentWeapon is null)
        {
            return;
        }

        ReloadRemaining = MathF.Max(0f, ReloadRemaining - dt);
        if (ReloadRemaining == 0f)
        {
            Ammo[SelectedIndex] = Math.Max(0, CurrentWeapon.AmmoMax);
        }
    }

    private void BeginReload(WeaponDefinition weapon)
    {
        if (weapon.ReloadSeconds <= 0f || CurrentAmmo >= weapon.AmmoMax)
        {
            return;
        }

        ReloadRemaining = weapon.ReloadSeconds;
    }

    private void TryDispatch(WeaponDefinition weapon, bool secondary, float dt)
    {
        if (CooldownRemaining > 0f || IsOverheated || CurrentAmmo <= 0)
        {
            return;
        }

        bool dispatched = TryDispatchViaProjectileBackend(weapon, secondary);
        if (!dispatched)
        {
            Dispatch(weapon, secondary, dt);
            dispatched = true;
        }

        if (!dispatched)
        {
            return;
        }

        Ammo[SelectedIndex] = Math.Max(0, Ammo[SelectedIndex] - 1);
        CooldownRemaining = MathF.Max(0f, weapon.CooldownSeconds);
        if (weapon.Kind == WeaponKind.Laser)
        {
            Heat = MathF.Min(OverheatLimit, Heat + MathF.Max(weapon.HeatPerCell, LaserHeatPerSecond * dt));
            if (Heat >= OverheatLimit)
            {
                IsOverheated = true;
            }
        }

        LastDispatchedKind = weapon.Kind;
        if (secondary)
        {
            SecondaryFireCount++;
        }
        else
        {
            PrimaryFireCount++;
        }
    }

    private void Dispatch(WeaponDefinition weapon, bool secondary, float dt)
    {
        _ = secondary;
        Point2F target = Context.Camera.ScreenToWorld(Context.Input.MousePixel.X, Context.Input.MousePixel.Y);
        Point2F origin = ResolveMuzzle();
        float dx = target.X - origin.X;
        float dy = target.Y - origin.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            dx = 1f;
            dy = 0f;
            length = 1f;
        }

        dx /= length;
        dy /= length;
        float hitX = target.X;
        float hitY = target.Y;
        if (Context.Solids.Raycast(origin.X, origin.Y, dx, dy, 220f, out RaycastHit hit))
        {
            hitX = hit.X;
            hitY = hit.Y;
        }

        switch (weapon.Kind)
        {
            case WeaponKind.SingleShot:
                Context.World.DamageCircle(hitX, hitY, Math.Max(1, weapon.Radius), weapon.Damage, weapon.Falloff != WeaponFalloff.None);
                EmitImpactFeedback(weapon, hitX, hitY, count: 3);
                break;
            case WeaponKind.Bomb:
                Context.World.Explode(hitX, hitY, Math.Max(1, weapon.Radius), Math.Max(1f, weapon.Impulse));
                _impactFlash.Start(hitX, hitY, Math.Max(1, weapon.Radius), 0xFF_30_80_FF);
                _impactFlash.SubmitInitial(Context);
                EmitImpactFeedback(weapon, hitX, hitY, count: 10);
                break;
            case WeaponKind.Grenade:
                SpawnGrenade(weapon, origin.X, origin.Y, dx, dy, secondary);
                break;
            case WeaponKind.Laser:
                float hitDistance = MathF.Sqrt(((hitX - origin.X) * (hitX - origin.X)) + ((hitY - origin.Y) * (hitY - origin.Y)));
                DispatchLaser(weapon, origin.X, origin.Y, dx, dy, Math.Min(RangeOrHitLength(hitDistance), 260), hitX, hitY, dt);
                DrawBeamOverlay(origin.X, origin.Y, hitX, hitY, 0xFF_58_E6_FF);
                EmitImpactFeedback(weapon, hitX, hitY, count: 2);
                break;
            case WeaponKind.Excavator:
                int excavatorRadius = Math.Max(1, weapon.Radius);
                PublishMineYieldForCircle(hitX, hitY, excavatorRadius);
                Context.Cells.Paint((int)MathF.Round(hitX), (int)MathF.Round(hitY), excavatorRadius, new MaterialId(0));
                EmitImpactFeedback(weapon, hitX, hitY, count: 4);
                break;
            case WeaponKind.Builder:
                MaterialId material = Context.Materials.Resolve(weapon.SpawnMaterial);
                if (material != MaterialId.Invalid)
                {
                    Context.Cells.Paint((int)MathF.Round(hitX), (int)MathF.Round(hitY), Math.Max(1, weapon.Radius), material);
                    Context.Particles.Burst(hitX, hitY, material, Math.Clamp(weapon.Radius, 2, 8), speed: 1.5f);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(weapon), weapon.Kind, "未知武器类型。");
        }

        if (!secondary && !string.IsNullOrWhiteSpace(weapon.ImpactCue))
        {
            Context.Audio.PlayAt(ToCuePath(weapon.ImpactCue), hitX, hitY, 0.7f);
        }
    }

    private void DispatchLaser(
        WeaponDefinition weapon,
        float originX,
        float originY,
        float dirX,
        float dirY,
        int length,
        float hitX,
        float hitY,
        float dt)
    {
        int radius = Math.Max(1, weapon.Radius);
        float damage = MathF.Max(1f, weapon.BeamDps * MathF.Max(dt, 1f / 60f));
        float normalX = -dirY;
        float normalY = dirX;
        for (int offset = -radius; offset <= radius; offset++)
        {
            float weight = 1f - (MathF.Abs(offset) / (radius + 1f));
            Context.World.DamageBeam(
                originX + (normalX * offset),
                originY + (normalY * offset),
                dirX,
                dirY,
                length,
                damage * weight,
                DamageKind.Beam);
        }

        Context.World.AddHeat(hitX, hitY, radius + 1, Math.Max(1f, weapon.HeatPerCell));
        EmitLaserSparks(weapon, hitX, hitY, dirX, dirY, radius);
    }

    private void EmitLaserSparks(WeaponDefinition weapon, float hitX, float hitY, float dirX, float dirY, int radius)
    {
        if (!_laserSparkMaterial.IsValid)
        {
            return;
        }

        int count = Math.Clamp(radius + 3, 3, 12);
        float angle = MathF.Atan2(-dirY, -dirX);
        Context.Particles.Emit(new ParticleEmit(
            hitX,
            hitY,
            _laserSparkMaterial,
            count,
            angle,
            DirSpreadRad: 0.65f,
            BaseSpeed: MathF.Max(24f, weapon.BeamDps * 0.015f),
            SpeedJitter: 18f,
            LifeTicks: 18));
    }

    private static int RangeOrHitLength(float length)
    {
        return Math.Max(1, (int)MathF.Ceiling(length));
    }

    private void SpawnGrenade(WeaponDefinition weapon, float originX, float originY, float dirX, float dirY, bool charged)
    {
        float chargeScale = charged ? 1.35f : 1f;
        _pendingGrenade = new GrenadeSpawnRequest(
            true,
            originX,
            originY,
            dirX * Math.Max(1f, weapon.ThrowSpeed) * 24f * chargeScale,
            dirY * Math.Max(1f, weapon.ThrowSpeed) * 24f * chargeScale,
            Math.Max(0.05f, weapon.FuseSeconds),
            Math.Max(1, weapon.Radius),
            Math.Max(1f, weapon.Damage),
            Math.Max(1f, weapon.Impulse),
            Math.Max(0f, weapon.Gravity) * 48f,
            Math.Clamp(weapon.Bounce, 0f, 0.9f),
            ToCuePath(weapon.ImpactCue));
    }

    private void RegisterGrenadeSpawnSystem()
    {
        if (_grenadeSpawnSystemRegistered)
        {
            return;
        }

        Context.Scene.RegisterSystem(new GrenadeSpawnSystem(this));
        _grenadeSpawnSystemRegistered = true;
    }

    private void FlushPendingGrenade()
    {
        if (!_pendingGrenade.HasValue)
        {
            return;
        }

        GrenadeSpawnRequest request = _pendingGrenade;
        _pendingGrenade = default;
        Entity grenadeEntity = Context.Scene.CreateEntity();
        Transform transform = grenadeEntity.AddComponent<Transform>();
        transform.SetPosition(request.X, request.Y);
        GrenadeProjectile grenade = grenadeEntity.AddComponent<GrenadeProjectile>();
        grenade.Initialize(
            request.X,
            request.Y,
            request.Vx,
            request.Vy,
            request.FuseSeconds,
            request.Radius,
            request.Damage,
            request.Impulse,
            request.Gravity,
            request.Bounce,
            request.ImpactCue);
    }

    private void EmitImpactFeedback(WeaponDefinition weapon, float x, float y, int count)
    {
        TransientParticleBurst.Emit(
            Context,
            x,
            y,
            Math.Clamp(count, 1, 32),
            Math.Max(8f, weapon.Impulse * 1.5f),
            lifetime: weapon.Kind == WeaponKind.Bomb ? (ushort)36 : (ushort)24);
    }

    private void PublishMineYieldForCircle(float x, float y, int radius)
    {
        int centerX = (int)MathF.Round(x);
        int centerY = (int)MathF.Round(y);
        int radiusSquared = radius * radius;
        int amount = 0;
        ushort materialId = 0;
        for (int yy = centerY - radius; yy <= centerY + radius; yy++)
        {
            int dy = yy - centerY;
            for (int xx = centerX - radius; xx <= centerX + radius; xx++)
            {
                int dx = xx - centerX;
                if ((dx * dx) + (dy * dy) > radiusSquared)
                {
                    continue;
                }

                MaterialId material = Context.Cells.GetMaterial(xx, yy);
                if (!material.IsValid || material.Value == 0)
                {
                    continue;
                }

                MaterialInfo info = Context.Materials.GetInfo(material);
                if (info.MineYield == 0)
                {
                    continue;
                }

                materialId = material.Value;
                amount += info.MineYield;
                if (amount >= ushort.MaxValue)
                {
                    amount = ushort.MaxValue;
                    break;
                }
            }

            if (amount >= ushort.MaxValue)
            {
                break;
            }
        }

        if (amount > 0)
        {
            MineYieldEvent item = new(centerX, centerY, materialId, (ushort)amount);
            _ = Context.Events.TryPublish(in item);
        }
    }

    private void DrawBeamOverlay(float startX, float startY, float endX, float endY, uint color)
    {
        Point2F start = Context.Camera.WorldToScreen(startX, startY);
        Point2F end = Context.Camera.WorldToScreen(endX, endY);
        Context.Overlay.Line(start.X, start.Y, end.X, end.Y, 3f, color);
    }

    private bool TryDispatchViaProjectileBackend(WeaponDefinition weapon, bool secondary)
    {
        if (secondary || weapon.Kind != WeaponKind.SingleShot)
        {
            return false;
        }

        ResolveComponents();
        if (_projectile is null)
        {
            return false;
        }

        _projectile.ImpactRadius = Math.Max(1, weapon.Radius);
        _projectile.ImpactForce = Math.Max(1f, weapon.Impulse);
        _projectile.ImpactDamage = Math.Max(1f, weapon.Damage);
        _projectile.UseExplosionDamage = false;
        _projectile.CooldownSeconds = Math.Max(0f, weapon.CooldownSeconds);
        _projectile.TracerDurationSeconds = Math.Clamp(weapon.TracerDuration, 0f, 0.25f);
        return _projectile.FireOnceFromCurrentInput();
    }

    private Point2F ResolveMuzzle()
    {
        ResolveComponents();
        return _player is null
            ? Context.Camera.ScreenToWorld(Context.Input.MousePixel.X, Context.Input.MousePixel.Y)
            : new Point2F(_player.CenterX, _player.CenterY - 2f);
    }

    private void ResolveComponents()
    {
        if (_player is null)
        {
            if (Entity.TryGetComponent(out PlayerController player))
            {
                _player = player;
            }
        }

        if (_projectile is null)
        {
            if (Entity.TryGetComponent(out PlayableProjectileTool projectile))
            {
                _projectile = projectile;
            }
        }
    }

    private static string ToCuePath(string cue)
    {
        return cue.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? cue : cue + ".wav";
    }

    private readonly record struct GrenadeSpawnRequest(
        bool HasValue,
        float X,
        float Y,
        float Vx,
        float Vy,
        float FuseSeconds,
        int Radius,
        float Damage,
        float Impulse,
        float Gravity,
        float Bounce,
        string ImpactCue);

    private sealed class GrenadeSpawnSystem(WeaponController owner) : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
            _ = context;
        }

        public void OnFrame(IScriptContext context, float dt)
        {
            _ = context;
            _ = dt;
            owner.FlushPendingGrenade();
        }
    }
}
