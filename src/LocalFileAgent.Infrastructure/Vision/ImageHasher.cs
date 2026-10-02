using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Text;
using SkiaSharp;

namespace LocalFileAgent.Infrastructure.Vision;

public sealed class ImageHasher : IImageHasher
{
    public async Task<ulong> ComputeDHashAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
        {
            return 0UL;
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var original = SKBitmap.Decode(imagePath);
            if (original == null)
            {
                return 0UL;
            }

            // Downsample to 9 columns x 8 rows
            var info = new SKImageInfo(9, 8, SKColorType.Gray8, SKAlphaType.Opaque);
            using var resized = original.Resize(info, SKFilterQuality.Medium);
            if (resized == null)
            {
                return 0UL;
            }

            var hash = 0UL;
            var bitIndex = 0;

            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var leftPixel = resized.GetPixel(x, y).Red;
                    var rightPixel = resized.GetPixel(x + 1, y).Red;

                    if (leftPixel > rightPixel)
                    {
                        hash |= (1UL << bitIndex);
                    }

                    bitIndex++;
                }
            }

            return hash;
        }, cancellationToken).ConfigureAwait(false);
    }

    public int ComputeHammingDistance(ulong hashA, ulong hashB)
    {
        return BitOperations.PopCount(hashA ^ hashB);
    }

    public bool AreNearDuplicates(ulong hashA, ulong hashB, int threshold = 5)
    {
        return ComputeHammingDistance(hashA, hashB) <= threshold;
    }
}
