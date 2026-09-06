using System;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RagScript.Services
{
    public static class VetorConverter
    {
        /// <summary>
        /// Método SÍNCRONO: Normaliza o vetor e o converte para byte[] em memória.
        /// Como não é async, o C# 12 permite usar Span sem erros.
        /// </summary>
        public static byte[] NormalizarQuantizarEConverterParaBytes(float[] vetor)
        {
            if (vetor == null || vetor.Length == 0)
                return Array.Empty<byte>();

            Span<float> vetorSpan = vetor.AsSpan();

            // 1. Normaliza L2 via SIMD
            float magnitude = TensorPrimitives.Norm(vetorSpan);
            
            if (MathF.Abs(magnitude - 1.0f) > 1e-6f && magnitude > 0.0f)
            {
                TensorPrimitives.Divide(vetorSpan, magnitude, vetorSpan);
            }

            // 2. Quantiza
            return QuantizarParaBytesSqlite(vetorSpan);
        }



        public static byte[] QuantizarParaBytesSqlite(ReadOnlySpan<float> vetorFloatNormalizado)
        {
            if (vetorFloatNormalizado.IsEmpty)
                return Array.Empty<byte>();

            int tamanho = vetorFloatNormalizado.Length;

            // 1. Aloca o byte[] de saída de 3072 bytes
            byte[] resultadoBytes = new byte[tamanho];
            Span<sbyte> sbytesSpan = MemoryMarshal.Cast<byte, sbyte>(resultadoBytes.AsSpan());

            // 2. Aluga um buffer temporário do ArrayPool (Zero alocação no GC e 100% seguro)
            float[] bufferTemporario = ArrayPool<float>.Shared.Rent(tamanho);
            Span<float> escalado = bufferTemporario.AsSpan(0, tamanho);

            try
            {
                // 3. Multiplica por 127.0f usando SIMD
                TensorPrimitives.Multiply(vetorFloatNormalizado, 127.0f, escalado);

                // 4. Arredonda e limita para sbyte [-128, 127]
                for (int i = 0; i < tamanho; i++)
                {
                    float valor = MathF.Round(escalado[i]);
                    sbytesSpan[i] = (sbyte)Math.Clamp(valor, -128f, 127f);
                }
            }
            finally
            {
                // Devolve o buffer temporário para o Pool
                ArrayPool<float>.Shared.Return(bufferTemporario);
            }

            return resultadoBytes;
        }
    }
}
