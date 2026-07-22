using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 驱动主路径底部 Portal：由 Teleportatium 池供能，固定传送到对应 Holy Mountain 内部。
/// </summary>
public sealed class CampaignPortalNetwork : Behaviour
{
    private const uint ActiveOuterColorBgra = 0xF0_FF_7A_E6;
    private const uint ActiveInnerColorBgra = 0xE8_FF_EA_FF;
    private const uint InactiveColorBgra = 0xB0_58_4A_66;
    private CampaignConfig? _config;
    private PortalNetworkDefinition? _portal;
    private CampaignPortalAnchor[] _anchors = [];
    private PlayerController? _player;
    private PlayerHealth? _health;
    private CampaignRunDirector? _run;
    private MaterialId _teleportatium;
    private float _cooldownRemainingSeconds;
    private float _pulse;

    /// <summary>当前黑屏过渡剩余时间。</summary>
    public float TransitionFadeRemainingSeconds { get; private set; }

    /// <summary>本轮成功通过 Portal 的次数。</summary>
    public int TransitionCount { get; private set; }

    /// <summary>最近一次成功通过的 Holy Mountain 索引；未触发时为 -1。</summary>
    public int LastTransitionHolyMountainIndex { get; private set; } = -1;

    /// <summary>最近一次检查到的供能池 Teleportatium cell 数。</summary>
    public int LastPoweredCellCount { get; private set; }

    /// <summary>最近一次玩家附近的 Portal 是否处于供能状态。</summary>
    public bool LastPortalActive { get; private set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        _config = CampaignConfig.Load(Context.Config);
        BiomeCatalog catalog = BiomeCatalog.Load(Context.Config, _config);
        _portal = catalog.PortalNetwork;
        _teleportatium = Context.Materials.Resolve(_portal.TeleportatiumMaterial);
        if (!_teleportatium.IsValid)
        {
            throw new InvalidOperationException(
                $"Campaign Portal 需要材质 {_portal.TeleportatiumMaterial}。");
        }

        int holyMountainCount = CampaignConfig.RequiredRegionCount - 1;
        _anchors = new CampaignPortalAnchor[holyMountainCount * _portal.PortalsPerHolyMountain];
        ResolveComponents();
        ulong worldSeed = _run is null || _run.RunSeed == 0
            ? _config.InitialRunSeed
            : _run.RunSeed;
        int anchorIndex = 0;
        for (int holyMountainIndex = 0; holyMountainIndex < holyMountainCount; holyMountainIndex++)
        {
            for (int portalIndex = 0; portalIndex < _portal.PortalsPerHolyMountain; portalIndex++)
            {
                _anchors[anchorIndex++] = PlayableCavernWorldGenerator.ResolvePortalAnchor(
                    _config,
                    _portal,
                    holyMountainIndex,
                    portalIndex,
                    worldSeed);
            }
        }

        _cooldownRemainingSeconds = 0f;
        TransitionFadeRemainingSeconds = 0f;
        TransitionCount = 0;
        LastTransitionHolyMountainIndex = -1;
        LastPoweredCellCount = 0;
        LastPortalActive = false;
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f)
        {
            return;
        }

        _pulse += dt;
        _cooldownRemainingSeconds = MathF.Max(0f, _cooldownRemainingSeconds - dt);
        TransitionFadeRemainingSeconds = MathF.Max(0f, TransitionFadeRemainingSeconds - dt);
        DrawTransitionFade();
        ResolveComponents();
        if (_config is null || _portal is null || _player is null || _run is null ||
            _run.Mode != DemoGameMode.Campaign ||
            _run.State is not (CampaignRunState.StartingRun or CampaignRunState.Exploring))
        {
            LastPortalActive = false;
            LastPoweredCellCount = 0;
            return;
        }

        CampaignDepthLocation location = _config.ResolveLocation((long)MathF.Floor(_player.CenterY));
        if (location.Kind != CampaignDepthKind.Region ||
            (uint)location.RegionIndex >= CampaignConfig.RequiredRegionCount - 1)
        {
            LastPortalActive = false;
            LastPoweredCellCount = 0;
            return;
        }

        int firstPortalIndex = location.RegionIndex * _portal.PortalsPerHolyMountain;
        LastPortalActive = false;
        LastPoweredCellCount = 0;
        for (int i = 0; i < _portal.PortalsPerHolyMountain; i++)
        {
            CampaignPortalAnchor anchor = _anchors[firstPortalIndex + i];
            bool nearPortal = IsNearPortal(anchor);
            if (!nearPortal && !IsVisible(anchor))
            {
                continue;
            }

            int poweredCells = CountPoweredCells(anchor);
            bool active = poweredCells >= _portal.MinimumPowerCells;
            if (nearPortal)
            {
                LastPoweredCellCount = poweredCells;
                LastPortalActive = active;
            }

            DrawPortal(anchor, active);
            if (nearPortal && active && _cooldownRemainingSeconds <= 0f)
            {
                Activate(anchor);
                return;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _anchors = [];
        _player = null;
        _health = null;
        _run = null;
        _config = null;
        _portal = null;
        _teleportatium = default;
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

        if (_health is null)
        {
            if (Entity.TryGetComponent(out PlayerHealth health))
            {
                _health = health;
            }
        }

        if (_run is null)
        {
            if (Entity.TryGetComponent(out CampaignRunDirector run))
            {
                _run = run;
            }
        }
    }

    private bool IsNearPortal(in CampaignPortalAnchor anchor)
    {
        PortalNetworkDefinition portal = _portal!;
        CharacterState state = _player!.State;
        float minimumX = anchor.SourceX - portal.TriggerHalfWidthCells;
        float maximumX = anchor.SourceX + portal.TriggerHalfWidthCells;
        float minimumY = anchor.SourceY - portal.TriggerHalfHeightCells;
        float maximumY = anchor.SourceY + portal.TriggerHalfHeightCells;
        return state.X < maximumX && state.X + state.Width > minimumX &&
            state.Y < maximumY && state.Y + state.Height > minimumY;
    }

    private bool IsVisible(in CampaignPortalAnchor anchor)
    {
        Point2F screen = Context.Camera.WorldToScreen(anchor.SourceX, anchor.SourceY);
        RectF viewport = Context.Camera.Viewport;
        const float Margin = 40f;
        return screen.X >= viewport.X - Margin && screen.X <= viewport.X + viewport.Width + Margin &&
            screen.Y >= viewport.Y - Margin && screen.Y <= viewport.Y + viewport.Height + Margin;
    }

    private int CountPoweredCells(in CampaignPortalAnchor anchor)
    {
        PortalNetworkDefinition portal = _portal!;
        int centerX = checked((int)anchor.SourceX);
        int sourceY = checked((int)anchor.SourceY);
        int count = 0;
        int minimumX = centerX - portal.BasinHalfWidthCells + 1;
        int maximumX = centerX + portal.BasinHalfWidthCells - 1;
        int minimumY = sourceY + portal.BasinTopOffsetCells + 2;
        int maximumY = sourceY + portal.BasinTopOffsetCells + portal.BasinDepthCells - 1;
        for (int y = minimumY; y <= maximumY; y++)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                if (TryGetMaterial(x, y, out MaterialId material) && material == _teleportatium)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool TryGetMaterial(int x, int y, out MaterialId material)
    {
        try
        {
            material = Context.Cells.GetMaterial(x, y);
            return true;
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("目标 chunk 未驻留", StringComparison.Ordinal))
        {
            material = default;
            return false;
        }
    }

    private void Activate(in CampaignPortalAnchor anchor)
    {
        PortalNetworkDefinition portal = _portal!;
        _player!.TeleportToCenter(anchor.DestinationX, anchor.DestinationY);
        _health?.GrantInvulnerability((float)portal.InvulnerabilitySeconds);
        Context.Particles.Burst(
            anchor.SourceX,
            anchor.SourceY,
            _teleportatium,
            count: 12,
            speed: 28f);
        Context.Particles.Burst(
            anchor.DestinationX,
            anchor.DestinationY,
            _teleportatium,
            count: 12,
            speed: 28f);
        _cooldownRemainingSeconds = (float)portal.CooldownSeconds;
        TransitionFadeRemainingSeconds = (float)portal.TransitionSeconds;
        TransitionCount++;
        LastTransitionHolyMountainIndex = anchor.HolyMountainIndex;
    }

    private void DrawPortal(in CampaignPortalAnchor anchor, bool active)
    {
        Point2F center = Context.Camera.WorldToScreen(anchor.SourceX, anchor.SourceY);
        float zoom = MathF.Max(1f, Context.Camera.Zoom);
        float pulse = 1f + (MathF.Sin(_pulse * 6f) * 0.08f);
        float outerWidth = MathF.Max(16f, _portal!.TriggerHalfWidthCells * 2f * zoom * pulse);
        float outerHeight = MathF.Max(24f, _portal.TriggerHalfHeightCells * 2f * zoom * pulse);
        uint outerColor = active ? ActiveOuterColorBgra : InactiveColorBgra;
        Context.Overlay.OutlineRectangle(
            center.X - (outerWidth * 0.5f),
            center.Y - (outerHeight * 0.5f),
            outerWidth,
            outerHeight,
            MathF.Max(1f, zoom),
            outerColor);
        Context.Overlay.OutlineRectangle(
            center.X - (outerWidth * 0.28f),
            center.Y - (outerHeight * 0.36f),
            outerWidth * 0.56f,
            outerHeight * 0.72f,
            MathF.Max(1f, zoom * 0.75f),
            active ? ActiveInnerColorBgra : InactiveColorBgra);
        if (active)
        {
            Context.Lighting.AddPointLight(anchor.SourceX, anchor.SourceY, 52f, ActiveOuterColorBgra, 0.72f);
        }
    }

    private void DrawTransitionFade()
    {
        if (TransitionFadeRemainingSeconds <= 0f || _portal is null)
        {
            return;
        }

        float fraction = Math.Clamp(
            TransitionFadeRemainingSeconds / (float)_portal.TransitionSeconds,
            0f,
            1f);
        byte alpha = (byte)Math.Clamp((int)MathF.Round(fraction * 230f), 0, 230);
        RectF viewport = Context.Camera.Viewport;
        Context.Overlay.SolidRectangle(
            viewport.X,
            viewport.Y,
            viewport.Width,
            viewport.Height,
            (uint)alpha << 24);
    }
}
