using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// 维护 world cell 到刚体 local 像素的 stamp 映射。
/// </summary>
public sealed class RigidStampRegistry : IRigidCellOwnershipLookup
{
    private readonly Dictionary<long, RigidStamp> _stamps = [];

    /// <summary>清空本帧 registry。</summary>
    public void Clear()
    {
        _stamps.Clear();
    }

    /// <summary>
    /// 登记一个 world cell stamp。
    /// </summary>
    public void Register(int worldX, int worldY, in RigidStamp stamp)
    {
        _stamps[Pack(worldX, worldY)] = stamp;
    }

    /// <summary>
    /// 查询 world cell stamp。
    /// </summary>
    public bool TryGet(int worldX, int worldY, out RigidStamp stamp)
    {
        return _stamps.TryGetValue(Pack(worldX, worldY), out stamp);
    }

    /// <summary>
    /// 移除一个 world cell stamp。
    /// </summary>
    /// <param name="worldX">world X。</param>
    /// <param name="worldY">world Y。</param>
    /// <returns>存在并移除时返回 true。</returns>
    public bool Remove(int worldX, int worldY)
    {
        return _stamps.Remove(Pack(worldX, worldY));
    }

    /// <inheritdoc />
    public bool TryGetBodyAtCell(int worldX, int worldY, out int bodyKey)
    {
        if (TryGet(worldX, worldY, out RigidStamp stamp))
        {
            bodyKey = stamp.BodyKey;
            return true;
        }

        bodyKey = 0;
        return false;
    }

    private static long Pack(int x, int y)
    {
        return ((long)x << 32) ^ (uint)y;
    }
}
