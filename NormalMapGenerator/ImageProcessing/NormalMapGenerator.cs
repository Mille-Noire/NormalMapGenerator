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

        double[] heights = BuildHeightMap(sourcePixels, width, height, stride, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        heights = ApplyLevel(heights, level, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        heights = ApplyBlurSharp(heights, width, height, blurSharp, cancellationToken);
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

            int upY = Math.Max(0, y - 1);
            int downY = Math.Min(height - 1, y + 1);

            for (int x = 0; x < width; x++)
            {
                int leftX = Math.Max(0, x - 1);
                int rightX = Math.Min(width - 1, x + 1);

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

    private static double[] BuildHeightMap(
        byte[] pixels,
        int width,
        int height,
        int stride,
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

                heights[y * width + x] = ((0.299 * red) + (0.587 * green) + (0.114 * blue)) / 255.0;
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
        CancellationToken cancellationToken)
    {
        double amount = Math.Clamp(blurSharp, -10.0, 10.0);

        if (Math.Abs(amount) < 0.001)
        {
            return heights;
        }

        int radius = Math.Max(1, (int)Math.Ceiling(Math.Abs(amount)));
        double[] blurred = BoxBlur(heights, width, height, radius, cancellationToken);

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
                    int sampleX = Math.Clamp(x + offset, 0, width - 1);
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
                    int sampleY = Math.Clamp(y + offset, 0, height - 1);
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
}
