using RagScript.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RagScript.Chunker
{
    public static class EditorConfig_Chunker
    {
        public static List<ChunkResult> QuebrarCodigoEditorConfig(string conteudo, string nomeArquivo)
        {
            if (string.IsNullOrWhiteSpace(conteudo))
                return new List<ChunkResult>(0);

            var chunks = new List<ChunkResult>();
            ReadOnlySpan<char> lines = conteudo.AsSpan();

            string secaoAtual = "Global";
            var sbConteudoSecao = new StringBuilder();

            foreach (var rawLine in lines.EnumerateLines())
            {
                ReadOnlySpan<char> line = rawLine.Trim();

                if (line.IsEmpty || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (sbConteudoSecao.Length > 0)
                    {
                        chunks.Add(new ChunkResult
                        {
                            Tipo = "SecaoEditorConfig",
                            NomeMembro = secaoAtual,
                            HierarquiaCompleta = $"{nomeArquivo} -> {secaoAtual}",
                            DocumentacaoXml = $"Regras de formatação para {secaoAtual}",
                            Conteudo = sbConteudoSecao.ToString().Trim()
                        });
                        sbConteudoSecao.Clear();
                    }
                    secaoAtual = line.Slice(1, line.Length - 2).ToString();
                    continue;
                }

                sbConteudoSecao.AppendLine(line.ToString());
            }

            if (sbConteudoSecao.Length > 0)
            {
                chunks.Add(new ChunkResult
                {
                    Tipo = "SecaoEditorConfig",
                    NomeMembro = secaoAtual,
                    HierarquiaCompleta = $"{nomeArquivo} -> {secaoAtual}",
                    DocumentacaoXml = $"Regras de formatação para {secaoAtual}",
                    Conteudo = sbConteudoSecao.ToString().Trim()
                });
            }

            return chunks;
        }

    }
}
