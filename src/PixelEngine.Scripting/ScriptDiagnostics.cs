namespace PixelEngine.Scripting;

/// <summary>
/// 单次脚本回调异常的结构化记录，供编辑器与日志消费。
/// </summary>
/// <param name="ScriptType">发生异常的脚本类型全名。</param>
/// <param name="Callback">回调名称（如 OnUpdate、Event:...）。</param>
/// <param name="ExceptionType">异常类型全名。</param>
/// <param name="Message">异常消息。</param>
/// <param name="FrameIndex">发生时的帧序号；-1 表示不可用。</param>
internal readonly record struct ScriptExceptionRecord(
    string ScriptType,
    string Callback,
    string ExceptionType,
    string Message,
    long FrameIndex);

/// <summary>
/// 脚本运行时异常上报接口；由 Hosting 或编辑器实现。
/// </summary>
internal interface IScriptDiagnosticSink
{
    /// <summary>
    /// 上报一条脚本异常记录。
    /// </summary>
    void ReportScriptException(in ScriptExceptionRecord record);
}
