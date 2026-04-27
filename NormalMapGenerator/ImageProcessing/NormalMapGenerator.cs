using System;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NormalMapGenerator.ImageProcessing;

public static class NormalMapGenerator
{
    public static BitmapSource? Generate(
        BitmapSource source,
        double strength,
        double level,
        double blurSharp,
        bool invertX,
        bool invertY,
        HeightChannelSource channelSource = HeightChannelSource.Luminance,
        NormalMapEdgeMode edgeMode = NormalMapEdgeMode.Clamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        BitmapSource input = EnsureBgra32(source);
        int width = input.PixelWidth;
        int height = input.PixelHeight;

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("The source image must contain at least one pixel.", nameof(source));
        }

        int stride = width * 4;
        byte[] sourcePixels = new byte[stride * height];
        input.CopyPixels(sourcePixels, stride, 0);

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        double[] heights = BuildProcessedHeightMap(
            sourcePixels,
            width,
            height,
            stride,
            channelSource,
            edgeMode,
            level,
            blurSharp,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        byte[] normalPixels = new byte[stride * height];
        double safeStrength = Math.Max(0.0, strength);

        for (int y = 0; y < height; y++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            int upY = ResolveSampleCoordinate(y - 1, height, edgeMode);
            int downY = ResolveSampleCoordinate(y + 1, height, edgeMode);

            for (int x = 0; x < width; x++)
            {
                int leftX = ResolveSampleCoordinate(x - 1, width, edgeMode);
                int rightX = ResolveSampleCoordinate(x + 1, width, edgeMode);

                double left = heights[y * width + leftX];
                double right = heights[y * width + rightX];
                double up = heights[upY * width + x];
                double down = heights[downY * width + x];

                double dx = right - left;
                double dy = down - up;

                double nx = -dx * safeStrength;
                double ny = -dy * safeStrength;
                const double nz = 1.0;

                double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                nx /= length;
                ny /= length;
                double normalizedZ = nz / length;

                byte red = ToColorChannel(nx);
                byte green = ToColorChannel(ny);
                byte blue = ToColorChannel(normalizedZ);

                if (invertX)
                {
                    red = (byte)(255 - red);
                }

                if (invertY)
                {
                    green = (byte)(255 - green);
                }

                int pixelIndex = y * stride + x * 4;
                normalPixels[pixelIndex] = blue;
                normalPixels[pixelIndex + 1] = green;
                normalPixels[pixelIndex + 2] = red;
                normalPixels[pixelIndex + 3] = 255;
            }
        }

        BitmapSource normalMap = BitmapSource.Create(
            width,
            height,
            input.DpiX,
            input.DpiY,
            PixelFormats.Bgra32,
            null,
            normalPixels,
            stride);

        normalMap.Freeze();
        return normalMap;
    }

    public static BitmapSource? GenerateDisplacement(
        BitmapSource source,
        double level,
        double blurSharp,
        bool invert,
        HeightChannelSource channelSource = HeightChannelSource.Luminance,
        NormalMapEdgeMode edgeMode = NormalMapEdgeMode.Clamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        BitmapSource input = EnsureBgra32(source);
        int width = input.PixelWidth;
        int height = input.PixelHeight;

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("The source image must contain at least one pixel.", nameof(source));
        }

        int stride = width * 4;
        byte[] sourcePixels = new byte[stride * height];
        input.CopyPixels(sourcePixels, stride, 0);

        double[] heights = BuildProcessedHeightMap(
            sourcePixels,
            width,
            height,
            stride,
            channelSource,
            edgeMode,
            level,
            blurSharp,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        byte[] displacementPixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            for (int x = 0; x < width; x++)
            {
                double heightValue = Math.Clamp(heights[y * width + x], 0.0, 1.0);
                if (invert)
                {
                    heightValue = 1.0 - heightValue;
                }

                byte gray = (byte)Math.Clamp(Math.Round(heightValue * 255.0), 0.0, 255.0);
                int pixelIndex = y * stride + x * 4;
                displacementPixels[pixelIndex] = gray;
                displacementPixels[pixelIndex + 1] = gray;
                displacementPixels[pixelIndex + 2] = gray;
                displacementPixels[pixelIndex + 3] = 255;
            }
        }

        BitmapSource displacementMap = BitmapSource.Create(
            width,
            height,
            input.DpiX,
            input.DpiY,
            PixelFormats.Bgra32,
            null,
            displacementPixels,
            stride);

        displacementMap.Freeze();
        return displacementMap;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
        {
            return source;
        }

        FormatConvertedBitmap converted = new(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static double[] BuildProcessedHeightMap(
        byte[] pixels,
        int width,
        int height,
        int stride,
        HeightChannelSource channelSource,
        NormalMapEdgeMode edgeMode,
        double level,
        double blurSharp,
        CancellationToken cancellationToken)
    {
        double[] heights = BuildHeightMap(pixels, width, height, stride, channelSource, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return heights;
        }

        heights = ApplyLevel(heights, level, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return heights;
        }

        return ApplyBlurSharp(heights, width, height, blurSharp, edgeMode, cancellationToken);
    }

    private static double[] BuildHeightMap(
        byte[] pixels,
        int width,
        int height,
        int stride,
        HeightChannelSource channelSource,
        CancellationToken cancellationToken)
    {
        double[] heights = new double[width * height];

        for (int y = 0; y < height; y++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return heights;
            }

            for (int x = 0; x < width; x++)
            {
                int pixelIndex = y * stride + x * 4;
                byte blue = pixels[pixelIndex];
                byte green = pixels[pixelIndex + 1];
                byte red = pixels[pixelIndex + 2];
                byte alpha = pixels[pixelIndex + 3];

                byte sourceValue = channelSource switch
                {
                    HeightChannelSource.Red => red,
                    HeightChannelSource.Green => green,
                    HeightChannelSource.Blue => blue,
                    HeightChannelSource.Alpha => alpha,
                    _ => (byte)Math.Clamp(Math.Round((0.299 * red) + (0.587 * green) + (0.114 * blue)), 0.0, 255.0)
                };

                heights[y * width + x] = sourceValue / 255.0;
            }
        }

        return heights;
    }

    private static double[] ApplyLevel(double[] heights, double level, CancellationToken cancellationToken)
    {
        double safeLevel = Math.Clamp(level, 0.0, 3.0);

        if (Math.Abs(safeLevel - 1.0) < 0.001)
        {
            return heights;
        }

        double[] adjusted = new double[heights.Length];

        for (int i = 0; i < heights.Length; i++)
        {
            if (i % 4096 == 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return adjusted;
                }
            }

            adjusted[i] = ((heights[i] - 0.5) * safeLevel) + 0.5;
        }

        return adjusted;
    }

    private static double[] ApplyBlurSharp(
        double[] heights,
        int width,
        int height,
        double blurSharp,
        NormalMapEdgeMode edgeMode,
        CancellationToken cancellationToken)
    {
        double amount = Math.Clamp(blurSharp, -10.0, 10.0);

        if (Math.Abs(amount) < 0.001)
        {
            return heights;
        }

        int radius = Math.Max(1, (int)Math.Ceiling(Math.Abs(amount)));
        double[] blurred = BoxBlur(heights, width, height, radius, edgeMode, cancellationToken);

        if (amount > 0)
        {
            double blurBlend = amount / 10.0;
            return BlendHeightMaps(heights, blurred, blurBlend, cancellationToken);
        }

        double sharpenStrength = (-amount / 10.0) * 2.0;
        double[] sharpened = new double[heights.Length];

        for (int i = 0; i < heights.Length; i++)
        {
            if (i % 4096 == 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return sharpened;
                }
            }

            sharpened[i] = heights[i] + ((heights[i] - blurred[i]) * sharpenStrength);
        }

        return sharpened;
    }

    private static double[] BoxBlur(
        double[] heights,
        int width,
        int height,
        int radius,
        NormalMapEdgeMode edgeMode,
        CancellationToken cancellationToken)
    {
        double[] horizontal = new double[heights.Length];
        double[] blurred = new double[heights.Length];
        int diameter = (radius * 2) + 1;

        for (int y = 0; y < height; y++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return blurred;
            }

            for (int x = 0; x < width; x++)
            {
                double sum = 0.0;

                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sampleX = ResolveSampleCoordinate(x + offset, width, edgeMode);
                    sum += heights[y * width + sampleX];
                }

                horizontal[y * width + x] = sum / diameter;
            }
        }

        for (int y = 0; y < height; y++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return blurred;
            }

            for (int x = 0; x < width; x++)
            {
                double sum = 0.0;

                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sampleY = ResolveSampleCoordinate(y + offset, height, edgeMode);
                    sum += horizontal[sampleY * width + x];
                }

                blurred[y * width + x] = sum / diameter;
            }
        }

        return blurred;
    }

    private static double[] BlendHeightMaps(
        double[] original,
        double[] target,
        double amount,
        CancellationToken cancellationToken)
    {
        double[] blended = new double[original.Length];

        for (int i = 0; i < original.Length; i++)
        {
            if (i % 4096 == 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return blended;
                }
            }

            blended[i] = original[i] + ((target[i] - original[i]) * amount);
        }

        return blended;
    }

    private static byte ToColorChannel(double value)
    {
        double mapped = (value * 0.5) + 0.5;
        return (byte)Math.Clamp(Math.Round(mapped * 255.0), 0.0, 255.0);
    }

    private static int ResolveSampleCoordinate(int coordinate, int length, NormalMapEdgeMode edgeMode)
    {
        if (length <= 1)
        {
            return 0;
        }

        if (edgeMode == NormalMapEdgeMode.Wrap)
        {
            int wrapped = coordinate % length;
            return wrapped < 0 ? wrapped + length : wrapped;
        }

        return Math.Clamp(coordinate, 0, length - 1);
    }
}

public enum HeightChannelSource
{
    Luminance,
    Red,
    Green,
    Blue,
    Alpha
}

public enum NormalMapEdgeMode
{
    Clamp,
    Wrap
}
