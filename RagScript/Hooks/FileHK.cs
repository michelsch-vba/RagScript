using FreeRag.IndexerConsole; // Para usar a classe DocumentoVetorial
using Google.GenAI;
using Google.GenAI.Types;
using RagScript.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static RagScript.Hooks.CodeChunker;
using static RagScript.Hooks.RagHook;

namespace RagScript.Hooks;

    public class ApiHook
    {

    public async Task<string> ObteroudarKey()
        {
            JsonSerializerOptions options = new JsonSerializerOptions() { WriteIndented = true };

            string pastaApp = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RagKey");
            Directory.CreateDirectory(pastaApp);

            string arquivo = Path.Combine(pastaApp, "key.json");
            string apiKey = string.Empty;

            // 1. Tenta ler chave existente no arquivo
            if (System.IO.File.Exists(arquivo))
            {
                string conteudo = System.IO.File.ReadAllText(arquivo);

                if (!string.IsNullOrWhiteSpace(conteudo))
                {
                    Options keySalva = JsonSerializer.Deserialize<Options>(conteudo, options) ?? new Options(string.Empty);

                    if (!string.IsNullOrEmpty(keySalva.key))
                    {
                        apiKey = keySalva.key;

                        string chaveMascarada = MascararKey(apiKey);
                        Console.WriteLine($"\n🔑 Chave encontrada no sistema: {chaveMascarada}");
                        Console.WriteLine("---------------------------------------------");
                        Console.WriteLine("[1] Usar a chave atual");
                        Console.WriteLine("[2] Alterar / Editar a API Key");
                        Console.WriteLine("[3] Voltar");
                        Console.Write("Escolha uma opção: ");

                        string opcao = Console.ReadLine()?.Trim() ?? string.Empty;

                        if (opcao == "1")
                        {
                            // Testa a chave salva para garantir que continua funcional antes de retornar
                            bool valida = await TestarApiKeyAsync(apiKey);
                            if (valida)
                            {
                                return apiKey;
                            }

                            Console.WriteLine("⚠️ Não foi possível validar a chave salva no momento. Informe uma nova ou tente mais tarde.");
                        }
                        else if (opcao == "3")
                        {
                            Console.WriteLine("Operação cancelada pelo usuário.");
                            return string.Empty;
                        }

                        apiKey = string.Empty; // Reseta se escolheu [2] ou se a chave salva falhou
                    }
                }
            }

            // 2. Loop para digitação e VALIDAÇÃO ANTES DE SALVAR
            Console.WriteLine("\n⚠️ Informe sua API Key do Gemini.");

            while (string.IsNullOrEmpty(apiKey))
            {
                Console.Write("\nDigite a nova API Key (ou 'sair' para encerrar): ");
                string input = Console.ReadLine()?.Trim() ?? string.Empty;

                if (input.Equals("sair", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Operação cancelada.");
                    return string.Empty;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("❌ Chave não pode ser vazia. Tente novamente.");
                    continue;
                }

                // Testamos a API Key na nuvem antes de criar o arquivo
                bool passouNoTeste = await TestarApiKeyAsync(input);

                if (passouNoTeste)
                {
                    apiKey = input;

                    try
                    {
                        Options novoOptions = new Options(apiKey);
                        string jsonParaSalvar = JsonSerializer.Serialize(novoOptions, options);
                        System.IO.File.WriteAllText(arquivo, jsonParaSalvar);
                        Console.WriteLine($"✅ API Key validada e salva em: {arquivo}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Chave válida, mas ocorreu um erro ao salvar o arquivo: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Não foi possível validar essa chave. Verifique se digitou corretamente.");
                }
            }

            return apiKey;
        }

    
    public async Task<bool> TestarApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        Console.WriteLine("📡 Validando API Key na nuvem...");

        try
        {
            // Testa a chave listando os modelos disponíveis (não consome cota de texto e não quebra por modelo antigo)
            using var client = new HttpClient();
            string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";

            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            string erro = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"❌ Chave recusada pela API ({response.StatusCode}): {erro}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Falha de conexão ao validar chave: {ex.Message}");
            return false;
        }
    }

    private string MascararKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length <= 8)
                return "****";

            return $"{key[..6]}...{key[^4..]}";
        }
    }

    public class RagHook
    {
        private static readonly HttpClient _httpClient = new HttpClient();

    // Pastas do sistema/build que devem ser ignoradas no escaneamento
    private static readonly HashSet<string> PastasIgnoradas = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".vs", ".git", ".idea", "node_modules", "packages"
    };

        // Extensões binárias ignoradas na vetorização
        private static readonly HashSet<string> ExtensoesBinariasIgnoradas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db", ".sqlite", ".db-shm", ".db-wal", ".ttf", ".otf", ".png", ".jpg", ".jpeg", ".dll", ".exe", ".pdb"
    };

        
        public string SelecionarPastaOrigem()
        {
            string caminhoSelecionado = string.Empty;

            Thread threadUI = new Thread(() =>
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Selecione a pasta com os arquivos para gerar o RAG";
                    dialog.UseDescriptionForTitle = true;
                    dialog.ShowNewFolderButton = false;

                    using (Form formDummy = new Form { TopMost = true, StartPosition = FormStartPosition.CenterScreen })
                    {
                        DialogResult result = dialog.ShowDialog(formDummy);
                        if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                        {
                            caminhoSelecionado = dialog.SelectedPath;
                        }
                    }
                }
            });

            threadUI.SetApartmentState(ApartmentState.STA);
            threadUI.Start();
            threadUI.Join();

            return caminhoSelecionado;
        }


    private IEnumerable<string> ObterArquivosValidos(string caminhoPasta)
    {
        return Directory.EnumerateFiles(caminhoPasta, "*.*", SearchOption.AllDirectories)
            .Where(arquivo => !CaminhoContemPastaIgnorada(arquivo.AsSpan()));
    }

    private static bool CaminhoContemPastaIgnorada(ReadOnlySpan<char> caminho)
    {
        // Percorre cada caractere / trecho do caminho usando slicing sem alocar strings
        while (!caminho.IsEmpty)
        {
            int index = caminho.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            ReadOnlySpan<char> segmento;
            if (index < 0)
            {
                segmento = caminho;
                caminho = ReadOnlySpan<char>.Empty;
            }
            else
            {
                segmento = caminho.Slice(0, index);
                caminho = caminho.Slice(index + 1);
            }

            if (segmento.IsEmpty) continue;

            // Verifica contra o HashSet de pastas ignoradas
            foreach (var pasta in PastasIgnoradas)
            {
                if (segmento.Equals(pasta.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }


    public List<string> SelecionarExtensoes(string caminhoPasta)
        {
            var todosArquivosValidos = ObterArquivosValidos(caminhoPasta).ToList();

            var extensoesEncontradas = todosArquivosValidos
                .Select(f => Path.GetExtension(f).ToLower())
                .Where(ext => !string.IsNullOrEmpty(ext))
                .Distinct()
                .ToList();

            if (!extensoesEncontradas.Any())
            {
                Console.WriteLine("⚠️ Nenhum arquivo válido encontrado (pastas bin/obj foram ignoradas).");
                return new List<string>();
            }

            Console.WriteLine("\n🎯 Escolha um Perfil de Seleção para o RAG:");
            Console.WriteLine("---------------------------------------------");
            Console.WriteLine("[1] 💻 Perfil C# & WPF (.cs, .xaml, .xml, .csproj, .slnx, .editorconfig)");
            Console.WriteLine("[2] 🗄️ Perfil Dados & Config (.json, .db, .sqlite, .xml, .config, .http, .settings)");
            Console.WriteLine("[3] 📄 Perfil Documentação (.md, .txt)");
            Console.WriteLine("[4] 🚀 SELECIONAR TUDO (Exceto bin/obj)");
            Console.WriteLine("[5] 🛠️ Escolher extensões manualmente (Lista Filtrada)");
            Console.Write("\nDigite a opção desejada (pode combinar, ex: 1,2): ");

            string escolha = Console.ReadLine()?.Trim() ?? string.Empty;

            if (escolha == "4" || escolha.Equals("tudo", StringComparison.OrdinalIgnoreCase))
            {
                return extensoesEncontradas;
            }

            var selecao = new List<string>();

            if (escolha.Contains("1"))
                selecao.AddRange(extensoesEncontradas.Intersect(new[] { ".cs", ".xaml", ".xml", ".csproj", ".sln", ".slnx", ".editorconfig", ".props" }));

            if (escolha.Contains("2"))
                selecao.AddRange(extensoesEncontradas.Intersect(new[] { ".json", ".db", ".sqlite", ".xml", ".config", ".http", ".settings" }));

            if (escolha.Contains("3"))
                selecao.AddRange(extensoesEncontradas.Intersect(new[] { ".md", ".txt" }));

            if (escolha.Contains("5"))
                return SelecionarExtensoesManualmente(extensoesEncontradas);

            return selecao.Distinct().Any() ? selecao.Distinct().ToList() : extensoesEncontradas;
        }

        private List<string> SelecionarExtensoesManualmente(List<string> extensoes)
        {
            Console.WriteLine("\n📁 Extensões limpas encontradas no projeto:");
            for (int i = 0; i < extensoes.Count; i++)
            {
                Console.WriteLine($"  [{i + 1}] {extensoes[i]}");
            }

            Console.Write("\nDigite os números separados por vírgula (ex: 1,3,4): ");
            string entrada = Console.ReadLine()?.Trim() ?? string.Empty;

            var selecionadas = new List<string>();
            var escolhas = entrada.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var item in escolhas)
            {
                if (int.TryParse(item.Trim(), out int indice) && indice >= 1 && indice <= extensoes.Count)
                {
                    selecionadas.Add(extensoes[indice - 1]);
                }
            }

            return selecionadas.Distinct().ToList();
        }


    public async Task<List<DocumentoVetorial>> ProcessarArquivosERagAsync(string caminhoPasta, List<string> extensoes, string apiKey)
    {
        var documentosVetoriais = new List<DocumentoVetorial>();

        var arquivosParaProcessar = ObterArquivosValidos(caminhoPasta)
            .Where(f => extensoes.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        Console.WriteLine($"\n🧠 Processando {arquivosParaProcessar.Count} arquivos para vetorização...\n");

        foreach (var arquivo in arquivosParaProcessar)
        {
            string ext = Path.GetExtension(arquivo).ToLower();

            if (ExtensoesBinariasIgnoradas.Contains(ext)) continue;

            try
            {
                string conteudo = await System.IO.File.ReadAllTextAsync(arquivo);
                if (string.IsNullOrWhiteSpace(conteudo)) continue;

                string caminhoRelativo = Path.GetRelativePath(caminhoPasta, arquivo);
                string hash = GerarHashSHA256(conteudo);
                string metadadosExtraidos = (ext == ".cs") ? ExtrairEstruturaCSharp(conteudo) : $"Arquivo {ext}";

                // 💡 CHUNKING: Se for C#, quebra em métodos. Se não, trata como bloco único.
                var pedacos = (ext == ".cs")
                    ? CodeChunker.QuebrarCodigoCSharp(conteudo)
                    : new List<ChunkResult> { new ChunkResult { Tipo = "Documento", NomeMembro = Path.GetFileName(arquivo), Conteudo = conteudo } };

                foreach (var chunk in pedacos)
                {
                    // Enriquecimento do Chunk com o Contexto Pai (Arquivo e Namespace/Classe)
                    string textoParaEmbedding = $"[ARQUIVO: {caminhoRelativo}]\n" +
                                                $"[MEMBER/TIPO: {chunk.Tipo} -> {chunk.NomeMembro}]\n" +
                                                $"[ESTRUTURA/METADADOS: {metadadosExtraidos}]\n\n" +
                                                $"[CÓDIGO/CONTEÚDO]:\n{chunk.Conteudo}";

                    Console.Write($"🔄 Vetorizando Chunk ({chunk.NomeMembro}) em {caminhoRelativo}... ");

                    float[] vectorValues = await GerarEmbeddingHttpAsync(textoParaEmbedding, apiKey);

                    if (vectorValues.Length > 0)
                    {
                        documentosVetoriais.Add(new DocumentoVetorial
                        {
                            CaminhoRelativo = caminhoRelativo,
                            NomeArquivo = Path.GetFileName(arquivo),
                            HashConteudo = hash,
                            TipoChunk = chunk.Tipo,
                            NomeMembro = chunk.NomeMembro,
                            HierarquiaCompleta = chunk.HierarquiaCompleta,
                            Metadados = metadadosExtraidos,
                            ConteudoTexto = chunk.Conteudo, // Guarda apenas o código do método/bloco
                            Embedding = vectorValues
                        });

                        Console.WriteLine("✅ OK");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Falha ao vetorizar chunk.");
                    }

                    await Task.Delay(1500); // Respeita cota da API
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao processar {Path.GetFileName(arquivo)}: {ex.Message}");
            }
        }

        return documentosVetoriais;
    }

    public async Task<float[]> GerarEmbeddingHttpAsync(string texto, string apiKey)
        {
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={apiKey}";
        var payload = new
            {
                content = new
                {
                    parts = new[] { new { text = texto } }
                }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            int tentativamax = 5;
            // Tenta até 3 vezes caso bata no Rate Limit (HTTP 429)
            for (int tentativa = 1; tentativa <= tentativamax; tentativa++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };

                    using var response = await _httpClient.SendAsync(request);

                    // Se tomou Rate Limit (429), aguarda 10 segundos e tenta de novo
                    if ((int)response.StatusCode == 429)
                    {
                        Console.WriteLine($"\n⚠️ Limite de requisições por minuto atingido (429). Aguardando 10s (Tentativa {tentativa}/{tentativamax})...");
                        await Task.Delay(20000);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        string erroCorpo = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"\n❌ Erro na API HTTP ({response.StatusCode}): {erroCorpo}");
                        return Array.Empty<float>();
                    }

                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonResponse);

                    if (doc.RootElement.TryGetProperty("embedding", out var embeddingProp) &&
                        embeddingProp.TryGetProperty("values", out var valuesProp))
                    {
                        return valuesProp.EnumerateArray()
                            .Select(v => v.GetSingle())
                            .ToArray();
                    }

                    return Array.Empty<float>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n❌ Exceção ao gerar embedding: {ex.Message}");
                }
            }

            return Array.Empty<float>();
        }
        private string GerarHashSHA256(string texto)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(texto));
            return Convert.ToHexString(bytes);
        }

    private string ExtrairEstruturaCSharp(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return "Arquivo C# Vazio";

        SyntaxTree tree = CSharpSyntaxTree.ParseText(codigo);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        var estrutura = new List<string>();

        // 1. Extrai Namespace
        var namespaceNode = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        if (namespaceNode != null)
        {
            estrutura.Add($"Namespace: {namespaceNode.Name}");
        }

        // 2. Extrai Tipos (Classes, Interfaces, Structs, Records)
        var tipos = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();

        foreach (var tipo in tipos)
        {
            var sbTipo = new StringBuilder();

            // Captura o tipo (class, interface, record, struct) e o nome
            string tipoKIND = tipo.Kind().ToString().Replace("Declaration", "").ToLower();
            sbTipo.Append($"{tipoKIND} {tipo.Identifier.Text}");

            // Captura Herança / Implementação de Interfaces
            if (tipo.BaseList != null && tipo.BaseList.Types.Any())
            {
                var herancas = tipo.BaseList.Types.Select(t => t.ToString().Trim());
                sbTipo.Append($" : {string.Join(", ", herancas)}");
            }

            // Captura Membros/Propriedades para dar contexto ao RAG
            var propriedades = (tipo as TypeDeclarationSyntax)?.Members
                            .OfType<PropertyDeclarationSyntax>()
                            .Select(p => $"{p.Type} {p.Identifier.Text}")
                            ?? Enumerable.Empty<string>();

            if (propriedades.Any())
            {
                sbTipo.Append($" [Props: {string.Join(", ", propriedades.Take(5))}{(propriedades.Count() > 5 ? "..." : "")}]");
            }

            estrutura.Add(sbTipo.ToString());
        }

        // Se não encontrou tipos declarados (ex: Top-Level Statements ou apenas Enums)
        if (!estrutura.Any())
        {
            var enums = root.DescendantNodes().OfType<EnumDeclarationSyntax>();
            if (enums.Any())
            {
                return "Enums: " + string.Join(", ", enums.Select(e => e.Identifier.Text));
            }

            return "Estrutura C# Geral / Script";
        }

        return string.Join(" | ", estrutura);
    }    
}

public class RagSearchHook
{
    RagHook rag = new RagHook();
    public string GerarPerguntaEstruturada(string perguntaUsuario, List<DocumentoVetorial> documentosRelevantes)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### 🤖 CONTEXTO DO PROJETO E SOLICITAÇÃO");
        sb.AppendLine("Você é um arquiteto de software e especialista em desenvolvimento C# / .NET.");
        sb.AppendLine("Abaixo estão fornecidos os trechos de código e a estrutura de metadados extraídos da base do projeto via RAG sintático (Roslyn).\n");

        sb.AppendLine("---");
        sb.AppendLine("### 📁 CONTEXTO E METADADOS DOS ARQUIVOS (ROSLYN RAG)\n");

        foreach (var doc in documentosRelevantes)
        {
            sb.AppendLine($"#### 📄 Arquivo: `{doc.CaminhoRelativo}`");

            // 1. Exibe o Tipo e o Membro/Método capturado pelo Roslyn
            if (!string.IsNullOrWhiteSpace(doc.TipoChunk) || !string.IsNullOrWhiteSpace(doc.NomeMembro))
            {
                sb.AppendLine($"> **Elemento:** `{doc.TipoChunk}` -> `{doc.NomeMembro}`");
            }

            // 2. Exibe a Estrutura / Metadados ricos (Namespaces, Heranças, Propriedades)
            if (!string.IsNullOrWhiteSpace(doc.Metadados))
            {
                sb.AppendLine($"> **Estrutura / Contrato:** `{doc.Metadados}`");
            }

            // 3. Bloco de código do Chunk
            sb.AppendLine("```csharp");
            sb.AppendLine(doc.ConteudoTexto);
            sb.AppendLine("```\n");
        }

        sb.AppendLine("---");
        sb.AppendLine("### ❓ PERGUNTA DO USUÁRIO");
        sb.AppendLine($"**\"{perguntaUsuario}\"**\n");

        sb.AppendLine("---");
        sb.AppendLine("### 🎯 INSTRUÇÕES DE RESPOSTA");
        sb.AppendLine("Responda de forma objetiva e técnica seguindo a estrutura:");
        sb.AppendLine("1. **Resumo Executivo:** Explicação direta de 2 a 3 frases sobre a solução proposta.");
        sb.AppendLine("2. **Análise de Contexto:** Como o problema se relaciona com os membros/estruturas fornecidos acima.");
        sb.AppendLine("3. **Impactos na Arquitetura:** Possíveis efeitos colaterais nos tipos ou contratos relacionados.");
        sb.AppendLine("4. **Código / Refatoração (se aplicável):** Implementação pronta respeitando os padrões do projeto.");

        return sb.ToString();
    }

    public float CalcularScorePonderado(
    DocumentoVetorial doc,
    float[] embeddingPergunta,
    string perguntaUsuario)
    {
        // 1. Similaridade Semântica de Vetores (Cos Sim Base)
        float baseCosineSim = CalcularSimilaridadeCosseno(doc.Embedding, embeddingPergunta);

        if (baseCosineSim <= 0f) return 0f;

        float bonusMetadados = 0f;
        string perguntaLower = perguntaUsuario.ToLowerInvariant();

        // 2. Pesos de Exatidão Léxica / Sintática via Roslyn

        // A) Bônus se o nome do método/classe aparece diretamente na pergunta
        if (!string.IsNullOrWhiteSpace(doc.NomeMembro) &&
            doc.NomeMembro.Length > 2 &&
            perguntaLower.Contains(doc.NomeMembro.ToLowerInvariant()))
        {
            bonusMetadados += 0.25f; // +25% de relevância
        }

        // B) Bônus se o namespace ou caminho hierárquico (Ex: RagScript.Hooks) é citado
        if (!string.IsNullOrWhiteSpace(doc.HierarquiaCompleta) &&
            doc.HierarquiaCompleta.Split('.').Any(parte => parte.Length > 3 && perguntaLower.Contains(parte.ToLowerInvariant())))
        {
            bonusMetadados += 0.15f; // +15% de relevância
        }

        // C) Bônus se palavras-chave de propriedades ou interfaces dos metadados combinam
        if (!string.IsNullOrWhiteSpace(doc.Metadados))
        {
            string metadadosLower = doc.Metadados.ToLowerInvariant();
            int matches = perguntaLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                       .Count(palavra => palavra.Length > 3 && metadadosLower.Contains(palavra));

            if (matches > 0)
            {
                bonusMetadados += Math.Min(matches * 0.05f, 0.15f); // Cap de +15%
            }
        }

        // Retorna o score combinado (com teto de 1.0)
        return Math.Min(baseCosineSim + bonusMetadados, 1.0f);
    }

    public float CalcularSimilaridadeCosseno(float[] vetorA, float[] vetorB)
    {
        if (vetorA == null || vetorB == null || vetorA.Length != vetorB.Length)
            return 0f;

        float dotProduct = 0f;
        float normaA = 0f;
        float normaB = 0f;

        for (int i = 0; i < vetorA.Length; i++)
        {
            dotProduct += vetorA[i] * vetorB[i];
            normaA += vetorA[i] * vetorA[i];
            normaB += vetorB[i] * vetorB[i];
        }

        if (normaA == 0f || normaB == 0f)
            return 0f;

        return dotProduct / ((float)Math.Sqrt(normaA) * (float)Math.Sqrt(normaB));
    }

    public async Task<float[]> GerarEmbeddingPerguntaAsync(string pergunta, string apiKey)
    {
        return await rag.GerarEmbeddingHttpAsync(pergunta, apiKey);
    }
}

public static class CodeChunker
{
    public class ChunkResult
    {
        public string Tipo { get; set; } = "Bloco";
        public string NomeMembro { get; set; } = string.Empty;
        public string HierarquiaCompleta { get; set; } = string.Empty;
        public string Conteudo { get; set; } = string.Empty;
    }

    public static List<ChunkResult> QuebrarCodigoCSharp(string codigo)
    {
        var chunks = new List<ChunkResult>();

        // 1. Traz a árvore sintática completa construída pelo Roslyn
        SyntaxTree tree = CSharpSyntaxTree.ParseText(codigo);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        // 2. Localiza todos os métodos do arquivo
        var methodNodes = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

        // Fallback: Se não encontrar métodos (ex: DTOs puras, Enums, Structs de dados), captura classes/structs/records
        if (!methodNodes.Any())
        {
            var typeNodes = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();
            foreach (var typeNode in typeNodes)
            {
                chunks.Add(new ChunkResult
                {
                    Tipo = typeNode.Kind().ToString().Replace("Declaration", ""),
                    NomeMembro = typeNode.Identifier.Text,
                    HierarquiaCompleta = ObterCaminhoHierarquico(typeNode),
                    Conteudo = typeNode.ToFullString().Trim()
                });
            }

            // Se for um arquivo script isolado sem nada declarado
            if (!chunks.Any() && !string.IsNullOrWhiteSpace(codigo))
            {
                chunks.Add(new ChunkResult
                {
                    Tipo = "Arquivo/Estrutura",
                    NomeMembro = "Geral",
                    HierarquiaCompleta = "Geral",
                    Conteudo = codigo
                });
            }

            return chunks;
        }

        // 3. Extrai cada método preservando o escopo correto e comentários (xml docs / trivia)
        foreach (var method in methodNodes)
        {
            string nomeMetodo = method.Identifier.Text;
            string hierarquia = ObterCaminhoHierarquico(method);

            chunks.Add(new ChunkResult
            {
                Tipo = "Metodo",
                NomeMembro = nomeMetodo,
                HierarquiaCompleta = hierarquia,
                // GetText() ou ToFullString() preservam comentários XML em cima do método
                Conteudo = method.ToFullString().Trim()
            });
        }

        return chunks;
    }

    /// <summary>
    /// Reconstrói o caminho sintático do membro (Ex: Meunamespace.MinhaClasse.MeuMetodo)
    /// </summary>
    private static string ObterCaminhoHierarquico(SyntaxNode node)
    {
        var nomes = new List<string>();
        var atual = node;

        while (atual != null)
        {
            if (atual is MethodDeclarationSyntax method)
                nomes.Add(method.Identifier.Text);
            else if (atual is BaseTypeDeclarationSyntax type)
                nomes.Add(type.Identifier.Text);
            else if (atual is BaseNamespaceDeclarationSyntax ns)
                nomes.Add(ns.Name.ToString());

            atual = atual.Parent;
        }

        nomes.Reverse();
        return string.Join(".", nomes);
    }
}
