using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// automation wire/discovery 的 source-generated JSON metadata。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AutomationEnvelope))]
[JsonSerializable(typeof(AutomationError))]
[JsonSerializable(typeof(AutomationHelloRequest))]
[JsonSerializable(typeof(AutomationHelloChallenge))]
[JsonSerializable(typeof(AutomationAuthenticateRequest))]
[JsonSerializable(typeof(AutomationSessionInfo))]
[JsonSerializable(typeof(AutomationCancelRequest))]
[JsonSerializable(typeof(AutomationInstanceDescriptor))]
[JsonSerializable(typeof(AutomationEndpointDescriptor))]
[JsonSerializable(typeof(AutomationProjectSummary))]
[JsonSerializable(typeof(AutomationPingResponse))]
[JsonSerializable(typeof(AutomationProtocolVersion[]))]
[JsonSerializable(typeof(string[]))]
public sealed partial class AutomationJsonContext : JsonSerializerContext;
