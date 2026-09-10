using RagScript.Hooks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using RagScript.Models;

namespace RagScript.Chunker
{
    public static class Xaml_Chunker
    {
        private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        public static List<ChunkResult> QuebrarCodigoXaml(string conteudoXaml, string nomeArquivo)
        {
            if (string.IsNullOrWhiteSpace(conteudoXaml))
                return new List<ChunkResult>(0);

            try
            {
                XElement root = XElement.Parse(conteudoXaml);

                // MELHORIA 1: Limpeza de comentários XML antigos/comentados para evitar ruído
                root.DescendantNodes().OfType<XComment>().Remove();

                var chunks = new List<ChunkResult>();

                string localName = root.Name.LocalName;
                string classeVinculada = root.Attribute(XamlNs + "Class")?.Value ?? "SemCodeBehind";

                // CASO 1: ResourceDictionary puro (Dicionários isolados)
                if (localName == "ResourceDictionary")
                {
                    ProcessarResourceDictionaryPuro(root, nomeArquivo, chunks);
                    return chunks.Count > 0 ? chunks : CriarChunkFallback(conteudoXaml, nomeArquivo, localName);
                }

                // CASO 2: App.xaml (Configurações globais e inicialização)
                if (localName == "Application")
                {
                    string startupUri = root.Attribute("StartupUri")?.Value ?? "Não especificado";
                    chunks.Add(new ChunkResult
                    {
                        Tipo = "ConfiguracaoAppXaml",
                        NomeMembro = "ApplicationStartup",
                        HierarquiaCompleta = $"{nomeArquivo} -> Application",
                        DocumentacaoXml = $"Configuração global da aplicação. Tela inicial configurada: {startupUri}",
                        Conteudo = $"<Application StartupUri=\"{startupUri}\" x:Class=\"{classeVinculada}\" />"
                    });
                }

                // EXTRAÇÃO DE RECURSOS E TEMPLATES: Recorre nós de recursos em Window/UserControl/Application
                var nosRecursos = root.Descendants().Where(e => e.Name.LocalName.EndsWith(".Resources"));
                foreach (var noRes in nosRecursos)
                {
                    ExtrairRecursosDoNo(noRes.Elements(), nomeArquivo, localName, classeVinculada, chunks);
                }

                // EXTRAÇÃO DE ELEMENTOS INTERATIVOS E ESTRUTURAIS:
                // MELHORIA 2: Captura elementos nomeados (x:Name) OU elementos com Command/Binding funcional
                var elementosRelevantes = root.Descendants()
                    .Where(e => !e.Ancestors().Any(a => a.Name.LocalName.EndsWith(".Resources")))
                    .Where(e => e.Attribute(XamlNs + "Name") != null ||
                                e.Attribute("Name") != null ||
                                e.Attribute("Command") != null ||
                                (e.Attribute("ItemsSource") != null && e.Attribute("ItemsSource")!.Value.Contains("Binding")));

                foreach (var elem in elementosRelevantes)
                {
                    string nomeControle = elem.Attribute(XamlNs + "Name")?.Value
                                       ?? elem.Attribute("Name")?.Value
                                       ?? ObterIdentificadorPorBindingOuComando(elem);

                    string comandoAtribuido = elem.Attribute("Command")?.Value;
                    string docComplementar = !string.IsNullOrEmpty(comandoAtribuido)
                        ? $" [Comando vinculado: {comandoAtribuido}]"
                        : string.Empty;

                    chunks.Add(new ChunkResult
                    {
                        Tipo = $"ControleXaml ({elem.Name.LocalName})",
                        NomeMembro = nomeControle,
                        HierarquiaCompleta = $"{nomeArquivo} -> {classeVinculada} -> {nomeControle}",
                        DocumentacaoXml = $"Elemento {elem.Name.LocalName} na interface.{docComplementar}",
                        Conteudo = SanitizarConteudoXaml(elem.ToString().Trim())
                    });
                }

                return chunks.Count > 0 ? chunks : CriarChunkFallback(conteudoXaml, nomeArquivo, localName);
            }
            catch
            {
                return CriarChunkFallback(conteudoXaml, nomeArquivo, "XamlComErro");
            }
        }

        private static void ProcessarResourceDictionaryPuro(XElement root, string nomeArquivo, List<ChunkResult> chunks)
        {
            var merged = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "ResourceDictionary.MergedDictionaries");
            if (merged != null)
            {
                chunks.Add(new ChunkResult
                {
                    Tipo = "MergedDictionaries",
                    NomeMembro = "Imports",
                    HierarquiaCompleta = $"{nomeArquivo} -> MergedDictionaries",
                    DocumentacaoXml = "Dicionários de recursos externos importados neste arquivo",
                    Conteudo = SanitizarConteudoXaml(merged.ToString().Trim())
                });
            }

            var recursosDiretos = root.Elements().Where(e => e.Name.LocalName != "ResourceDictionary.MergedDictionaries");
            ExtrairRecursosDoNo(recursosDiretos, nomeArquivo, "ResourceDictionary", "Global", chunks);
        }

        private static void ExtrairRecursosDoNo(IEnumerable<XElement> elementos, string nomeArquivo, string containerPai, string classeVinculada, List<ChunkResult> chunks)
        {
            foreach (var child in elementos)
            {
                // MELHORIA 3: Suporte completo para DataTemplates implícitos via DataType
                string? dataType = child.Attribute("DataType")?.Value;
                string? key = child.Attribute(XamlNs + "Key")?.Value;
                string? targetType = child.Attribute("TargetType")?.Value;

                string chave = key
                            ?? (!string.IsNullOrEmpty(dataType) ? $"ImplicitDataTemplate ({ExtrairNomeClassePura(dataType)})" : null)
                            ?? (!string.IsNullOrEmpty(targetType) ? $"Style ({ExtrairNomeClassePura(targetType)})" : null)
                            ?? child.Name.LocalName;

                string tipoContexto = !string.IsNullOrEmpty(dataType)
                    ? $"DataTemplate Implicito para {dataType}"
                    : $"Recurso {child.Name.LocalName}";

                chunks.Add(new ChunkResult
                {
                    Tipo = $"RecursoXaml ({child.Name.LocalName})",
                    NomeMembro = chave,
                    HierarquiaCompleta = $"{nomeArquivo} -> {containerPai} -> {chave}",
                    DocumentacaoXml = $"{tipoContexto} definido em {containerPai} (Classe: {classeVinculada})",
                    Conteudo = SanitizarConteudoXaml(child.ToString().Trim())
                });
            }
        }

        private static string ObterIdentificadorPorBindingOuComando(XElement elem)
        {
            string? cmd = elem.Attribute("Command")?.Value;
            if (!string.IsNullOrEmpty(cmd))
                return $"ActionNode ({ExtrairNomeBindingPuro(cmd)})";

            string? items = elem.Attribute("ItemsSource")?.Value;
            if (!string.IsNullOrEmpty(items))
                return $"ListNode ({ExtrairNomeBindingPuro(items)})";

            return $"{elem.Name.LocalName}_SemNome";
        }

        private static string ExtrairNomeClassePura(string valor)
        {
            if (valor.Contains("{x:Type"))
            {
                var match = Regex.Match(valor, @"{x:Type\s+(?:[^:]+:)?([^}]+)}");
                if (match.Success) return match.Groups[1].Value.Trim();
            }
            return valor.Split(':').Last().Trim();
        }

        private static string ExtrairNomeBindingPuro(string valorBinding)
        {
            var match = Regex.Match(valorBinding, @"Path=([^,}]+)|Binding\s+([^,}]+)");
            if (match.Success)
            {
                string resultado = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                return resultado.Trim();
            }
            return valorBinding.Replace("{", "").Replace("}", "").Replace("Binding", "").Trim();
        }

        // MELHORIA 4: Limpeza e economia de tokens ao remover atributos puramente de posicionamento visual
        private static string SanitizarConteudoXaml(string xamlText)
        {
            if (string.IsNullOrWhiteSpace(xamlText)) return string.Empty;

            // Opcional: Remove atributos de margem e posicionamento repetitivos que poluem o vetor
            string limpo = Regex.Replace(xamlText, @"\s+(Margin|Grid\.Row|Grid\.Column|Grid\.RowSpan|Grid\.ColumnSpan|Canvas\.Left|Canvas\.Top)=""[^""]*""", "");
            return limpo;
        }

        private static List<ChunkResult> CriarChunkFallback(string conteudo, string nomeArquivo, string tipo)
        {
            return new List<ChunkResult>
        {
            new ChunkResult
            {
                Tipo = $"DocumentoXaml ({tipo})",
                NomeMembro = Path.GetFileNameWithoutExtension(nomeArquivo),
                HierarquiaCompleta = nomeArquivo,
                DocumentacaoXml = "Arquivo XAML completo (sem divisões internas)",
                Conteudo = conteudo
            }
        };
        }
    }
}
    
