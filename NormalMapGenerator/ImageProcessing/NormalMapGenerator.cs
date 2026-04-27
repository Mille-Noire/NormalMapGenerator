using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NormalMapGenerator.ImageProcessing;

public static class NormalMapGenerator
{
    public static BitmapSource Generate(BitmapSource source, double strength, bool invertX, bool invertY)
    {
        ArgumentNullException.ThrowIfNull(source);

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

        double[] heights = BuildHeightMap(sourcePixels, width, height, stride);
        byte[] normalPixels = new byte[stride * height];
        double safeStrength = Math.Max(0.0, strength);

        for (int y = 0; y < height; y++)
        {
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

    private static double[] BuildHeightMap(byte[] pixels, int width, int height, int stride)
    {
        double[] heights = new double[width * height];

        for (int y = 0; y < height; y++)
        {
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

    private static byte ToColorChannel(double value)
    {
        double mapped = (value * 0.5) + 0.5;
        return (byte)Math.Clamp(Math.Round(mapped * 255.0), 0.0, 255.0);
    }
}
