using RagScript.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RagScript.Chunker
{
    public static class Csproj_Chunker
    {

        public static List<ChunkResult> QuebrarCodigoCsproj(string conteudoXml, string nomeArquivo)
        {
            if (string.IsNullOrWhiteSpace(conteudoXml))
                return new List<ChunkResult>(0);

            var chunks = new List<ChunkResult>();
            try
            {
                XElement root = XElement.Parse(conteudoXml);
                string targetFramework = root.Descendants("TargetFramework").FirstOrDefault()?.Value
                                      ?? root.Descendants("TargetFrameworks").FirstOrDefault()?.Value
                                      ?? "Indefinido";

                // Chunk 1: Propriedades Gerais
                chunks.Add(new ChunkResult
                {
                    Tipo = "ConfiguracaoCsproj",
                    NomeMembro = "PropertyGroup",
                    HierarquiaCompleta = $"{nomeArquivo} -> PropertyGroup",
                    DocumentacaoXml = $"TargetFramework: {targetFramework}",
                    Conteudo = root.Elements("PropertyGroup").FirstOrDefault()?.ToString() ?? string.Empty
                });

                // Chunk 2: Dependências de Pacotes (NuGet)
                foreach (var pkg in root.Descendants("PackageReference"))
                {
                    string pacote = pkg.Attribute("Include")?.Value ?? "Desconhecido";
                    string versao = pkg.Attribute("Version")?.Value ?? "N/A";

                    chunks.Add(new ChunkResult
                    {
                        Tipo = "PacoteNuget",
                        NomeMembro = pacote,
                        HierarquiaCompleta = $"{nomeArquivo} -> PackageReference -> {pacote}",
                        DocumentacaoXml = $"Pacote NuGet versão {versao}",
                        Conteudo = pkg.ToString()
                    });
                }

                // Chunk 3: Referências de Projeto Interno
                foreach (var proj in root.Descendants("ProjectReference"))
                {
                    string caminhoProj = proj.Attribute("Include")?.Value ?? "";
                    string nomeProj = Path.GetFileNameWithoutExtension(caminhoProj);

                    chunks.Add(new ChunkResult
                    {
                        Tipo = "ReferenciaProjeto",
                        NomeMembro = nomeProj,
                        HierarquiaCompleta = $"{nomeArquivo} -> ProjectReference -> {nomeProj}",
                        DocumentacaoXml = $"Referência interna ao projeto: {caminhoProj}",
                        Conteudo = proj.ToString()
                    });
                }
            }
            catch
            {
                chunks.Add(new ChunkResult
                {
                    Tipo = "CsprojComErro",
                    NomeMembro = "Fallback",
                    HierarquiaCompleta = nomeArquivo,
                    Conteudo = conteudoXml
                });
            }

            return chunks;
        }

    }
}
