// FastLZ decompression port based on ariya/FastLZ fastlz.c.
// Copyright (C) 2005-2020 Ariya Hidayat. Distributed under the MIT license.
using System;
using System.IO;

namespace PixelEngine.Tools.Noita;

public static class NoitaFastLzDecoder
{
    private const int MaximumLevel2Distance = 8_191;

    public static byte[] Decode(byte[] sourceBytes, int decodedLength)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ReadOnlySpan<byte> source = sourceBytes;
        if (decodedLength < 0)
        {
            throw new InvalidDataException("FastLZ decoded length 不能为负数。");
        }

        if (source.Length == decodedLength)
        {
            return source.ToArray();
        }

        if (source.IsEmpty)
        {
            throw new InvalidDataException("FastLZ payload 不能为空。");
        }

        byte[] destination = new byte[decodedLength];
        int input = 0;
        int output = 0;
        int inputBound = source.Length - 2;
        int level = (source[input] >> 5) + 1;
        if (level is not (1 or 2))
        {
            throw new InvalidDataException($"未知 FastLZ level {level}。");
        }

        int control = source[input++] & 31;
        while (true)
        {
            if (control >= 32)
            {
                int length = (control >> 5) - 1;
                int offset = (control & 31) << 8;
                int reference = output - offset - 1;
                if (length == 6)
                {
                    int code;
                    do
                    {
                        Require(input <= inputBound, "FastLZ match length 越过输入。");
                        code = source[input++];
                        length += code;
                    }
                    while (level == 2 && code == byte.MaxValue);
                }

                Require(input < source.Length, "FastLZ match distance 缺失。");
                int distanceCode = source[input++];
                reference -= distanceCode;
                length += 3;
                if (level == 2 && distanceCode == byte.MaxValue && offset == (31 << 8))
                {
                    Require(input < inputBound, "FastLZ far distance 越过输入。");
                    offset = (source[input++] << 8) + source[input++];
                    reference = output - offset - MaximumLevel2Distance - 1;
                }

                Require(reference >= 0, "FastLZ match 指向输出起点之前。");
                Require(output + length <= destination.Length, "FastLZ match 越过输出容量。");
                for (int i = 0; i < length; i++)
                {
                    destination[output + i] = destination[reference + i];
                }

                output += length;
            }
            else
            {
                int length = control + 1;
                Require(input + length <= source.Length, "FastLZ literal 越过输入。");
                Require(output + length <= destination.Length, "FastLZ literal 越过输出容量。");
                source.Slice(input, length).CopyTo(destination.AsSpan(output));
                input += length;
                output += length;
            }

            if (level == 1 ? input > inputBound : input >= source.Length)
            {
                break;
            }

            control = source[input++];
        }

        Require(output == decodedLength, $"FastLZ 解码长度不匹配：{output}/{decodedLength}。");
        return destination;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
