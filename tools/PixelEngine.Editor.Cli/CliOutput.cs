using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Cli;

internal sealed class CliOutput(CliOutputMode mode)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public CliOutputMode Mode { get; } = mode;

    public void WriteInvocation(JsonElement? payload, AutomationRevisionSnapshot? revision)
    {
        if (Mode == CliOutputMode.Compact)
        {
            Console.WriteLine(payload?.GetRawText() ?? "null");
            if (revision is not null)
            {
                Console.Error.WriteLine($"revision={revision.GlobalRevision}");
            }

            return;
        }

        WriteJsonObject(writer =>
        {
            writer.WritePropertyName("payload");
            if (payload.HasValue)
            {
                payload.Value.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WritePropertyName("revision");
            if (revision is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(
                    writer,
                    revision,
                    AutomationJsonContext.Default.AutomationRevisionSnapshot);
            }
        });
    }

    public void WriteArtifact(
        AutomationArtifactReference artifact,
        AutomationArtifactVerification? verification = null)
    {
        if (Mode == CliOutputMode.Compact)
        {
            Console.WriteLine($"{artifact.Path}\t{artifact.Sha256}\t{artifact.ByteLength}");
            if (verification is not null)
            {
                Console.Error.WriteLine($"verified={verification.Verified.ToString().ToLowerInvariant()}");
            }

            return;
        }

        WriteJsonObject(writer =>
        {
            writer.WritePropertyName("artifact");
            JsonSerializer.Serialize(
                writer,
                artifact,
                AutomationJsonContext.Default.AutomationArtifactReference);
            writer.WritePropertyName("verification");
            if (verification is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, verification, JsonOptions);
            }
        });
    }

    public void WriteDiscovery(AutomationDiscoverySnapshot snapshot)
    {
        if (Mode == CliOutputMode.Compact)
        {
            for (int i = 0; i < snapshot.Instances.Length; i++)
            {
                AutomationInstanceDescriptor item = snapshot.Instances[i].Descriptor;
                Console.WriteLine(
                    $"{item.InstanceId}\t{item.ProcessId}\t{item.Project?.Name ?? "-"}\t{item.Endpoint.Kind}");
            }

            for (int i = 0; i < snapshot.Diagnostics.Length; i++)
            {
                Console.Error.WriteLine(
                    $"{snapshot.Diagnostics[i].Code}\t{snapshot.Diagnostics[i].Path}\t{snapshot.Diagnostics[i].Message}");
            }

            return;
        }

        if (Mode == CliOutputMode.Ndjson)
        {
            for (int i = 0; i < snapshot.Instances.Length; i++)
            {
                WriteSerialized(new { kind = "instance", value = snapshot.Instances[i] });
            }

            for (int i = 0; i < snapshot.Diagnostics.Length; i++)
            {
                WriteSerialized(new { kind = "diagnostic", value = snapshot.Diagnostics[i] });
            }

            return;
        }

        WriteSerialized(snapshot);
    }

    public void WriteCapabilities(AutomationCapabilityCatalog catalog)
    {
        if (Mode == CliOutputMode.Compact)
        {
            for (int i = 0; i < catalog.Items.Length; i++)
            {
                AutomationCapabilityDescriptor item = catalog.Items[i];
                Console.WriteLine(
                    $"{item.Id}\t{item.Domain}\t{item.OperationKind}\t{string.Join(',', item.RequiredScopes)}");
            }

            Console.Error.WriteLine($"capabilityDigest={catalog.CapabilityDigest}");
            return;
        }

        WriteJsonObject(writer =>
        {
            writer.WriteString("capabilityDigest", catalog.CapabilityDigest);
            writer.WritePropertyName("items");
            JsonSerializer.Serialize(
                writer,
                catalog.Items,
                AutomationJsonContext.Default.AutomationCapabilityDescriptorArray);
            writer.WritePropertyName("revision");
            if (catalog.Revision is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(
                    writer,
                    catalog.Revision,
                    AutomationJsonContext.Default.AutomationRevisionSnapshot);
            }
        });
    }

    public void WriteCapability(AutomationCapabilityDescriptor descriptor)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(
            descriptor,
            AutomationJsonContext.Default.AutomationCapabilityDescriptor);
        WriteInvocation(payload, revision: null);
    }

    public void WriteCapabilityMatrix(
        AutomationCapabilityMatrixSnapshot matrix,
        AutomationRevisionSnapshot? revision)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (Mode == CliOutputMode.Compact)
        {
            for (int i = 0; i < matrix.UiCommands.Length; i++)
            {
                AutomationUiCommandDescriptor command = matrix.UiCommands[i];
                Console.WriteLine(
                    $"{command.Id}\t{command.SurfaceId}\t{string.Join(',', command.CapabilityIds)}\t{command.HandlerId}");
            }

            Console.Error.WriteLine(
                $"matrixDigest={matrix.MatrixDigest} capabilityDigest={matrix.CapabilityDigest} " +
                $"uiCommandDigest={matrix.UiCommandDigest} capabilities={matrix.Capabilities.Length} " +
                $"uiCommands={matrix.UiCommands.Length} revision={revision?.GlobalRevision.ToString() ?? "-"}");
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(
            matrix,
            AutomationJsonContext.Default.AutomationCapabilityMatrixSnapshot));
    }

    public void WriteEvent(AutomationEventRecord record)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(
            record,
            AutomationJsonContext.Default.AutomationEventRecord);
        Console.WriteLine(Mode == CliOutputMode.Compact
            ? $"{record.Sequence}\t{record.EventType}\t{payload.GetRawText()}"
            : payload.GetRawText());
    }

    public void WriteResumeState(AutomationEventSubscription subscription)
    {
        if (Mode == CliOutputMode.Compact)
        {
            Console.Error.WriteLine(
                $"subscription={subscription.Info.SubscriptionId} resumeToken={subscription.ResumeState.ResumeToken} ack={subscription.ResumeState.AcknowledgedSequence}");
            return;
        }

        WriteSerialized(new { kind = "subscription", value = subscription });
    }

    public static void WriteError(string code, string message, int exitCode)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(
            new { error = new { code, message }, exitCode },
            JsonOptions));
    }

    private void WriteSerialized<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void WriteJsonObject(Action<Utf8JsonWriter> writeProperties)
    {
        using Utf8JsonWriter writer = new(Console.OpenStandardOutput(), new JsonWriterOptions
        {
            Indented = false,
        });
        writer.WriteStartObject();
        writeProperties(writer);
        writer.WriteEndObject();
        writer.Flush();
        Console.WriteLine();
    }
}
