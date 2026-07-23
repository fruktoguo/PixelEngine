using System.IO.Compression;
using System.Security.Cryptography;

namespace PixelEngine.Demo;

/// <summary>
/// Noita biome XML 中 <c>BitmapCaves</c> 节点的逐字段快照。所有范围均保留源文件的
/// inclusive min/max 语义；缺少该节点与声明为全零配置是两种不同状态。
/// </summary>
internal sealed class NoitaBitmapCavesDefinition
{
    public int SizeX { get; init; }

    public int SizeY { get; init; }

    public double SpawnPercent { get; init; }

    public int BlobCavesCountMin { get; init; }

    public int BlobCavesCountMax { get; init; }

    public double BlobCavesRadiusMin { get; init; }

    public double BlobCavesRadiusMax { get; init; }

    public double BlobCavesStrengthMin { get; init; }

    public double BlobCavesStrengthMax { get; init; }

    public int CaveChildsMin { get; init; }

    public int CaveChildsMax { get; init; }

    public int CaveCountMin { get; init; }

    public int CaveCountMax { get; init; }

    public double CaveStrengthMin { get; init; }

    public double CaveStrengthMax { get; init; }

    public int MountainCountMin { get; init; }

    public int MountainCountMax { get; init; }

    public double MountainSizeMin { get; init; }

    public double MountainSizeMax { get; init; }

    public int SurfaceCaveChildsMin { get; init; }

    public int SurfaceCaveChildsMax { get; init; }

    public int SurfaceCavesCountMin { get; init; }

    public int SurfaceCavesCountMax { get; init; }

    public NoitaBitmapCaveStructureDefinition[] Structures { get; init; } = [];
}

/// <summary>
/// <c>CaveStructure</c> 的来源、放置范围与经材质/marker 表解码后的语义像素。
/// </summary>
internal sealed class NoitaBitmapCaveStructureDefinition
{
    public string SourceImagePath { get; init; } = string.Empty;

    public string SourceImageSha256 { get; init; } = string.Empty;

    public int SourceWidth { get; init; }

    public int SourceHeight { get; init; }

    public int AabbMinX { get; init; }

    public int AabbMaxX { get; init; }

    public int AabbMinY { get; init; }

    public int AabbMaxY { get; init; }

    public int CountMin { get; init; }

    public int CountMax { get; init; }

    public double StrengthMin { get; init; }

    public double StrengthMax { get; init; }

    public string Encoding { get; init; } = string.Empty;

    public int DecodedLength { get; init; }

    public string DecodedSha256 { get; init; } = string.Empty;

    public string Data { get; init; } = string.Empty;
}

/// <summary>
/// 已校验并编译的 BitmapCaves 配置。Noita 的 native BitmapCaves RNG/栅格器并未随数据公开，
/// 因此这里严格复用其 XML 参数与结构语义，并用版本化、全局坐标确定性的 Demo 栅格器执行；
/// 后续取得原始算法证据时必须升级 world persistence key，而不能静默改变旧存档。
/// </summary>
internal sealed class DecodedNoitaBitmapCaves
{
    private const double Inverse53Bit = 1.0 / 9_007_199_254_740_992.0;
    private const ulong CountSalt = 0xA076_1D64_78BD_642FUL;
    private const ulong PositionSalt = 0xE703_7ED1_A0B4_28DBUL;
    private const ulong StrengthSalt = 0x8EBC_6AF0_9C88_C6E3UL;
    private const ulong ChildSalt = 0x5899_65CC_7537_4CC3UL;
    private const ulong BoundarySalt = 0x1D8E_4E27_C47D_124FUL;
    private const ulong StructureSalt = 0xEB44_ACCAB4_55D165UL;
    private const int CachedBlocksPerThread = 16;
    private const int MaximumFeaturesPerBlock = 256;
    private const int MaximumBlockWidth = 516;
    private const int MaximumBlockHeight = 256;
    private const int MaximumBlockCells = MaximumBlockWidth * MaximumBlockHeight;
    private const int SpatialBinShift = 6;
    private const int SpatialBinSize = 1 << SpatialBinShift;
    private const int MaximumSpatialBinsX = (MaximumBlockWidth + SpatialBinSize - 1) / SpatialBinSize;
    private const int MaximumSpatialBinsY = (MaximumBlockHeight + SpatialBinSize - 1) / SpatialBinSize;
    private const int MaximumSpatialBins = MaximumSpatialBinsX * MaximumSpatialBinsY;
    private const int MaximumSpatialReferences = MaximumSpatialBins * MaximumFeaturesPerBlock;
    private const int BoundaryTileShift = 3;
    private const int BoundaryTileSize = 1 << BoundaryTileShift;
    private const int MaximumBoundaryTilesX = (MaximumBlockWidth + BoundaryTileSize - 1) / BoundaryTileSize;
    private const int MaximumBoundaryTilesY = (MaximumBlockHeight + BoundaryTileSize - 1) / BoundaryTileSize;
    private const int MaximumBoundaryTiles = MaximumBoundaryTilesX * MaximumBoundaryTilesY;
    private const double MaximumBoundaryScale = 1.12;

    [ThreadStatic]
    private static BitmapCavesThreadCache? _threadCache;

    private readonly NoitaBitmapCavesDefinition _definition;
    private readonly DecodedNoitaBitmapCaveStructure[] _structures;

    private DecodedNoitaBitmapCaves(
        NoitaBitmapCavesDefinition definition,
        DecodedNoitaBitmapCaveStructure[] structures)
    {
        _definition = definition;
        _structures = structures;
        IsEnabled = definition.BlobCavesCountMax > 0 ||
            definition.CaveCountMax > 0 ||
            definition.MountainCountMax > 0 ||
            definition.SurfaceCavesCountMax > 0 ||
            structures.Any(static structure => structure.CountMax > 0);
    }

    internal int SizeX => _definition.SizeX;

    internal int SizeY => _definition.SizeY;

    internal bool IsEnabled { get; }

    internal ReadOnlySpan<DecodedNoitaBitmapCaveStructure> Structures => _structures;

    internal static DecodedNoitaBitmapCaves Decode(
        NoitaBitmapCavesDefinition definition,
        int markerCount,
        string label)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateCountRange(definition.BlobCavesCountMin, definition.BlobCavesCountMax, $"{label}.blobCavesCount");
        ValidateRange(definition.BlobCavesRadiusMin, definition.BlobCavesRadiusMax, $"{label}.blobCavesRadius");
        ValidateRange(definition.BlobCavesStrengthMin, definition.BlobCavesStrengthMax, $"{label}.blobCavesStrength");
        ValidateCountRange(definition.CaveChildsMin, definition.CaveChildsMax, $"{label}.caveChilds");
        ValidateCountRange(definition.CaveCountMin, definition.CaveCountMax, $"{label}.caveCount");
        ValidateRange(definition.CaveStrengthMin, definition.CaveStrengthMax, $"{label}.caveStrength");
        ValidateCountRange(definition.MountainCountMin, definition.MountainCountMax, $"{label}.mountainCount");
        ValidateRange(definition.MountainSizeMin, definition.MountainSizeMax, $"{label}.mountainSize");
        ValidateCountRange(definition.SurfaceCaveChildsMin, definition.SurfaceCaveChildsMax, $"{label}.surfaceCaveChilds");
        ValidateCountRange(definition.SurfaceCavesCountMin, definition.SurfaceCavesCountMax, $"{label}.surfaceCavesCount");
        Require(double.IsFinite(definition.SpawnPercent) && definition.SpawnPercent is >= 0.0 and <= 100.0,
            $"{label}.spawnPercent 必须位于 [0,100]。");

        NoitaBitmapCaveStructureDefinition[] structures = definition.Structures ??
            throw Invalid($"{label}.structures 不能为空。");
        Require(structures.Length <= 16, $"{label}.structures 最多包含 16 项。");
        DecodedNoitaBitmapCaveStructure[] decodedStructures = new DecodedNoitaBitmapCaveStructure[structures.Length];
        for (int i = 0; i < structures.Length; i++)
        {
            decodedStructures[i] = DecodeStructure(
                structures[i] ?? throw Invalid($"{label}.structures[{i}] 不能为空。"),
                markerCount,
                $"{label}.structures[{i}]");
        }

        bool enabled = definition.BlobCavesCountMax > 0 ||
            definition.CaveCountMax > 0 ||
            definition.MountainCountMax > 0 ||
            definition.SurfaceCavesCountMax > 0 ||
            decodedStructures.Any(static structure => structure.CountMax > 0);
        int maximumFeatureCount = checked(
            definition.BlobCavesCountMax +
            definition.MountainCountMax +
            (definition.CaveCountMax * (2 + definition.CaveChildsMax)) +
            (definition.SurfaceCavesCountMax * (2 + definition.SurfaceCaveChildsMax)));
        for (int i = 0; i < decodedStructures.Length; i++)
        {
            maximumFeatureCount = checked(maximumFeatureCount + decodedStructures[i].CountMax);
        }

        Require(maximumFeatureCount <= MaximumFeaturesPerBlock,
            $"{label} 每 block 最多生成 {MaximumFeaturesPerBlock} 个几何特征，当前上界为 {maximumFeatureCount}。");
        if (enabled)
        {
            Require(definition.SizeX is >= 64 and <= MaximumBlockWidth,
                $"{label}.sizeX 启用时必须位于 [64,{MaximumBlockWidth}]。");
            Require(definition.SizeY is >= 64 and <= MaximumBlockHeight,
                $"{label}.sizeY 启用时必须位于 [64,{MaximumBlockHeight}]。");
        }
        else
        {
            Require(definition.SizeX is 0 or (>= 64 and <= MaximumBlockWidth),
                $"{label}.sizeX 必须为 0 或位于 [64,{MaximumBlockWidth}]。");
            Require(definition.SizeY is 0 or (>= 64 and <= MaximumBlockHeight),
                $"{label}.sizeY 必须为 0 或位于 [64,{MaximumBlockHeight}]。");
        }

        Require(
            definition.SizeX == 0 || definition.SizeY == 0 ||
            definition.SizeX * definition.SizeY <= MaximumBlockCells,
            $"{label}.sizeX*sizeY 最多为 {MaximumBlockCells} cells。");

        return new DecodedNoitaBitmapCaves(definition, decodedStructures);
    }

    internal bool TrySample(
        long worldX,
        long worldY,
        ulong worldSeed,
        ulong biomeSalt,
        out byte semantic)
    {
        if (!IsEnabled)
        {
            semantic = default;
            return false;
        }

        long blockX = FloorDivide(worldX, _definition.SizeX, out int localX);
        long blockY = FloorDivide(worldY, _definition.SizeY, out int localY);
        ulong blockSeed = HashCoordinates(blockX, blockY, worldSeed, biomeSalt);

        BitmapCavesBlockCacheEntry block = GetCompiledBlock(blockX, blockY, worldSeed, biomeSalt, blockSeed);
        int cellIndex = (localY * _definition.SizeX) + localX;
        ulong bit = 1UL << (cellIndex & 63);
        if ((block.OverrideBits[cellIndex >> 6] & bit) != 0UL)
        {
            semantic = block.Semantics[cellIndex];
            return true;
        }

        semantic = default;
        return false;
    }

    private bool TrySampleCompiled(
        BitmapCavesBlockCacheEntry block,
        int localX,
        int localY,
        out byte semantic)
    {
        int boundaryIndex = ((localY >> BoundaryTileShift) * block.BoundaryTilesX) +
            (localX >> BoundaryTileShift);
        double boundaryScale = block.BoundaryScales[boundaryIndex];
        int spatialBinIndex = ((localY >> SpatialBinShift) * block.SpatialBinsX) +
            (localX >> SpatialBinShift);
        int spatialOffset = block.SpatialOffsets[spatialBinIndex];
        int spatialEnd = spatialOffset + block.SpatialCounts[spatialBinIndex];
        for (int spatialIndex = spatialOffset; spatialIndex < spatialEnd; spatialIndex++)
        {
            int featureIndex = block.SpatialFeatureIndices[spatialIndex];
            CompiledBitmapFeature feature = block.Features[featureIndex];
            switch (feature.Kind)
            {
                case BitmapFeatureKind.Structure:
                    {
                        DecodedNoitaBitmapCaveStructure structure = _structures[feature.StructureIndex];
                        int relativeX = localX - (int)feature.X0;
                        int relativeY = localY - (int)feature.Y0;
                        int scaledWidth = structure.Width * (int)feature.Scale;
                        int scaledHeight = structure.Height * (int)feature.Scale;
                        if ((uint)relativeX < (uint)scaledWidth && (uint)relativeY < (uint)scaledHeight)
                        {
                            int scale = (int)feature.Scale;
                            semantic = structure.Pixels[(relativeY / scale * structure.Width) + (relativeX / scale)];
                            return true;
                        }

                        break;
                    }
                case BitmapFeatureKind.Capsule:
                    {
                        double radius = feature.Radius * boundaryScale;
                        if (DistanceSquaredToSegment(localX, localY, feature.X0, feature.Y0, feature.X1, feature.Y1) <=
                            radius * radius)
                        {
                            semantic = (byte)NoitaWangTerrainSemantic.Empty;
                            return true;
                        }

                        break;
                    }
                case BitmapFeatureKind.Ellipse:
                    {
                        double radiusX = feature.Radius * boundaryScale;
                        double radiusY = feature.SecondaryRadius * boundaryScale;
                        double normalizedX = (localX - feature.X0) / radiusX;
                        double normalizedY = (localY - feature.Y0) / radiusY;
                        if ((normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1.0)
                        {
                            semantic = (byte)NoitaWangTerrainSemantic.Empty;
                            return true;
                        }

                        break;
                    }
                case BitmapFeatureKind.Mountain:
                    {
                        double halfWidth = feature.Radius * boundaryScale;
                        double distanceX = Math.Abs(localX - feature.X0);
                        if (distanceX <= halfWidth &&
                            localY >= _definition.SizeY -
                            (feature.SecondaryRadius * (1.0 - (distanceX / halfWidth))))
                        {
                            semantic = (byte)NoitaWangTerrainSemantic.Primary;
                            return true;
                        }

                        break;
                    }
                default:
                    throw new InvalidOperationException($"未知 BitmapCaves feature kind：{feature.Kind}。");
            }
        }

        semantic = default;
        return false;
    }

    private BitmapCavesBlockCacheEntry GetCompiledBlock(
        long blockX,
        long blockY,
        ulong worldSeed,
        ulong biomeSalt,
        ulong blockSeed)
    {
        BitmapCavesThreadCache cache = _threadCache ??= new BitmapCavesThreadCache(CachedBlocksPerThread);
        BitmapCavesBlockCacheEntry? entry = cache.LastEntry;
        if (entry is not null && entry.Matches(this, blockX, blockY, worldSeed, biomeSalt))
        {
            return entry;
        }

        BitmapCavesBlockCacheEntry[] entries = cache.Entries;
        for (int i = 0; i < entries.Length; i++)
        {
            entry = entries[i];
            if (entry.Matches(this, blockX, blockY, worldSeed, biomeSalt))
            {
                cache.LastEntry = entry;
                return entry;
            }
        }

        entry = entries[cache.NextReplacementIndex];
        cache.NextReplacementIndex = (cache.NextReplacementIndex + 1) & (CachedBlocksPerThread - 1);
        CompileBlock(entry, blockX, blockY, worldSeed, biomeSalt, blockSeed);
        cache.LastEntry = entry;
        return entry;
    }

    private void CompileBlock(
        BitmapCavesBlockCacheEntry entry,
        long blockX,
        long blockY,
        ulong worldSeed,
        ulong biomeSalt,
        ulong blockSeed)
    {
        entry.FeatureCount = 0;

        for (int structureIndex = 0; structureIndex < _structures.Length; structureIndex++)
        {
            DecodedNoitaBitmapCaveStructure structure = _structures[structureIndex];
            ulong structureSeed = blockSeed ^ StructureSalt ^ ((ulong)(structureIndex + 1) * PositionSalt);
            int count = ResolveCount(
                structure.CountMin,
                structure.CountMax,
                blockX,
                blockY,
                structureSeed ^ CountSalt);
            for (int instance = 0; instance < count; instance++)
            {
                ulong instanceSeed = structureSeed ^ ((ulong)(instance + 1) * CountSalt);
                double strength = ResolveRange(
                    structure.StrengthMin,
                    structure.StrengthMax,
                    blockX,
                    blockY,
                    instanceSeed ^ StrengthSalt);
                int scale = Math.Clamp((int)Math.Round(2.0 + (strength * 2.0)), 1, 12);
                int width = checked(structure.Width * scale);
                int height = checked(structure.Height * scale);
                int maximumOriginX = Math.Max(structure.AabbMinX, structure.AabbMaxX - width);
                int maximumOriginY = Math.Max(structure.AabbMinY, structure.AabbMaxY - height);
                int originX = ResolveCoordinate(
                    structure.AabbMinX,
                    maximumOriginX,
                    blockX,
                    blockY,
                    instanceSeed ^ PositionSalt);
                int originY = ResolveCoordinate(
                    structure.AabbMinY,
                    maximumOriginY,
                    blockX,
                    blockY,
                    instanceSeed ^ ChildSalt);
                AddFeature(entry, CompiledBitmapFeature.Structure(structureIndex, originX, originY, scale));
            }
        }

        int caveCount = ResolveCount(
            _definition.CaveCountMin,
            _definition.CaveCountMax,
            blockX,
            blockY,
            blockSeed ^ CountSalt);
        for (int cave = 0; cave < caveCount; cave++)
        {
            ulong caveSeed = blockSeed ^ ((ulong)(cave + 1) * PositionSalt);
            double radius = 6.0 +
                (ResolveRange(
                    _definition.CaveStrengthMin,
                    _definition.CaveStrengthMax,
                    blockX,
                    blockY,
                    caveSeed ^ StrengthSalt) * 11.0);
            ResolvePath(caveSeed, blockX, blockY, out double x0, out double y0, out double xm, out double ym, out double x1, out double y1);
            AddFeature(entry, CompiledBitmapFeature.Capsule(x0, y0, xm, ym, radius));
            AddFeature(entry, CompiledBitmapFeature.Capsule(xm, ym, x1, y1, radius));

            int childCount = ResolveCount(
                _definition.CaveChildsMin,
                _definition.CaveChildsMax,
                blockX,
                blockY,
                caveSeed ^ ChildSalt);
            for (int child = 0; child < childCount; child++)
            {
                ulong childSeed = caveSeed ^ ((ulong)(child + 1) * ChildSalt);
                double childEndX = 24.0 +
                    (HashUnit(blockX, blockY, childSeed ^ PositionSalt) * Math.Max(1.0, _definition.SizeX - 48.0));
                double childEndY = 20.0 +
                    (HashUnit(blockX, blockY, childSeed ^ StrengthSalt) * Math.Max(1.0, _definition.SizeY - 40.0));
                double childRadius = radius * (0.58 + (HashUnit(blockX, blockY, childSeed ^ CountSalt) * 0.20));
                AddFeature(entry, CompiledBitmapFeature.Capsule(xm, ym, childEndX, childEndY, childRadius));
            }
        }

        int surfaceCount = ResolveCount(
            _definition.SurfaceCavesCountMin,
            _definition.SurfaceCavesCountMax,
            blockX,
            blockY,
            blockSeed ^ SurfaceSalt(CountSalt));
        for (int cave = 0; cave < surfaceCount; cave++)
        {
            ulong caveSeed = blockSeed ^ SurfaceSalt((ulong)(cave + 1) * PositionSalt);
            double startX = 20.0 +
                (HashUnit(blockX, blockY, caveSeed) * Math.Max(1.0, _definition.SizeX - 40.0));
            double middleX = Math.Clamp(
                startX + ((HashUnit(blockX, blockY, caveSeed ^ ChildSalt) - 0.5) * _definition.SizeX * 0.28),
                12.0,
                _definition.SizeX - 12.0);
            double middleY = _definition.SizeY * (0.22 + (HashUnit(blockX, blockY, caveSeed ^ StrengthSalt) * 0.20));
            double endX = Math.Clamp(
                middleX + ((HashUnit(blockX, blockY, caveSeed ^ CountSalt) - 0.5) * _definition.SizeX * 0.32),
                12.0,
                _definition.SizeX - 12.0);
            double endY = _definition.SizeY * (0.48 + (HashUnit(blockX, blockY, caveSeed ^ BoundarySalt) * 0.32));
            double radius = 9.0 + (_definition.CaveStrengthMax * 8.0);
            AddFeature(entry, CompiledBitmapFeature.Capsule(startX, -radius, middleX, middleY, radius));
            AddFeature(entry, CompiledBitmapFeature.Capsule(middleX, middleY, endX, endY, radius));

            int childCount = ResolveCount(
                _definition.SurfaceCaveChildsMin,
                _definition.SurfaceCaveChildsMax,
                blockX,
                blockY,
                caveSeed ^ ChildSalt);
            for (int child = 0; child < childCount; child++)
            {
                ulong childSeed = caveSeed ^ ((ulong)(child + 1) * StructureSalt);
                double childEndX = 16.0 +
                    (HashUnit(blockX, blockY, childSeed) * Math.Max(1.0, _definition.SizeX - 32.0));
                double childEndY = Math.Clamp(
                    endY + ((HashUnit(blockX, blockY, childSeed ^ PositionSalt) - 0.5) * _definition.SizeY * 0.38),
                    20.0,
                    _definition.SizeY - 20.0);
                AddFeature(entry, CompiledBitmapFeature.Capsule(endX, endY, childEndX, childEndY, radius * 0.62));
            }
        }

        int blobCount = ResolveCount(
            _definition.BlobCavesCountMin,
            _definition.BlobCavesCountMax,
            blockX,
            blockY,
            blockSeed ^ BlobSalt(CountSalt));
        for (int blob = 0; blob < blobCount; blob++)
        {
            ulong blobSeed = blockSeed ^ BlobSalt((ulong)(blob + 1) * PositionSalt);
            double radiusValue = ResolveRange(
                _definition.BlobCavesRadiusMin,
                _definition.BlobCavesRadiusMax,
                blockX,
                blockY,
                blobSeed ^ CountSalt);
            double strength = ResolveRange(
                _definition.BlobCavesStrengthMin,
                _definition.BlobCavesStrengthMax,
                blockX,
                blockY,
                blobSeed ^ StrengthSalt);
            double radiusX = 8.0 + (radiusValue * 6.0) + (strength * 7.0);
            double radiusY = radiusX * (0.62 + (HashUnit(blockX, blockY, blobSeed ^ ChildSalt) * 0.32));
            double centerX = radiusX +
                (HashUnit(blockX, blockY, blobSeed) * Math.Max(1.0, _definition.SizeX - (2.0 * radiusX)));
            double centerY = radiusY +
                (HashUnit(blockX, blockY, blobSeed ^ PositionSalt) * Math.Max(1.0, _definition.SizeY - (2.0 * radiusY)));
            AddFeature(entry, CompiledBitmapFeature.Ellipse(centerX, centerY, radiusX, radiusY));
        }

        int mountainCount = ResolveCount(
            _definition.MountainCountMin,
            _definition.MountainCountMax,
            blockX,
            blockY,
            blockSeed ^ MountainSalt(CountSalt));
        for (int mountain = 0; mountain < mountainCount; mountain++)
        {
            ulong mountainSeed = blockSeed ^ MountainSalt((ulong)(mountain + 1) * PositionSalt);
            double size = ResolveRange(
                _definition.MountainSizeMin,
                _definition.MountainSizeMax,
                blockX,
                blockY,
                mountainSeed ^ StrengthSalt);
            double halfWidth = 12.0 + (size * 8.0);
            double height = Math.Min(_definition.SizeY, 24.0 + (size * 12.0));
            double centerX = halfWidth +
                (HashUnit(blockX, blockY, mountainSeed) * Math.Max(1.0, _definition.SizeX - (2.0 * halfWidth)));
            AddFeature(entry, CompiledBitmapFeature.Mountain(centerX, halfWidth, height));
        }

        CompileSpatialIndex(entry, blockSeed);
        RasterizeBlock(entry);
        entry.SetIdentity(this, blockX, blockY, worldSeed, biomeSalt);
    }

    private void RasterizeBlock(BitmapCavesBlockCacheEntry entry)
    {
        int cellCount = checked(_definition.SizeX * _definition.SizeY);
        Array.Clear(entry.OverrideBits, 0, (cellCount + 63) >> 6);
        for (int localY = 0; localY < _definition.SizeY; localY++)
        {
            int rowOffset = localY * _definition.SizeX;
            for (int localX = 0; localX < _definition.SizeX; localX++)
            {
                if (!TrySampleCompiled(entry, localX, localY, out byte semantic))
                {
                    continue;
                }

                int cellIndex = rowOffset + localX;
                entry.Semantics[cellIndex] = semantic;
                entry.OverrideBits[cellIndex >> 6] |= 1UL << (cellIndex & 63);
            }
        }
    }

    private void CompileSpatialIndex(BitmapCavesBlockCacheEntry entry, ulong blockSeed)
    {
        entry.SpatialBinsX = (_definition.SizeX + SpatialBinSize - 1) >> SpatialBinShift;
        entry.SpatialBinsY = (_definition.SizeY + SpatialBinSize - 1) >> SpatialBinShift;
        int spatialReferenceCount = 0;
        for (int binY = 0; binY < entry.SpatialBinsY; binY++)
        {
            double minimumY = binY * SpatialBinSize;
            double maximumY = Math.Min(_definition.SizeY, minimumY + SpatialBinSize);
            for (int binX = 0; binX < entry.SpatialBinsX; binX++)
            {
                double minimumX = binX * SpatialBinSize;
                double maximumX = Math.Min(_definition.SizeX, minimumX + SpatialBinSize);
                int binIndex = (binY * entry.SpatialBinsX) + binX;
                entry.SpatialOffsets[binIndex] = spatialReferenceCount;
                int count = 0;
                for (int featureIndex = 0; featureIndex < entry.FeatureCount; featureIndex++)
                {
                    if (!FeatureIntersects(
                            entry.Features[featureIndex],
                            minimumX,
                            minimumY,
                            maximumX,
                            maximumY))
                    {
                        continue;
                    }

                    if ((uint)spatialReferenceCount >= (uint)entry.SpatialFeatureIndices.Length)
                    {
                        throw new InvalidOperationException("BitmapCaves 空间索引超过固定容量。");
                    }

                    entry.SpatialFeatureIndices[spatialReferenceCount++] = (byte)featureIndex;
                    count++;
                }

                entry.SpatialCounts[binIndex] = checked((ushort)count);
            }
        }

        entry.BoundaryTilesX = (_definition.SizeX + BoundaryTileSize - 1) >> BoundaryTileShift;
        entry.BoundaryTilesY = (_definition.SizeY + BoundaryTileSize - 1) >> BoundaryTileShift;
        for (int tileY = 0; tileY < entry.BoundaryTilesY; tileY++)
        {
            int rowOffset = tileY * entry.BoundaryTilesX;
            for (int tileX = 0; tileX < entry.BoundaryTilesX; tileX++)
            {
                entry.BoundaryScales[rowOffset + tileX] =
                    0.88 + (HashUnit(tileX, tileY, blockSeed ^ BoundarySalt) * 0.24);
            }
        }
    }

    private bool FeatureIntersects(
        in CompiledBitmapFeature feature,
        double minimumX,
        double minimumY,
        double maximumX,
        double maximumY)
    {
        switch (feature.Kind)
        {
            case BitmapFeatureKind.Structure:
                {
                    DecodedNoitaBitmapCaveStructure structure = _structures[feature.StructureIndex];
                    double structureMaximumX = feature.X0 + (structure.Width * feature.Scale);
                    double structureMaximumY = feature.Y0 + (structure.Height * feature.Scale);
                    return feature.X0 < maximumX && structureMaximumX > minimumX &&
                        feature.Y0 < maximumY && structureMaximumY > minimumY;
                }
            case BitmapFeatureKind.Capsule:
                {
                    double radius = feature.Radius * MaximumBoundaryScale;
                    return Math.Min(feature.X0, feature.X1) - radius < maximumX &&
                        Math.Max(feature.X0, feature.X1) + radius > minimumX &&
                        Math.Min(feature.Y0, feature.Y1) - radius < maximumY &&
                        Math.Max(feature.Y0, feature.Y1) + radius > minimumY;
                }
            case BitmapFeatureKind.Ellipse:
                return feature.X0 - (feature.Radius * MaximumBoundaryScale) < maximumX &&
                    feature.X0 + (feature.Radius * MaximumBoundaryScale) > minimumX &&
                    feature.Y0 - (feature.SecondaryRadius * MaximumBoundaryScale) < maximumY &&
                    feature.Y0 + (feature.SecondaryRadius * MaximumBoundaryScale) > minimumY;
            case BitmapFeatureKind.Mountain:
                return feature.X0 - (feature.Radius * MaximumBoundaryScale) < maximumX &&
                    feature.X0 + (feature.Radius * MaximumBoundaryScale) > minimumX &&
                    _definition.SizeY - feature.SecondaryRadius < maximumY &&
                    _definition.SizeY > minimumY;
            default:
                throw new InvalidOperationException($"未知 BitmapCaves feature kind：{feature.Kind}。");
        }
    }

    private static void AddFeature(BitmapCavesBlockCacheEntry entry, CompiledBitmapFeature feature)
    {
        if ((uint)entry.FeatureCount >= (uint)entry.Features.Length)
        {
            throw new InvalidOperationException("BitmapCaves block 几何特征超过固定容量。");
        }

        entry.Features[entry.FeatureCount++] = feature;
    }

    private static DecodedNoitaBitmapCaveStructure DecodeStructure(
        NoitaBitmapCaveStructureDefinition definition,
        int markerCount,
        string label)
    {
        Require(
            definition.SourceImagePath.StartsWith("data/biome_impl/", StringComparison.Ordinal) &&
            definition.SourceImagePath.EndsWith(".png", StringComparison.Ordinal),
            $"{label}.sourceImagePath 必须位于 data/biome_impl/ 且为 PNG。");
        Require(IsSha256(definition.SourceImageSha256), $"{label}.sourceImageSha256 必须为 64 位 SHA256 hex。");
        Require(definition.SourceWidth is >= 1 and <= 1024, $"{label}.sourceWidth 必须位于 [1,1024]。");
        Require(definition.SourceHeight is >= 1 and <= 1024, $"{label}.sourceHeight 必须位于 [1,1024]。");
        Require(definition.AabbMinX >= 0 && definition.AabbMaxX > definition.AabbMinX, $"{label}.aabb X 范围无效。");
        Require(definition.AabbMinY >= 0 && definition.AabbMaxY > definition.AabbMinY, $"{label}.aabb Y 范围无效。");
        ValidateCountRange(definition.CountMin, definition.CountMax, $"{label}.count");
        ValidateRange(definition.StrengthMin, definition.StrengthMax, $"{label}.strength");
        Require(string.Equals(definition.Encoding, "brotli-pebitmap-v1", StringComparison.Ordinal),
            $"{label}.encoding 必须为 brotli-pebitmap-v1。");
        int expectedLength = checked(definition.SourceWidth * definition.SourceHeight);
        Require(definition.DecodedLength == expectedLength, $"{label}.decodedLength 与图像尺寸不一致。");
        Require(IsSha256(definition.DecodedSha256), $"{label}.decodedSha256 必须为 64 位 SHA256 hex。");
        byte[] pixels = DecodeBrotli(definition.Data, definition.DecodedLength, definition.DecodedSha256, label);
        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = pixels[i];
            bool terrain = value is <= (byte)NoitaWangTerrainSemantic.Pool or
                (byte)NoitaWangTerrainSemantic.RandomBinary;
            bool marker = value >= NoitaWangTerrainCatalog.MarkerSemanticBase &&
                value - NoitaWangTerrainCatalog.MarkerSemanticBase < markerCount;
            Require(terrain || marker, $"{label}.data[{i}] 含未知 semantic {value}。");
        }

        return new DecodedNoitaBitmapCaveStructure(
            definition.SourceWidth,
            definition.SourceHeight,
            definition.AabbMinX,
            definition.AabbMaxX,
            definition.AabbMinY,
            definition.AabbMaxY,
            definition.CountMin,
            definition.CountMax,
            definition.StrengthMin,
            definition.StrengthMax,
            pixels);
    }

    private void ResolvePath(
        ulong seed,
        long blockX,
        long blockY,
        out double x0,
        out double y0,
        out double xm,
        out double ym,
        out double x1,
        out double y1)
    {
        double usableX = Math.Max(1.0, _definition.SizeX - 48.0);
        double usableY = Math.Max(1.0, _definition.SizeY - 40.0);
        x0 = 24.0 + (HashUnit(blockX, blockY, seed) * usableX);
        y0 = 20.0 + (HashUnit(blockX, blockY, seed ^ PositionSalt) * usableY);
        x1 = 24.0 + (HashUnit(blockX, blockY, seed ^ CountSalt) * usableX);
        y1 = 20.0 + (HashUnit(blockX, blockY, seed ^ StrengthSalt) * usableY);
        if (Math.Abs(x1 - x0) < _definition.SizeX * 0.18)
        {
            x1 = _definition.SizeX - x0;
        }

        if (Math.Abs(y1 - y0) < _definition.SizeY * 0.12)
        {
            y1 = _definition.SizeY - y0;
        }

        xm = Math.Clamp(
            ((x0 + x1) * 0.5) + ((HashUnit(blockX, blockY, seed ^ ChildSalt) - 0.5) * _definition.SizeX * 0.18),
            12.0,
            _definition.SizeX - 12.0);
        ym = Math.Clamp(
            ((y0 + y1) * 0.5) + ((HashUnit(blockX, blockY, seed ^ BoundarySalt) - 0.5) * _definition.SizeY * 0.22),
            12.0,
            _definition.SizeY - 12.0);
    }

    private static double DistanceSquaredToSegment(
        double x,
        double y,
        double x0,
        double y0,
        double x1,
        double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double lengthSquared = (dx * dx) + (dy * dy);
        double t = lengthSquared <= double.Epsilon
            ? 0.0
            : Math.Clamp((((x - x0) * dx) + ((y - y0) * dy)) / lengthSquared, 0.0, 1.0);
        double distanceX = x - (x0 + (dx * t));
        double distanceY = y - (y0 + (dy * t));
        return (distanceX * distanceX) + (distanceY * distanceY);
    }

    private static int ResolveCount(
        int minimum,
        int maximum,
        long blockX,
        long blockY,
        ulong salt)
    {
        return minimum == maximum
            ? minimum
            : minimum + (int)(HashCoordinates(blockX, blockY, salt, CountSalt) % (uint)(maximum - minimum + 1));
    }

    private static int ResolveCoordinate(
        int minimum,
        int maximum,
        long blockX,
        long blockY,
        ulong salt)
    {
        return minimum >= maximum
            ? minimum
            : minimum + (int)(HashCoordinates(blockX, blockY, salt, PositionSalt) % (uint)(maximum - minimum + 1));
    }

    private static double ResolveRange(
        double minimum,
        double maximum,
        long blockX,
        long blockY,
        ulong salt)
    {
        return minimum == maximum
            ? minimum
            : minimum + ((maximum - minimum) * HashUnit(blockX, blockY, salt));
    }

    private static ulong HashCoordinates(long x, long y, ulong worldSeed, ulong biomeSalt)
    {
        ulong value = worldSeed ^ biomeSalt;
        unchecked
        {
            value ^= (ulong)x * 0x9E37_79B9_7F4A_7C15UL;
            value ^= (ulong)y * 0xC2B2_AE3D_27D4_EB4FUL;
        }

        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        value ^= value >> 31;
        return value;
    }

    private static double HashUnit(long x, long y, ulong salt)
    {
        return (HashCoordinates(x, y, salt, BoundarySalt) >> 11) * Inverse53Bit;
    }

    private static long FloorDivide(long value, int divisor, out int remainder)
    {
        long quotient = Math.DivRem(value, divisor, out long rawRemainder);
        if (rawRemainder < 0)
        {
            quotient--;
            rawRemainder += divisor;
        }

        remainder = checked((int)rawRemainder);
        return quotient;
    }

    private static ulong SurfaceSalt(ulong value)
    {
        return value ^ 0xD6E8_FEB8_6659_FD93UL;
    }

    private static ulong BlobSalt(ulong value)
    {
        return value ^ 0xDB4F_0B91_75AE_2165UL;
    }

    private static ulong MountainSalt(ulong value)
    {
        return value ^ 0xBBE0_5633_03A4_61E5UL;
    }

    private static byte[] DecodeBrotli(string data, int decodedLength, string expectedSha256, string label)
    {
        byte[] compressed;
        try
        {
            compressed = Convert.FromBase64String(data);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"noita-wang-terrain.json 配置无效：{label}.data 不是合法 Base64。", exception);
        }

        Require(compressed.Length > 0, $"{label}.data 不能为空。");
        byte[] decoded = new byte[decodedLength];
        using MemoryStream source = new(compressed, writable: false);
        using BrotliStream brotli = new(source, CompressionMode.Decompress, leaveOpen: false);
        int offset = 0;
        while (offset < decoded.Length)
        {
            int read = brotli.Read(decoded, offset, decoded.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        Require(offset == decoded.Length && brotli.ReadByte() < 0, $"{label}.data 解压长度必须恰好为 {decodedLength}。");
        string actualSha256 = Convert.ToHexString(SHA256.HashData(decoded));
        Require(string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase),
            $"{label}.decodedSha256 与解码内容不一致。");
        return decoded;
    }

    private static void ValidateCountRange(int minimum, int maximum, string label)
    {
        Require(minimum is >= 0 and <= 32, $"{label}Min 必须位于 [0,32]。");
        Require(maximum >= minimum && maximum <= 32, $"{label}Max 必须位于 [{minimum},32]。");
    }

    private static void ValidateRange(double minimum, double maximum, string label)
    {
        Require(double.IsFinite(minimum) && minimum is >= 0.0 and <= 64.0,
            $"{label}Min 必须位于 [0,64]。");
        Require(double.IsFinite(maximum) && maximum >= minimum && maximum <= 64.0,
            $"{label}Max 必须位于 [{minimum},64]。");
    }

    private static bool IsSha256(string value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidDataException Invalid(string message)
    {
        return new InvalidDataException($"noita-wang-terrain.json 配置无效：{message}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Invalid(message);
        }
    }

    private sealed class BitmapCavesThreadCache
    {
        internal BitmapCavesThreadCache(int entryCount)
        {
            Entries = new BitmapCavesBlockCacheEntry[entryCount];
            for (int i = 0; i < Entries.Length; i++)
            {
                Entries[i] = new BitmapCavesBlockCacheEntry();
            }
        }

        internal BitmapCavesBlockCacheEntry[] Entries { get; }

        internal BitmapCavesBlockCacheEntry? LastEntry { get; set; }

        internal int NextReplacementIndex { get; set; }
    }

    private sealed class BitmapCavesBlockCacheEntry
    {
        private DecodedNoitaBitmapCaves? _owner;
        private long _blockX;
        private long _blockY;
        private ulong _worldSeed;
        private ulong _biomeSalt;

        internal CompiledBitmapFeature[] Features { get; } = new CompiledBitmapFeature[MaximumFeaturesPerBlock];

        internal int FeatureCount { get; set; }

        internal int[] SpatialOffsets { get; } = new int[MaximumSpatialBins];

        internal ushort[] SpatialCounts { get; } = new ushort[MaximumSpatialBins];

        internal byte[] SpatialFeatureIndices { get; } = new byte[MaximumSpatialReferences];

        internal int SpatialBinsX { get; set; }

        internal int SpatialBinsY { get; set; }

        internal double[] BoundaryScales { get; } = new double[MaximumBoundaryTiles];

        internal int BoundaryTilesX { get; set; }

        internal int BoundaryTilesY { get; set; }

        internal byte[] Semantics { get; } = new byte[MaximumBlockCells];

        internal ulong[] OverrideBits { get; } = new ulong[(MaximumBlockCells + 63) / 64];

        internal bool Matches(
            DecodedNoitaBitmapCaves owner,
            long blockX,
            long blockY,
            ulong worldSeed,
            ulong biomeSalt)
        {
            return ReferenceEquals(_owner, owner) &&
                _blockX == blockX &&
                _blockY == blockY &&
                _worldSeed == worldSeed &&
                _biomeSalt == biomeSalt;
        }

        internal void SetIdentity(
            DecodedNoitaBitmapCaves owner,
            long blockX,
            long blockY,
            ulong worldSeed,
            ulong biomeSalt)
        {
            _owner = owner;
            _blockX = blockX;
            _blockY = blockY;
            _worldSeed = worldSeed;
            _biomeSalt = biomeSalt;
        }
    }

    private readonly struct CompiledBitmapFeature(
        BitmapFeatureKind kind,
        int structureIndex,
        double x0,
        double y0,
        double x1,
        double y1,
        double radius,
        double secondaryRadius,
        double scale)
    {
        internal BitmapFeatureKind Kind { get; } = kind;

        internal int StructureIndex { get; } = structureIndex;

        internal double X0 { get; } = x0;

        internal double Y0 { get; } = y0;

        internal double X1 { get; } = x1;

        internal double Y1 { get; } = y1;

        internal double Radius { get; } = radius;

        internal double SecondaryRadius { get; } = secondaryRadius;

        internal double Scale { get; } = scale;

        internal static CompiledBitmapFeature Structure(int structureIndex, int originX, int originY, int scale)
        {
            return new CompiledBitmapFeature(
                BitmapFeatureKind.Structure,
                structureIndex,
                originX,
                originY,
                0.0,
                0.0,
                0.0,
                0.0,
                scale);
        }

        internal static CompiledBitmapFeature Capsule(
            double x0,
            double y0,
            double x1,
            double y1,
            double radius)
        {
            return new CompiledBitmapFeature(
                BitmapFeatureKind.Capsule,
                -1,
                x0,
                y0,
                x1,
                y1,
                radius,
                0.0,
                0.0);
        }

        internal static CompiledBitmapFeature Ellipse(
            double centerX,
            double centerY,
            double radiusX,
            double radiusY)
        {
            return new CompiledBitmapFeature(
                BitmapFeatureKind.Ellipse,
                -1,
                centerX,
                centerY,
                0.0,
                0.0,
                radiusX,
                radiusY,
                0.0);
        }

        internal static CompiledBitmapFeature Mountain(double centerX, double halfWidth, double height)
        {
            return new CompiledBitmapFeature(
                BitmapFeatureKind.Mountain,
                -1,
                centerX,
                0.0,
                0.0,
                0.0,
                halfWidth,
                height,
                0.0);
        }
    }

    private enum BitmapFeatureKind : byte
    {
        Structure,
        Capsule,
        Ellipse,
        Mountain,
    }
}

/// <summary>
/// CaveStructure 的只读语义图与放置范围。
/// </summary>
internal sealed class DecodedNoitaBitmapCaveStructure(
    int width,
    int height,
    int aabbMinX,
    int aabbMaxX,
    int aabbMinY,
    int aabbMaxY,
    int countMin,
    int countMax,
    double strengthMin,
    double strengthMax,
    byte[] pixels)
{
    internal int Width { get; } = width;

    internal int Height { get; } = height;

    internal int AabbMinX { get; } = aabbMinX;

    internal int AabbMaxX { get; } = aabbMaxX;

    internal int AabbMinY { get; } = aabbMinY;

    internal int AabbMaxY { get; } = aabbMaxY;

    internal int CountMin { get; } = countMin;

    internal int CountMax { get; } = countMax;

    internal double StrengthMin { get; } = strengthMin;

    internal double StrengthMax { get; } = strengthMax;

    internal byte[] Pixels { get; } = pixels;
}
