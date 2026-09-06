using Microsoft.Data.Sqlite;
using RagScript.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

public class MotorBuscaRAG
{
    private readonly string _conexaoSqlite;

    public MotorBuscaRAG(string caminhoBanco)
    {
        _conexaoSqlite = $"Data Source={caminhoBanco};Mode=ReadOnly;";
    }

    public List<ResultadoBusca> BuscarTopK(
        IReadOnlyList<float> embeddingPergunta,
        string perguntaUsuario,
        int topK = 3,
        float threshold = 0.50f)
    {
        if (embeddingPergunta == null || embeddingPergunta.Count == 0)
            return new List<ResultadoBusca>();

        int tamanhoVetor = embeddingPergunta.Count;

        // Normalização L2 da pergunta via SIMD
        Span<float> perguntaSpan = stackalloc float[tamanhoVetor];
        for (int i = 0; i < tamanhoVetor; i++)
        {
            perguntaSpan[i] = embeddingPergunta[i];
        }

        float magnitude = TensorPrimitives.Norm(perguntaSpan);
        if (MathF.Abs(magnitude - 1.0f) > 1e-6f && magnitude > 0.0f)
        {
            TensorPrimitives.Divide(perguntaSpan, magnitude, perguntaSpan);
        }

        string perguntaLower = perguntaUsuario?.ToLowerInvariant() ?? "";
        var candidatos = new List<ResultadoBusca>();

        float[] bufferTemporario = ArrayPool<float>.Shared.Rent(tamanhoVetor);
        Span<float> chunkFloatSpan = bufferTemporario.AsSpan(0, tamanhoVetor);

        try
        {
            using var conexao = new SqliteConnection(_conexaoSqlite);
            conexao.Open();

            // Busca todas as colunas necessárias para preencher o DocumentoVetorial
            using var comando = conexao.CreateCommand();
            comando.CommandText = @"
                SELECT IdChunk, CaminhoRelativo, TipoChunk, NomeMembro, HierarquiaCompleta, Metadados, ConteudoTexto, VetorBlob 
                FROM ChunksVetoriais;";

            using var reader = comando.ExecuteReader();
            while (reader.Read())
            {
                var doc = new DocumentoVetorial
                {
                    IdChunk = reader.GetString(0),
                    CaminhoRelativo = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    TipoChunk = reader.IsDBNull(2) ? "ArquivoCompleto" : reader.GetString(2),
                    NomeMembro = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    HierarquiaCompleta = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Metadados = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    ConteudoTexto = reader.GetString(6)
                };

                byte[] blobBytes = (byte[])reader.GetValue(7);
                ReadOnlySpan<sbyte> sbytesQuantizados = MemoryMarshal.Cast<byte, sbyte>(blobBytes);

                // Desquantização SIMD (sbyte -> float)
                for (int i = 0; i < tamanhoVetor; i++)
                {
                    chunkFloatSpan[i] = sbytesQuantizados[i];
                }
                TensorPrimitives.Divide(chunkFloatSpan, 127.0f, chunkFloatSpan);

                // Cosseno SIMD + Score Ponderado por Metadados
                float baseCosineSim = TensorPrimitives.Dot(perguntaSpan, chunkFloatSpan);
                float scoreFinal = CalcularScorePonderado(doc, baseCosineSim, perguntaLower);

                if (scoreFinal >= threshold)
                {
                    candidatos.Add(new ResultadoBusca(doc, scoreFinal));
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(bufferTemporario);
        }

        return candidatos
            .OrderByDescending(c => c.Similaridade)
            .Take(topK)
            .ToList();
    }

    private float CalcularScorePonderado(DocumentoVetorial doc, float baseCosineSim, string perguntaLower)
    {
        if (baseCosineSim <= 0f) return 0f;

        float bonusMetadados = 0f;

        if (!string.IsNullOrWhiteSpace(doc.NomeMembro) &&
            doc.NomeMembro.Length > 2 &&
            perguntaLower.Contains(doc.NomeMembro.ToLowerInvariant()))
        {
            bonusMetadados += 0.25f;
        }

        if (!string.IsNullOrWhiteSpace(doc.HierarquiaCompleta) &&
            doc.HierarquiaCompleta.Split('.').Any(parte => parte.Length > 3 && perguntaLower.Contains(parte.ToLowerInvariant())))
        {
            bonusMetadados += 0.15f;
        }

        if (!string.IsNullOrWhiteSpace(doc.Metadados))
        {
            string metadadosLower = doc.Metadados.ToLowerInvariant();
            int matches = perguntaLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                       .Count(palavra => palavra.Length > 3 && metadadosLower.Contains(palavra));

            if (matches > 0)
            {
                bonusMetadados += Math.Min(matches * 0.05f, 0.15f);
            }
        }

        return Math.Min(baseCosineSim + bonusMetadados, 1.0f);
    }

    public List<string> ObterSugestoesMembros()
    {
        var sugestoes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var conexao = new SqliteConnection(_conexaoSqlite);
        conexao.Open();

        string sql = @"
        SELECT NomeMembro, NomeArquivo 
        FROM ChunksVetoriais 
        WHERE NomeMembro IS NOT NULL AND NomeMembro != '';";

        using var comando = new SqliteCommand(sql, conexao);
        using var reader = comando.ExecuteReader();

        while (reader.Read())
        {
            string nomeMembro = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            string nomeArquivo = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
           

            if (!string.IsNullOrWhiteSpace(nomeMembro))
                sugestoes.Add(nomeMembro);

            if (!string.IsNullOrWhiteSpace(nomeArquivo))
                sugestoes.Add(nomeArquivo);

        }

        return sugestoes.OrderBy(s => s).ToList();
    }

    public Dictionary<string, string> ObterHashesPorCaminho()
    {
        var mapaHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var conexao = new SqliteConnection(_conexaoSqlite);
        conexao.Open();

        string sql = "SELECT DISTINCT CaminhoRelativo, HashConteudo FROM ChunksVetoriais;";
        using var comando = new SqliteCommand(sql, conexao);
        using var reader = comando.ExecuteReader();

        while (reader.Read())
        {
            string caminho = reader.GetString(0);
            string hash = reader.GetString(1);
            mapaHashes[caminho] = hash;
        }

        return mapaHashes;
    }

    public async Task RemoverChunksPorCaminhoAsync(string caminhoRelativo)
    {
        using var conexao = new SqliteConnection(_conexaoSqlite);
        await conexao.OpenAsync();

        string sql = "DELETE FROM ChunksVetoriais WHERE CaminhoRelativo = $caminho;";
        using var comando = new SqliteCommand(sql, conexao);
        comando.Parameters.AddWithValue("$caminho", caminhoRelativo);

        await comando.ExecuteNonQueryAsync();
    }
}