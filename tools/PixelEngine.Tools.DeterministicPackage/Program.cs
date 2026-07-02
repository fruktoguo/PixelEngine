using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

Options options = Options.Parse(args);
DeterministicPackageWriter.Write(options);

internal sealed record Options(
    string Source,
    string Output,
    string RootName,
    string Format,
    DateTimeOffset Timestamp)
{
    public static Options Parse(string[] args)
    {
        string source = "";
        string output = "";
        string rootName = "";
        string format = "";
        DateTimeOffset timestamp = ReadTimestamp();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string value = i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {arg}.");
            switch (arg)
            {
                case "--source":
                    source = value;
                    break;
                case "--output":
                    output = value;
                    break;
                case "--root-name":
                    rootName = value;
                    break;
                case "--format":
                    format = value;
                    break;
                case "--timestamp":
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(value, CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {source}");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("--output is required.");
        }

        if (string.IsNullOrWhiteSpace(rootName))
        {
            throw new ArgumentException("--root-name is required.");
        }

        _ = format switch
        {
            "zip" or "tar.gz" => true,
            _ => throw new ArgumentException("--format must be zip or tar.gz."),
        };

        return new Options(Path.GetFullPath(source), Path.GetFullPath(output), rootName, format, timestamp);
    }

    private static DateTimeOffset ReadTimestamp()
    {
        string? epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (long.TryParse(epoch, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return DateTimeOffset.FromUnixTimeSeconds(315532800); // 1980-01-01 for zip compatibility.
    }
}

internal static class DeterministicPackageWriter
{
    private const int DirectoryMode = 0x1ED; // 0755
    private const int FileMode = 0x1A4; // 0644
    private const int ExecutableMode = 0x1ED; // 0755

    public static void Write(Options options)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
        if (File.Exists(options.Output))
        {
            File.Delete(options.Output);
        }

        Entry[] entries = [.. BuildEntries(options.Source, options.RootName)];
        if (options.Format == "zip")
        {
            WriteZip(options.Output, entries, options.Timestamp);
        }
        else
        {
            WriteTarGz(options.Output, entries, options.Timestamp);
        }

        Console.WriteLine($"Deterministic package written: {options.Output}");
        Console.WriteLine($"SHA256: {Sha256(options.Output)}");
    }

    private static IEnumerable<Entry> BuildEntries(string source, string rootName)
    {
        yield return new Entry(rootName + "/", "", true, DirectoryMode);

        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).OrderBy(NormalizedRelativePath, StringComparer.Ordinal))
        {
            string relative = NormalizedRelativePath(directory);
            yield return new Entry($"{rootName}/{relative}/", directory, true, DirectoryMode);
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).OrderBy(NormalizedRelativePath, StringComparer.Ordinal))
        {
            string relative = NormalizedRelativePath(file);
            int mode = IsExecutable(relative) ? ExecutableMode : FileMode;
            yield return new Entry($"{rootName}/{relative}", file, false, mode);
        }

        string NormalizedRelativePath(string path)
        {
            return Path.GetRelativePath(source, path).Replace('\\', '/');
        }
    }

    private static bool IsExecutable(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        string extension = Path.GetExtension(relativePath);
        return extension is ".sh" or ".so" or ".dylib" ||
               fileName is "PixelEngine.Demo" or "PixelEngine.Demo.exe";
    }

    private static void WriteZip(string output, IReadOnlyList<Entry> entries, DateTimeOffset timestamp)
    {
        using FileStream stream = File.Create(output);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (Entry item in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(item.Name, CompressionLevel.Optimal);
            entry.LastWriteTime = timestamp;
            entry.ExternalAttributes = (item.Mode & 0xFFFF) << 16;
            if (!item.IsDirectory)
            {
                using Stream destination = entry.Open();
                using FileStream source = File.OpenRead(item.SourcePath);
                source.CopyTo(destination);
            }
        }
    }

    private static void WriteTarGz(string output, IReadOnlyList<Entry> entries, DateTimeOffset timestamp)
    {
        using FileStream file = File.Create(output);
        using GZipStream gzip = new(file, CompressionLevel.Optimal, leaveOpen: false);
        foreach (Entry item in entries)
        {
            WriteTarEntry(gzip, item, timestamp);
        }

        gzip.Write(new byte[1024]);
    }

    private static void WriteTarEntry(Stream stream, Entry item, DateTimeOffset timestamp)
    {
        byte[] header = new byte[512];
        WriteAscii(header, 0, 100, item.Name);
        WriteOctal(header, 100, 8, item.Mode);
        WriteOctal(header, 108, 8, 0);
        WriteOctal(header, 116, 8, 0);
        long size = item.IsDirectory ? 0 : new FileInfo(item.SourcePath).Length;
        WriteOctal(header, 124, 12, size);
        WriteOctal(header, 136, 12, timestamp.ToUnixTimeSeconds());
        for (int i = 148; i < 156; i++)
        {
            header[i] = 0x20;
        }

        header[156] = item.IsDirectory ? (byte)'5' : (byte)'0';
        WriteAscii(header, 257, 6, "ustar");
        WriteAscii(header, 263, 2, "00");
        WriteAscii(header, 265, 32, "root");
        WriteAscii(header, 297, 32, "root");
        WriteOctal(header, 329, 8, 0);
        WriteOctal(header, 337, 8, 0);

        int checksum = header.Sum(b => b);
        WriteChecksum(header, checksum);
        stream.Write(header);

        if (!item.IsDirectory)
        {
            using FileStream source = File.OpenRead(item.SourcePath);
            source.CopyTo(stream);
            int padding = (int)((512 - (size % 512)) % 512);
            if (padding > 0)
            {
                stream.Write(new byte[padding]);
            }
        }
    }

    private static void WriteAscii(byte[] buffer, int offset, int length, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > length)
        {
            throw new InvalidOperationException($"tar entry value is too long: {value}");
        }

        bytes.CopyTo(buffer.AsSpan(offset, bytes.Length));
    }

    private static void WriteOctal(byte[] buffer, int offset, int length, long value)
    {
        string text = Convert.ToString(value, 8);
        if (text.Length > length - 1)
        {
            throw new InvalidOperationException($"tar octal value is too large: {value}");
        }

        string padded = text.PadLeft(length - 1, '0');
        WriteAscii(buffer, offset, length - 1, padded);
        buffer[offset + length - 1] = 0;
    }

    private static void WriteChecksum(byte[] buffer, int checksum)
    {
        string text = Convert.ToString(checksum, 8).PadLeft(6, '0');
        WriteAscii(buffer, 148, 6, text);
        buffer[154] = 0;
        buffer[155] = 0x20;
    }

    private static string Sha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}

internal readonly record struct Entry(string Name, string SourcePath, bool IsDirectory, int Mode);
