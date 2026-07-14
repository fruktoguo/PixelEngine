namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// frame 或 wire contract 无效时抛出的协议异常。
/// </summary>
public class AutomationProtocolException : Exception
{
    /// <summary>
    /// 创建协议异常。
    /// </summary>
    /// <param name="message">诊断文本。</param>
    public AutomationProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 创建带内部异常的协议异常。
    /// </summary>
    /// <param name="message">诊断文本。</param>
    /// <param name="innerException">内部异常。</param>
    public AutomationProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>frame 配置或实际 JSON payload 超出有界控制面大小时抛出的协议异常。</summary>
public sealed class AutomationFrameSizeException(string message) : AutomationProtocolException(message)
{
}
