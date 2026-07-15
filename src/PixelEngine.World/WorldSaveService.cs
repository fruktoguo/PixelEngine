using System.Buffers;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using SerializedChunkSnapshot = PixelEngine.Serialization.ChunkSnapshot;

namespace PixelEngine.World;

/// <summary>
/// 显式整世界存档 / 读档服务。live world 只在 capture/apply 安全点访问；编码、磁盘 I/O、
/// 解码与 material remap 可通过游离的 <see cref="WorldSaveSnapshot" /> 在后台完成。
/// </summary>
/// <param name="chunkCodec">chunk blob 编解码器；null 时使用默认实现。</param>
/// <param name="manifestCodec">world manifest 编解码器；null 时使用默认实现。</param>
public sealed class WorldSaveService(ChunkCodec? chunkCodec = null, ManifestCodec? manifestCodec = null)
{
    private const string ManifestFileName = "manifest.bin";
    private const long MaximumManifestBytes = 16L * 1024 * 1024;
    private const int MaximumSnapshotChunks = 65_536;

    private readonly ChunkCodec _chunkCodec = chunkCodec ?? new ChunkCodec();
    private readonly ManifestCodec _manifestCodec = manifestCodec ?? new ManifestCodec();

    /// <summary>
    /// 保存当前 resident chunks 与全局态到 savePath。兼容同步调用方；新异步宿主应分别调用
    /// <see cref="CaptureSnapshot" /> 与 <see cref="WriteSnapshot" />。
    /// </summary>
    public WorldSaveWriteResult SaveAll(
        WorldSaveContext world,
        IWorldStateSnapshotSource stateSource,
        string savePath)
    {
        WorldSaveSnapshot snapshot = CaptureSnapshot(world, stateSource);
        WorldSaveWriteResult result = WriteSnapshot(snapshot, savePath, CancellationToken.None);
        _ = MarkSnapshotPersisted(world.Residency, snapshot);
        return result;
    }

    /// <summary>把成功持久化快照覆盖的 live chunk 标记为无需再次流式写回。</summary>
    /// <param name="residency">当前 live world 的驻留元数据。</param>
    /// <param name="snapshot">已确认成功发布的完整快照。</param>
    /// <returns>至少一个 chunk 的 dirty 状态发生变化时返回 <see langword="true" />。</returns>
    public static bool MarkSnapshotPersisted(
        ResidencyTable residency,
        WorldSaveSnapshot snapshot)
    {
        return UpdateSnapshotDirtyState(residency, snapshot, restoreBeforeImage: false);
    }

    /// <summary>恢复快照捕获时每个 chunk 的流式 dirty before-image，供失败回滚与 Undo。</summary>
    /// <param name="residency">当前 live world 的驻留元数据。</param>
    /// <param name="snapshot">保存前捕获的完整快照。</param>
    /// <returns>至少一个 chunk 的 dirty 状态发生变化时返回 <see langword="true" />。</returns>
    public static bool RestoreSnapshotPersistenceState(
        ResidencyTable residency,
        WorldSaveSnapshot snapshot)
    {
        return UpdateSnapshotDirtyState(residency, snapshot, restoreBeforeImage: true);
    }

    /// <summary>
    /// 在相位 2 或暂停点深拷贝完整 live world；返回值不再引用任何权威可变对象。
    /// </summary>
    /// <param name="world">live world 一致快照上下文。</param>
    /// <param name="stateSource">粒子与刚体状态源。</param>
    /// <param name="currentParity">当前 CA parity 位。</param>
    /// <param name="cancellationToken">在 chunk 间响应的取消令牌。</param>
    /// <returns>可交给后台线程的完整深快照。</returns>
    public WorldSaveSnapshot CaptureSnapshot(
        WorldSaveContext world,
        IWorldStateSnapshotSource stateSource,
        byte currentParity = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stateSource);
        if (!world.IsFrameBoundary)
        {
            throw new InvalidOperationException("WorldSaveService 只能在相位 2 或专门暂停点读取一致快照。");
        }

        Chunk[] liveChunks = world.Chunks.ResidentChunks.ToArray();
        if (liveChunks.Length > MaximumSnapshotChunks)
        {
            throw new InvalidOperationException(
                $"World snapshot chunk 数超过 {MaximumSnapshotChunks} 上限。");
        }

        WorldSnapshotChunk[] chunks = new WorldSnapshotChunk[liveChunks.Length];
        ChunkCoord[] chunkIndex = new ChunkCoord[liveChunks.Length];
        for (int i = 0; i < liveChunks.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Chunk live = liveChunks[i];
            Half[] temperature = new Half[TemperatureField.BlockArea];
            world.Temperature.ExportBlock(live.Coord, temperature);
            ChunkResidencyInfo residency = world.Residency.TryGetInfo(
                live.Coord,
                out ChunkResidencyInfo existing)
                ? existing
                : new ChunkResidencyInfo(
                    ChunkResidencyState.Cached,
                    0,
                    ChunkMemoryBudget.EstimatedResidentChunkBytes,
                    DirtySinceLoad: true);
            DirtyRect[] incoming = new DirtyRect[live.IncomingDirtySlotCount];
            for (int slot = 0; slot < incoming.Length; slot++)
            {
                incoming[slot] = live.GetIncomingDirty(slot);
            }

            chunks[i] = new WorldSnapshotChunk(
                live.Coord,
                [.. live.Material],
                [.. live.Flags],
                [.. live.Lifetime],
                [.. live.Damage],
                live.CurrentDirty,
                live.WorkingDirty,
                incoming,
                live.Parity,
                temperature,
                residency);
            chunkIndex[i] = live.Coord;
        }

        cancellationToken.ThrowIfCancellationRequested();
        int particleCount = stateSource.FreeParticleCount;
        int bodyCount = stateSource.RigidBodyCount;
        ArgumentOutOfRangeException.ThrowIfNegative(particleCount);
        ArgumentOutOfRangeException.ThrowIfNegative(bodyCount);
        FreeParticleSnapshot[] particles = new FreeParticleSnapshot[particleCount];
        RigidBodySnapshot[] bodies = new RigidBodySnapshot[bodyCount];
        stateSource.CopyFreeParticles(particles);
        stateSource.CopyRigidBodies(bodies);

        WorldManifest manifest = new(
            SaveFormatVersions.WorldManifest,
            world.WorldSeed,
            world.GameTimeTicks,
            world.PlayerStateBlob.Span,
            new MaterialNameTable(world.Materials.BuildIdNameTable()),
            particles,
            bodies,
            chunkIndex);
        return new WorldSaveSnapshot(manifest, chunks, currentParity, materialFallbackHitCount: 0);
    }

    /// <summary>
    /// 把游离 world 快照编码并写入目录；不得传入 live world 对象，可在线程池执行。
    /// </summary>
    /// <param name="snapshot">尚未被 apply 消费的快照。</param>
    /// <param name="savePath">目标世界目录。</param>
    /// <param name="cancellationToken">在 chunk 与发布 manifest 前响应的取消令牌。</param>
    public WorldSaveWriteResult WriteSnapshot(
        WorldSaveSnapshot snapshot,
        string savePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(savePath));
        string? parent = Path.GetDirectoryName(root);
        string targetName = Path.GetFileName(root);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(targetName))
        {
            throw new ArgumentException("World save 目标必须是可替换的子目录。", nameof(savePath));
        }

        if (File.Exists(root))
        {
            throw new IOException($"World save 目标是文件而不是目录：{root}");
        }

        EnsurePathHasNoReparsePoint(parent);
        _ = Directory.CreateDirectory(parent);
        EnsurePathHasNoReparsePoint(parent);
        EnsurePathHasNoReparsePoint(root);
        string journalRoot = Path.Combine(
            parent,
            $".{targetName}.{Guid.NewGuid():N}.world-save-journal");
        string stagingPath = Path.Combine(journalRoot, "after");
        string beforePath = Path.Combine(journalRoot, "before");
        _ = Directory.CreateDirectory(journalRoot);
        bool preserveJournal = false;
        try
        {
            WriteSnapshotContents(snapshot, stagingPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(root))
            {
                throw new IOException($"World save 目标在发布前变成文件：{root}");
            }

            if (Directory.Exists(root))
            {
                EnsurePathHasNoReparsePoint(root);
                Directory.Move(root, beforePath);
                try
                {
                    Directory.Move(stagingPath, root);
                }
                catch (Exception operationException)
                {
                    try
                    {
                        Directory.Move(beforePath, root);
                    }
                    catch (Exception rollbackException)
                    {
                        preserveJournal = true;
                        throw new AggregateException(
                            "World save 目录发布失败，且 before-image 回滚失败。",
                            operationException,
                            rollbackException);
                    }

                    throw;
                }
            }
            else
            {
                Directory.Move(stagingPath, root);
            }

            Exception? cleanupFailure = TryDeleteDirectory(journalRoot);
            return cleanupFailure is null
                ? new WorldSaveWriteResult(root, retainedJournalPath: null, cleanupError: null)
                : new WorldSaveWriteResult(root, journalRoot, cleanupFailure.Message);
        }
        catch (Exception operationException) when (!preserveJournal)
        {
            Exception? cleanupFailure = TryDeleteDirectory(journalRoot);
            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    "World save 失败，且 preparation journal 清理失败。",
                    operationException,
                    cleanupFailure);
            }

            throw;
        }
    }

    private void WriteSnapshotContents(
        WorldSaveSnapshot snapshot,
        string root,
        CancellationToken cancellationToken)
    {
        EnsurePathHasNoReparsePoint(root);
        _ = Directory.CreateDirectory(root);
        EnsurePathHasNoReparsePoint(root);

        RegionFileStore chunkStore = new(root);
        ArrayBufferWriter<byte> chunkBuffer = new();
        foreach (WorldSnapshotChunk item in snapshot.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunkBuffer.Clear();
            _chunkCodec.Encode(
                new SerializedChunkSnapshot(
                    item.Coord,
                    item.Material,
                    item.Flags,
                    item.Lifetime,
                    item.Damage,
                    item.Temperature),
                chunkBuffer);
            chunkStore.Write(item.Coord, chunkBuffer.WrittenSpan);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ArrayBufferWriter<byte> manifestBuffer = new();
        _manifestCodec.Encode(snapshot.Manifest, manifestBuffer);
        WriteAtomic(Path.Combine(root, ManifestFileName), manifestBuffer.WrittenSpan);
    }

    /// <summary>
    /// 在后台读取、完整解码并按冻结的目标 material name 表重映射一个 world 存档。
    /// cell parity 由存档 tick 推导，确保应用后的下一次 CA step 可检视所有载入 cell。
    /// </summary>
    /// <param name="savePath">包含 manifest.bin 与 regions 的世界目录。</param>
    /// <param name="currentMaterials">safe phase 深拷贝的目标 runtime material name 表。</param>
    /// <param name="fallbackMaterialId">缺失 material name 使用的目标 id。</param>
    /// <param name="cancellationToken">在文件与 chunk 间响应的取消令牌。</param>
    /// <returns>已完全解码、可在安全点一次性应用的游离快照。</returns>
    public WorldSaveSnapshot ReadSnapshot(
        string savePath,
        MaterialNameTable currentMaterials,
        ushort fallbackMaterialId,
        CancellationToken cancellationToken = default)
    {
        return ReadSnapshotCore(
            savePath,
            currentMaterials,
            fallbackMaterialId,
            currentParityBit: null,
            cancellationToken);
    }

    /// <summary>
    /// 从 savePath 读取整世界存档并重建 resident chunks 与全局态。
    /// 所有磁盘内容会先完整解码，缺失或损坏数据不会先行清空 live world。
    /// </summary>
    public WorldLoadResult LoadAll(string savePath, WorldLoadContext world, IWorldStateSnapshotSink stateSink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stateSink);
        WorldSaveSnapshot snapshot = ReadSnapshotCore(
            savePath,
            new MaterialNameTable(world.Materials.BuildIdNameTable()),
            world.FallbackMaterialId,
            world.CurrentParityBit,
            CancellationToken.None);
        return ApplySnapshot(snapshot, world, stateSink);
    }

    /// <summary>
    /// 在 world 结构性安全点一次性应用已完全解码的快照；本方法不执行磁盘 I/O 或解码。
    /// </summary>
    /// <param name="snapshot">目标 world 快照；成功或失败开始应用后均不可重复消费。</param>
    /// <param name="world">live world 写入上下文。</param>
    /// <param name="stateSink">粒子与刚体恢复入口。</param>
    /// <returns>本次读档摘要。</returns>
    public WorldLoadResult ApplySnapshot(
        WorldSaveSnapshot snapshot,
        WorldLoadContext world,
        IWorldStateSnapshotSink stateSink)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stateSink);
        ValidateMaterialNames(snapshot.Manifest.MaterialNames, world.Materials);
        ReadOnlySpan<WorldSnapshotChunk> prepared = snapshot.Chunks;
        Chunk[] chunks = new Chunk[prepared.Length];
        for (int i = 0; i < prepared.Length; i++)
        {
            chunks[i] = RestoreChunk(prepared[i]);
        }

        world.Chunks.Clear();
        world.Residency.Clear();
        world.Temperature.Clear();
        world.Chunks.AddRange(chunks);
        for (int i = 0; i < prepared.Length; i++)
        {
            WorldSnapshotChunk item = prepared[i];
            world.Temperature.ImportBlock(item.Coord, item.Temperature);
            world.Residency.Set(item.Coord, item.Residency);
        }

        stateSink.RestoreFreeParticles(snapshot.Manifest.FreeParticles.Span);
        stateSink.RestoreRigidBodies(snapshot.Manifest.RigidBodies.Span);
        return new WorldLoadResult(
            snapshot.GameTimeTicks,
            snapshot.WorldSeed,
            snapshot.ChunkCount,
            snapshot.MaterialFallbackHitCount);
    }

    private WorldSaveSnapshot ReadSnapshotCore(
        string savePath,
        MaterialNameTable currentMaterials,
        ushort fallbackMaterialId,
        byte? currentParityBit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);
        ArgumentNullException.ThrowIfNull(currentMaterials);
        string root = Path.GetFullPath(savePath);
        EnsurePathHasNoReparsePoint(root);
        string manifestPath = Path.Combine(root, ManifestFileName);
        FileIdentity manifestIdentity = CaptureRegularFileIdentity(manifestPath, MaximumManifestBytes);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(manifestPath, manifestIdentity);
        WorldManifest sourceManifest = _manifestCodec.Decode(manifestBytes);
        if (sourceManifest.ChunkIndex.Length > MaximumSnapshotChunks)
        {
            throw new InvalidDataException(
                $"World manifest chunk 数超过 {MaximumSnapshotChunks} 上限。");
        }

        byte parity = (byte)((currentParityBit ?? CurrentParityFromTick(sourceManifest.GameTimeTicks)) &
            CellFlags.Parity);
        MaterialRemap remap = MaterialRemap.Build(
            sourceManifest.MaterialNames,
            currentMaterials,
            fallbackMaterialId);
        RegionFileStore chunkStore = new(root);
        ReadOnlySpan<ChunkCoord> chunkIndex = sourceManifest.ChunkIndex.Span;
        WorldSnapshotChunk[] chunks = new WorldSnapshotChunk[chunkIndex.Length];
        HashSet<ChunkCoord> unique = [];
        Dictionary<string, FileIdentity> regionIdentities = new(StringComparer.OrdinalIgnoreCase);
        ArrayBufferWriter<byte> chunkBuffer = new();
        for (int i = 0; i < chunkIndex.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChunkCoord coord = chunkIndex[i];
            if (!unique.Add(coord))
            {
                throw new InvalidDataException($"World manifest 包含重复 chunk 坐标：{coord}。");
            }

            string regionPath = Path.GetFullPath(chunkStore.ResolveRegionPath(coord));
            if (!regionIdentities.ContainsKey(regionPath))
            {
                if (!File.Exists(regionPath))
                {
                    throw new InvalidDataException($"存档缺失 chunk blob：{coord}。");
                }

                regionIdentities.Add(regionPath, CaptureRegularFileIdentity(regionPath, long.MaxValue));
            }

            chunkBuffer.Clear();
            if (!chunkStore.TryRead(coord, chunkBuffer))
            {
                throw new InvalidDataException($"存档缺失 chunk blob：{coord}。");
            }

            ushort[] material = GC.AllocateArray<ushort>(PixelEngine.Core.EngineConstants.ChunkArea, pinned: true);
            byte[] flags = GC.AllocateArray<byte>(PixelEngine.Core.EngineConstants.ChunkArea, pinned: true);
            byte[] lifetime = GC.AllocateArray<byte>(PixelEngine.Core.EngineConstants.ChunkArea, pinned: true);
            byte[] damage = GC.AllocateArray<byte>(PixelEngine.Core.EngineConstants.ChunkArea, pinned: true);
            Half[] temperature = new Half[TemperatureField.BlockArea];
            _chunkCodec.Decode(
                chunkBuffer.WrittenSpan,
                new SerializedChunkSnapshot(
                    coord,
                    material,
                    flags,
                    lifetime,
                    damage,
                    temperature),
                parity);
            remap.RemapInPlace(material, damage);
            chunks[i] = new WorldSnapshotChunk(
                coord,
                material,
                flags,
                lifetime,
                damage,
                DirtyRect.Full,
                DirtyRect.Empty,
                new DirtyRect[8],
                parity,
                temperature,
                new ChunkResidencyInfo(
                    ChunkResidencyState.Cached,
                    0,
                    ChunkMemoryBudget.EstimatedResidentChunkBytes,
                    DirtySinceLoad: false));
        }

        foreach (KeyValuePair<string, FileIdentity> region in regionIdentities)
        {
            ValidateIdentity(region.Key, region.Value);
        }

        ValidateIdentity(manifestPath, manifestIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        FreeParticleSnapshot[] particles = RemapParticles(sourceManifest.FreeParticles.Span, remap);
        RigidBodySnapshot[] bodies = RemapBodies(sourceManifest.RigidBodies.Span, remap);
        WorldManifest targetManifest = new(
            SaveFormatVersions.WorldManifest,
            sourceManifest.WorldSeed,
            sourceManifest.GameTimeTicks,
            sourceManifest.PlayerStateBlob.Span,
            currentMaterials,
            particles,
            bodies,
            sourceManifest.ChunkIndex.Span);
        return new WorldSaveSnapshot(
            targetManifest,
            chunks,
            parity,
            remap.FallbackHitCount);
    }

    private static Chunk RestoreChunk(WorldSnapshotChunk source)
    {
        Chunk clone = new(source.Coord);
        source.Material.CopyTo(clone.MaterialBuffer, 0);
        source.Flags.CopyTo(clone.FlagsBuffer, 0);
        source.Lifetime.CopyTo(clone.LifetimeBuffer, 0);
        source.Damage.CopyTo(clone.DamageBuffer, 0);
        clone.SetCurrentDirty(source.CurrentDirty);
        clone.SetWorkingDirty(source.WorkingDirty);
        if (source.IncomingDirty.Length != clone.IncomingDirtySlotCount)
        {
            throw new InvalidDataException("World snapshot incoming dirty slot 数量无效。");
        }

        for (int i = 0; i < source.IncomingDirty.Length; i++)
        {
            clone.MarkIncomingDirty(i, source.IncomingDirty[i]);
        }

        clone.Parity = source.Parity;
        return clone;
    }

    private static FreeParticleSnapshot[] RemapParticles(
        ReadOnlySpan<FreeParticleSnapshot> particles,
        MaterialRemap remap)
    {
        FreeParticleSnapshot[] remapped = new FreeParticleSnapshot[particles.Length];
        for (int i = 0; i < particles.Length; i++)
        {
            remapped[i] = particles[i] with { Material = remap.Map(particles[i].Material) };
        }

        return remapped;
    }

    private static RigidBodySnapshot[] RemapBodies(
        ReadOnlySpan<RigidBodySnapshot> bodies,
        MaterialRemap remap)
    {
        RigidBodySnapshot[] remapped = new RigidBodySnapshot[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            RigidBodySnapshot body = bodies[i];
            ushort[] material = body.Material.ToArray();
            remap.RemapInPlace(material);
            remapped[i] = new RigidBodySnapshot(
                body.Id,
                body.Width,
                body.Height,
                body.BodyLocalMask.Span,
                material,
                body.PosX,
                body.PosY,
                body.RotCos,
                body.RotSin,
                body.LinVelX,
                body.LinVelY,
                body.AngVel,
                body.LocalOriginX,
                body.LocalOriginY);
        }

        return remapped;
    }

    private static void ValidateMaterialNames(MaterialNameTable expected, MaterialTable current)
    {
        MaterialNameTable actual = new(current.BuildIdNameTable());
        ReadOnlySpan<(ushort Id, string Name)> expectedEntries = expected.Entries;
        ReadOnlySpan<(ushort Id, string Name)> actualEntries = actual.Entries;
        if (expectedEntries.Length != actualEntries.Length)
        {
            throw new InvalidOperationException("后台读档期间 runtime material 表发生变化。");
        }

        for (int i = 0; i < expectedEntries.Length; i++)
        {
            if (expectedEntries[i].Id != actualEntries[i].Id ||
                !string.Equals(expectedEntries[i].Name, actualEntries[i].Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("后台读档期间 runtime material 表发生变化。");
            }
        }
    }

    private static bool UpdateSnapshotDirtyState(
        ResidencyTable residency,
        WorldSaveSnapshot snapshot,
        bool restoreBeforeImage)
    {
        ArgumentNullException.ThrowIfNull(residency);
        ArgumentNullException.ThrowIfNull(snapshot);
        bool changed = false;
        foreach (WorldSnapshotChunk item in snapshot.Chunks)
        {
            if (!residency.TryGetInfo(item.Coord, out ChunkResidencyInfo info))
            {
                continue;
            }

            bool target = restoreBeforeImage && item.Residency.DirtySinceLoad;
            if (info.DirtySinceLoad == target)
            {
                continue;
            }

            residency.Set(item.Coord, info with { DirtySinceLoad = target });
            changed = true;
        }

        return changed;
    }

    private static byte CurrentParityFromTick(long gameTimeTicks)
    {
        return (gameTimeTicks & 1L) == 0 ? (byte)0 : CellFlags.Parity;
    }

    private static FileIdentity CaptureRegularFileIdentity(string path, long maximumBytes)
    {
        EnsurePathHasNoReparsePoint(path);
        FileInfo info = new(path);
        return !info.Exists
            ? throw new FileNotFoundException("World save 文件不存在。", path)
            : info.Length is >= 0 && info.Length <= maximumBytes
            ? (info.Attributes & FileAttributes.ReparsePoint) == 0
                ? new FileIdentity(info.Length, info.LastWriteTimeUtc)
                : throw new InvalidDataException($"World save 文件不能是 reparse point：{path}")
            : throw new InvalidDataException(
                $"World save 文件大小无效：{path} ({info.Length} bytes)。");
    }

    private static void ValidateIdentity(string path, FileIdentity expected)
    {
        FileIdentity actual = CaptureRegularFileIdentity(path, long.MaxValue);
        if (actual != expected)
        {
            throw new IOException($"读取 world save 时文件发生变化：{path}");
        }
    }

    private static void EnsurePathHasNoReparsePoint(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"World save 路径包含 reparse point：{current}");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Exception? TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception;
        }
    }

    private readonly record struct FileIdentity(long Length, DateTime LastWriteTimeUtc);
}
