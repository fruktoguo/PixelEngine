using System.Globalization;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Cli;

internal enum CliOutputMode
{
    Compact,
    Json,
    Ndjson,
}

internal sealed record CliGlobalOptions(
    string DiscoveryRoot,
    string? InstanceId,
    string? CredentialPath,
    string ClientInstanceId,
    string[] Scopes,
    TimeSpan ConnectTimeout,
    TimeSpan RequestTimeout,
    CliOutputMode OutputMode,
    bool Version,
    bool Help);

internal sealed class CliArguments(IEnumerable<string> arguments)
{
    private readonly List<string> _values = [.. arguments];

    public int Count => _values.Count;

    public string this[int index] => _values[index];

    public bool TakeFlag(string name)
    {
        int index = _values.FindIndex(value => string.Equals(value, name, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        _values.RemoveAt(index);
        return true;
    }

    public string? TakeOption(string name)
    {
        int index = _values.FindIndex(value => string.Equals(value, name, StringComparison.Ordinal));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= _values.Count || _values[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException($"{name} 缺少值。");
        }

        string value = _values[index + 1];
        _values.RemoveRange(index, 2);
        return value;
    }

    public string[] TakeOptions(string name)
    {
        List<string> values = [];
        while (TakeOption(name) is { } value)
        {
            values.Add(value);
        }

        return [.. values];
    }

    public string TakeRequiredPositional(string description)
    {
        if (_values.Count == 0 || _values[0].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException($"缺少 {description}。");
        }

        string value = _values[0];
        _values.RemoveAt(0);
        return value;
    }

    public string? TakeOptionalPositional()
    {
        if (_values.Count == 0 || _values[0].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        string value = _values[0];
        _values.RemoveAt(0);
        return value;
    }

    public void EnsureEmpty()
    {
        if (_values.Count != 0)
        {
            throw new CliUsageException($"未知参数：{string.Join(' ', _values)}");
        }
    }

    public static CliGlobalOptions ParseGlobals(CliArguments arguments)
    {
        bool help = arguments.TakeFlag("--help") || arguments.TakeFlag("-h");
        bool version = arguments.TakeFlag("--version");
        string discoveryRoot = ResolveDiscoveryRoot(arguments.TakeOption("--discovery-root"));
        string? instanceId = arguments.TakeOption("--instance");
        string? credentialPath = arguments.TakeOption("--credential");
        string clientInstanceId = arguments.TakeOption("--client-instance-id") ??
            $"pixelengine-cli-{Guid.NewGuid():N}";
        string[] scopes = ParseScopes(arguments.TakeOption("--scopes"));
        TimeSpan connectTimeout = ParseDuration(
            arguments.TakeOption("--connect-timeout"),
            TimeSpan.FromSeconds(10),
            "--connect-timeout");
        TimeSpan requestTimeout = ParseDuration(
            arguments.TakeOption("--timeout"),
            TimeSpan.FromSeconds(30),
            "--timeout");
        CliOutputMode outputMode = ParseOutput(arguments.TakeOption("--output"));
        return new CliGlobalOptions(
            discoveryRoot,
            instanceId,
            credentialPath is null ? null : Path.GetFullPath(credentialPath),
            clientInstanceId,
            scopes,
            connectTimeout,
            requestTimeout,
            outputMode,
            version,
            help);
    }

    public static TimeSpan ParseDuration(string? value, TimeSpan defaultValue, string option)
    {
        return value is null
            ? defaultValue
            : double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds) &&
            double.IsFinite(seconds) && seconds is > 0 and <= 86400
            ? TimeSpan.FromSeconds(seconds)
            : throw new CliUsageException($"{option} 必须是大于 0 且不超过 86400 的秒数。");
    }

    private static CliOutputMode ParseOutput(string? value)
    {
        return value switch
        {
            null or "compact" => CliOutputMode.Compact,
            "json" => CliOutputMode.Json,
            "ndjson" => CliOutputMode.Ndjson,
            _ => throw new CliUsageException("--output 只支持 compact、json 或 ndjson。"),
        };
    }

    private static string[] ParseScopes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [AutomationScopes.EditorRead];
        }

        string[] scopes =
        [
            .. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        return scopes.Length > 0 && scopes.All(scope => AutomationScopes.All.Contains(scope, StringComparer.Ordinal))
            ? scopes
            : throw new CliUsageException("--scopes 包含未知或空 scope。");
    }

    private static string ResolveDiscoveryRoot(string? configured)
    {
        string? root = string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable("PIXELENGINE_AUTOMATION_DISCOVERY_ROOT")
            : configured;
        if (string.IsNullOrWhiteSpace(root))
        {
            string? userData = Environment.GetEnvironmentVariable("PIXELENGINE_EDITOR_USER_DATA_DIR");
            root = Path.Combine(
                string.IsNullOrWhiteSpace(userData)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "PixelEngine")
                    : userData,
                "automation");
        }

        return Path.GetFullPath(root);
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
