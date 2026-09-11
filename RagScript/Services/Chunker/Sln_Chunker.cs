using RagScript.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RagScript.Services.Chunker
{
    public static class Sln_Chunker
    {
        public static List<ChunkResult> QuebrarCodigoSln(string conteudo, string nomeArquivo)
        {
            if (string.IsNullOrWhiteSpace(conteudo))
                return new List<ChunkResult>(0);

            var chunks = new List<ChunkResult>();
            ReadOnlySpan<char> lines = conteudo.AsSpan();

            foreach (var rawLine in lines.EnumerateLines())
            {
                ReadOnlySpan<char> line = rawLine.Trim();

                if (line.StartsWith("Project("))
                {
                    int eqIndex = line.IndexOf('=');
                    if (eqIndex > -1)
                    {
                        ReadOnlySpan<char> declaration = line.Slice(eqIndex + 1).Trim();
                        string[] partes = declaration.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);

                        if (partes.Length >= 2)
                        {
                            string nomeProjeto = partes[0].Replace("\"", "").Trim();
                            string caminhoProjeto = partes[1].Replace("\"", "").Trim();

                            chunks.Add(new ChunkResult
                            {
                                Tipo = "ProjetoSolucao",
                                NomeMembro = nomeProjeto,
                                HierarquiaCompleta = $"{nomeArquivo} -> {nomeProjeto}",
                                DocumentacaoXml = $"Membro da Solução no caminho: {caminhoProjeto}",
                                Conteudo = line.ToString()
                            });
                        }
                    }
                }
            }

            return chunks;
        }

    }
}
