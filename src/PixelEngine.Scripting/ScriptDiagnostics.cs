namespace PixelEngine.Scripting;

internal readonly record struct ScriptExceptionRecord(
    string ScriptType,
    string Callback,
    string ExceptionType,
    string Message,
    long FrameIndex);

internal interface IScriptDiagnosticSink
{
    void ReportScriptException(in ScriptExceptionRecord record);
}
