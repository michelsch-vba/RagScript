using System;
using System.Collections.Generic;

namespace RagScript.Models;

// Ajustado para aceitar uma lista de API Keys
public class Options
{
    public List<string> Keys { get; set; } = new();

    public Options() { }

    public Options(List<string> keys)
    {
        Keys = keys ?? new List<string>();
    }

    // Construtor auxiliar para manter compatibilidade
    public Options(string singleKey)
    {
        Keys = !string.IsNullOrWhiteSpace(singleKey)
            ? new List<string> { singleKey }
            : new List<string>();
    }
}

public class DocumentoVetorial
{
    public string IdChunk { get; set; } = Guid.NewGuid().ToString();
    public string CaminhoRelativo { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public string HashConteudo { get; set; } = string.Empty;
    public string TipoChunk { get; set; } = "ArquivoCompleto";
    public string NomeMembro { get; set; } = string.Empty;
    public string HierarquiaCompleta { get; set; } = string.Empty;
    public string Metadados { get; set; } = string.Empty;
    public string ConteudoTexto { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

public enum TipoPreset
{
    ArquiteturaERefatoracao = 1,
    SugestaoMelhoriaPerformance = 2,
    DocumentacaoTecnica = 3,
    AnaliseDeBugsESeguranca = 4,
    CriacaoDeTestesUnitarios = 5
}