using System;
using System.Buffers;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace RagScript.Services
{
    public static class VetorConverter
    {
        public static byte[] NormalizarQuantizarEConverterParaBytes(ReadOnlySpan<float> vetorOriginal)
        {
            if (vetorOriginal.IsEmpty)
                return Array.Empty<byte>();

            int tamanho = vetorOriginal.Length;

            // Aluga buffer no ArrayPool para evitar mutar o vetor original do documento
            float[] bufferNormalizado = ArrayPool<float>.Shared.Rent(tamanho);
            Span<float> vetorSpan = bufferNormalizado.AsSpan(0, tamanho);

            try
            {
                vetorOriginal.CopyTo(vetorSpan);

                // 1. Normalização L2 via SIMD
                float magnitude = TensorPrimitives.Norm(vetorSpan);
                if (MathF.Abs(magnitude - 1.0f) > 1e-6f && magnitude > 0.0f)
                {
                    TensorPrimitives.Divide(vetorSpan, magnitude, vetorSpan);
                }

                // 2. Quantização int8
                return QuantizarParaBytesSqlite(vetorSpan);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(bufferNormalizado);
            }
        }

        public static byte[] QuantizarParaBytesSqlite(ReadOnlySpan<float> vetorFloatNormalizado)
        {
            if (vetorFloatNormalizado.IsEmpty)
                return Array.Empty<byte>();

            int tamanho = vetorFloatNormalizado.Length;

            byte[] resultadoBytes = new byte[tamanho];
            Span<sbyte> sbytesSpan = MemoryMarshal.Cast<byte, sbyte>(resultadoBytes.AsSpan());

            float[] bufferTemporario = ArrayPool<float>.Shared.Rent(tamanho);
            Span<float> escalado = bufferTemporario.AsSpan(0, tamanho);

            try
            {
                // Multiplicação por 127.0f via SIMD (AVX/Hardware Acceleration)
                TensorPrimitives.Multiply(vetorFloatNormalizado, 127.0f, escalado);

                for (int i = 0; i < tamanho; i++)
                {
                    float valor = MathF.Round(escalado[i]);
                    sbytesSpan[i] = (sbyte)Math.Clamp(valor, -128f, 127f);
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(bufferTemporario);
            }

            return resultadoBytes;
        }
    }
}