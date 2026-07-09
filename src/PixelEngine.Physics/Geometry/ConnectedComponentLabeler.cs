using System.Buffers;
using PixelEngine.Core.Mathematics;

namespace PixelEngine.Physics.Geometry;

/// <summary>
/// 使用显式栈 flood fill 标记二值像素 mask 的连通分量。
/// </summary>
public static class ConnectedComponentLabeler
{
    /// <summary>
    /// 标记连通分量。
    /// </summary>
    /// <param name="solidMask">二值固体 mask，非 0 表示固体。</param>
    /// <param name="width">mask 宽度。</param>
    /// <param name="height">mask 高度。</param>
    /// <param name="labels">输出标签缓冲，长度至少为 <c>width * height</c>。</param>
    /// <param name="components">输出分量摘要缓冲。</param>
    /// <param name="connectivity">连通性。</param>
    /// <param name="fragmentPixelThreshold">碎片像素阈值，小于该值标记为碎片。</param>
    /// <returns>写入的分量数量。</returns>
    public static int Label(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        Span<int> labels,
        Span<ConnectedComponent> components,
        Connectivity connectivity = Connectivity.Four,
        int fragmentPixelThreshold = 0)
    {
        ValidateArguments(solidMask, width, height, labels, components, fragmentPixelThreshold);

        int area = width * height;
        labels[..area].Clear();
        int[] rentedStack = ArrayPool<int>.Shared.Rent(area);

        try
        {
            Span<int> stack = rentedStack.AsSpan(0, area);
            int componentCount = 0;

            // 线性扫描未标记固体种子，每个种子启动一次 flood fill。
            for (int index = 0; index < area; index++)
            {
                if (solidMask[index] == 0 || labels[index] != 0)
                {
                    continue;
                }

                if (componentCount >= components.Length)
                {
                    throw new ArgumentException("components 缓冲不足。", nameof(components));
                }

                componentCount++;
                components[componentCount - 1] = FloodFill(
                    solidMask,
                    width,
                    height,
                    labels,
                    stack,
                    index,
                    componentCount,
                    connectivity,
                    fragmentPixelThreshold);
            }

            return componentCount;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedStack);
        }
    }

    private static ConnectedComponent FloodFill(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        Span<int> labels,
        Span<int> stack,
        int seedIndex,
        int label,
        Connectivity connectivity,
        int fragmentPixelThreshold)
    {
        int stackCount = 0;
        stack[stackCount++] = seedIndex;
        labels[seedIndex] = label;

        int pixelCount = 0;
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;
        bool touchesBorder = false;

        // 显式栈 flood fill：同时累计像素数、包围盒与是否触边。
        while (stackCount > 0)
        {
            int index = stack[--stackCount];
            int y = index / width;
            int x = index - (y * width);

            pixelCount++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            touchesBorder |= x == 0 || y == 0 || x == width - 1 || y == height - 1;

            TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x - 1, y);
            TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x + 1, y);
            TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x, y - 1);
            TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x, y + 1);

            // Eight 连通额外检查四个对角邻居。
            if (connectivity == Connectivity.Eight)
            {
                TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x - 1, y - 1);
                TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x + 1, y - 1);
                TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x - 1, y + 1);
                TryPush(solidMask, width, height, labels, stack, ref stackCount, label, x + 1, y + 1);
            }
        }

        return new ConnectedComponent(
            label,
            pixelCount,
            RectI.FromBounds(minX, minY, maxX + 1, maxY + 1),
            touchesBorder,
            fragmentPixelThreshold > 0 && pixelCount < fragmentPixelThreshold);
    }

    private static void TryPush(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        Span<int> labels,
        Span<int> stack,
        ref int stackCount,
        int label,
        int x,
        int y)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        int index = (y * width) + x;
        if ((solidMask[index] == 0) || (labels[index] != 0))
        {
            return;
        }

        labels[index] = label;
        stack[stackCount++] = index;
    }

    private static void ValidateArguments(
        ReadOnlySpan<byte> solidMask,
        int width,
        int height,
        Span<int> labels,
        Span<ConnectedComponent> components,
        int fragmentPixelThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(fragmentPixelThreshold);

        int area = checked(width * height);
        if (solidMask.Length < area)
        {
            throw new ArgumentException("solidMask 长度不足。", nameof(solidMask));
        }

        if (labels.Length < area)
        {
            throw new ArgumentException("labels 长度不足。", nameof(labels));
        }

        if (components.IsEmpty)
        {
            throw new ArgumentException("components 不能为空。", nameof(components));
        }
    }
}
