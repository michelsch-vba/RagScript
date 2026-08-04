using System;

namespace RagScript.Models;

public record Options(string key);

public class DocumentoVetorial
{
    public string IdChunk { get; set; } = Guid.NewGuid().ToString(); // Identificador único do trecho
    public string CaminhoRelativo { get; set; } = string.Empty;
    public string NomeArquivo { get; set; } = string.Empty;
    public string HashConteudo { get; set; } = string.Empty;
    public string TipoChunk { get; set; } = "ArquivoCompleto"; // Ex: "Classe", "Metodo", "Enum"
    public string NomeMembro { get; set; } = string.Empty; // Ex: "ProcessarArquivosERagAsync"

    // Novo: Guarda a hierarquia sintática capturada pelo Roslyn (Namespace.Classe.Metodo)
    public string HierarquiaCompleta { get; set; } = string.Empty;

    public string Metadados { get; set; } = string.Empty; // Namespace / Heranças / Propriedades da classe
    public string ConteudoTexto { get; set; } = string.Empty; // Apenas o trecho de código/método
    public float[] Embedding { get; set; } = Array.Empty<float>();
}