namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 提供一个由玩法、工具或宿主发布的通用命名 metric 通道。
/// </summary>
/// <remarks>
/// 通道只承载不解释语义的 label/value；Core 与 Editor 不依赖任何具体 Demo 名称。
/// 发布与读取使用版本号保护，读取稳态不创建托管对象，适合在帧边界交换诊断快照。
/// </remarks>
public sealed class CustomMetricChannel
{
    private string _name = string.Empty;
    private long _value;
    private long _version;
    private int _writerGate;

    /// <summary>
    /// 发布一个命名 metric 的当前值。
    /// </summary>
    /// <param name="name">由发布方定义的稳定 label。</param>
    /// <param name="value">label 对应的整数值。</param>
    public void Publish(string name, long value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Custom metric name must be non-empty.", nameof(name));
        }

        SpinWait spinner = default;
        while (Interlocked.CompareExchange(ref _writerGate, 1, 0) != 0)
        {
            spinner.SpinOnce();
        }

        try
        {
            // 版本号使用奇数表示写入中，避免 HUD 读到上一帧 label 与 value 的混合快照。
            _ = Interlocked.Increment(ref _version);
            Volatile.Write(ref _name, name);
            Volatile.Write(ref _value, value);
            _ = Interlocked.Increment(ref _version);
        }
        finally
        {
            Volatile.Write(ref _writerGate, 0);
        }
    }

    /// <summary>
    /// 读取当前 label/value 的一致快照。
    /// </summary>
    /// <param name="name">当前 label；尚未发布时为空字符串。</param>
    /// <param name="value">当前数值；尚未发布时为零。</param>
    public void Read(out string name, out long value)
    {
        SpinWait spinner = default;
        while (true)
        {
            long before = Volatile.Read(ref _version);
            if ((before & 1) != 0)
            {
                spinner.SpinOnce();
                continue;
            }

            string currentName = Volatile.Read(ref _name);
            long currentValue = Volatile.Read(ref _value);
            long after = Volatile.Read(ref _version);
            if (before == after)
            {
                name = currentName;
                value = currentValue;
                return;
            }

            spinner.SpinOnce();
        }
    }

    /// <summary>
    /// 清除当前 metric，使读取回到空 label/零值初态。
    /// </summary>
    public void Clear()
    {
        SpinWait spinner = default;
        while (Interlocked.CompareExchange(ref _writerGate, 1, 0) != 0)
        {
            spinner.SpinOnce();
        }

        try
        {
            _ = Interlocked.Increment(ref _version);
            Volatile.Write(ref _name, string.Empty);
            Volatile.Write(ref _value, 0);
            _ = Interlocked.Increment(ref _version);
        }
        finally
        {
            Volatile.Write(ref _writerGate, 0);
        }
    }
}
