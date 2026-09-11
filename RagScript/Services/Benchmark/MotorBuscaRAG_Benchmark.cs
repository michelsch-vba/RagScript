using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using RagScript.Models;
using System.Collections.Generic;
using System.IO;

[MemoryDiagnoser] // Liga a medição detalhada de memória/GC
[RankColumn]      // Adiciona uma coluna ordenando o método mais rápido
public class MotorBuscaRAGBenchmark
{
    private MotorBuscaRAG _motorBusca;
    private float[] _embeddingPergunta;
    private string _caminhoBanco;

    [GlobalSetup]
    public void Setup()
    {
        // Cria um caminho de banco fictício ou aponta para um banco .db de teste real
        _caminhoBanco = Path.Combine(Directory.GetCurrentDirectory(), "banco_teste.db");

        _motorBusca = new MotorBuscaRAG(_caminhoBanco);

        // Gera um vetor de 3072 dimensões pré-preenchido para simular a pergunta do Gemini
        _embeddingPergunta = new float[3072];
        for (int i = 0; i < 3072; i++)
        {
            _embeddingPergunta[i] = (float)(i % 100) / 100f;
        }
    }

    [Benchmark(Baseline = true)]
    public List<ResultadoBusca> BuscaTopK_SIMD_ComArrayPool()
    {
        // Mede a performance do seu método principal otimizado
        return _motorBusca.BuscarTopK(
            embeddingPergunta: _embeddingPergunta,
            perguntaUsuario: "Como funciona a quantização?",
            topK: 5,
            threshold: 0.50f
        );
    }
}