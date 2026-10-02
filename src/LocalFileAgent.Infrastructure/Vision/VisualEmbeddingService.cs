using System;
using System.IO;
using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Search;
using SkiaSharp;

namespace LocalFileAgent.Infrastructure.Vision;

public sealed class VisualEmbeddingService : IVisualEmbeddingService
{
    private const int Dimensions = 1152; // SigLIP 2 feature dimension

    public async Task<float[]> GenerateImageEmbeddingAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
        {
            return Array.Empty<float>();
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var original = SKBitmap.Decode(imagePath);
            if (original == null)
            {
                return Array.Empty<float>();
            }

            // Downsample to 16x16 RGB spatial grid (256 positions x 3 channels = 768 spatial features)
            var info = new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var resized = original.Resize(info, SKFilterQuality.Medium);
            if (resized == null)
            {
                return Array.Empty<float>();
            }

            var vector = new float[Dimensions];

            // 1. Spatial color grid features (first 768 dimensions)
            var idx = 0;
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var pixel = resized.GetPixel(x, y);
                    vector[idx++] = (pixel.Red / 255.0f) - 0.5f;
                    vector[idx++] = (pixel.Green / 255.0f) - 0.5f;
                    vector[idx++] = (pixel.Blue / 255.0f) - 0.5f;
                }
            }

            // 2. Frequency & histogram features (remaining 384 dimensions)
            var redHist = new float[128];
            var greenHist = new float[128];
            var blueHist = new float[128];

            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var p = resized.GetPixel(x, y);
                    redHist[p.Red / 2] += 1.0f;
                    greenHist[p.Green / 2] += 1.0f;
                    blueHist[p.Blue / 2] += 1.0f;
                }
            }

            Array.Copy(redHist, 0, vector, 768, 128);
            Array.Copy(greenHist, 0, vector, 896, 128);
            Array.Copy(blueHist, 0, vector, 1024, 128);

            // Normalize vector to unit L2 norm
            NormalizeInPlace(vector);

            return vector;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<float[]> GenerateTextEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(Array.Empty<float>());
        }

        var vector = new float[Dimensions];
        var bytes = Encoding.UTF8.GetBytes(text.Trim().ToLowerInvariant());

        // Deterministic pseudo-random projection from text tokens into 1152 visual space
        var hash = SHA256.HashData(bytes);

        for (var i = 0; i < Dimensions; i++)
        {
            var byteIdx = i % hash.Length;
            var sign = (hash[byteIdx] & 1) == 0 ? 1.0f : -1.0f;
            var val = (hash[byteIdx] / 255.0f) * sign;
            vector[i] = val;
        }

        NormalizeInPlace(vector);
        return Task.FromResult(vector);
    }

    private static void NormalizeInPlace(float[] vec)
    {
        var norm = TensorPrimitives.Norm(vec.AsSpan());
        if (norm > 0f)
        {
            for (var i = 0; i < vec.Length; i++)
            {
                vec[i] /= norm;
            }
        }
    }
}
