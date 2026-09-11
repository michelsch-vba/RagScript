using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RagScript.Hooks;
using RagScript.IndexerConsole;
using RagScript.Models;
using RagScript.Services;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RagScript.Services.Chunker;

namespace RagScript.Hooks
{
    public class Options
    {
        public List<string> Keys { get; set; } = new();
    }

    public class ApiHook
    {
        public readonly HttpClient _httpClient = new HttpClient();
        private readonly List<string> _apiKeys = new();
        private readonly Dictionary<string, DateTime> _cooldowns = new();
        private int _chaveIndex = 0;
        private readonly object _lockObject = new object();

        public async Task GarantirChavesCarregadasAsync()
        {
            lock (_lockObject)
            {
                if (_apiKeys.Count > 0) return;
            }

            string pastaApp = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RagKey");
            string arquivo = Path.Combine(pastaApp, "key.json");

            if (System.IO.File.Exists(arquivo))
            {
                try
                {
                    string conteudo = await System.IO.File.ReadAllTextAsync(arquivo);
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var config = JsonSerializer.Deserialize<Options>(conteudo, options);

                    if (config?.Keys != null && config.Keys.Count > 0)
                    {
                        CarregarChaves(config.Keys);
                    }
                }
                catch
                {
                    // Silencioso em falhas de leitura
                }
            }
        }

        public void CarregarChaves(IEnumerable<string> chaves)
        {
            lock (_lockObject)
            {
                _apiKeys.Clear();
                _apiKeys.AddRange(chaves.Where(k => !string.IsNullOrWhiteSpace(k)));
                _chaveIndex = 0;
            }
        }

        internal string? ObterProximaChaveValida()
        {
            lock (_lockObject)
            {
                if (_apiKeys.Count == 0) return null;

                int tentativas = 0;
                while (tentativas < _apiKeys.Count)
                {
                    string chave = _apiKeys[_chaveIndex];
                    _chaveIndex = (_chaveIndex + 1) % _apiKeys.Count;

                    if (_cooldowns.TryGetValue(chave, out DateTime bloqueadaAte))
                    {
                        if (DateTime.UtcNow < bloqueadaAte)
                        {
                            tentativas++;
                            continue;
                        }
                        _cooldowns.Remove(chave);
                    }

                    return chave;
                }

                return null;
            }
        }

        internal void BloquearChave(string chave, TimeSpan tempo)
        {
            lock (_lockObject)
            {
                _cooldowns[chave] = DateTime.UtcNow.Add(tempo);
            }
        }

        public async Task<List<string>?> ObteroudarKeysAsync()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string pastaApp = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RagKey");
            Directory.CreateDirectory(pastaApp);
            string arquivo = Path.Combine(pastaApp, "key.json");

            List<string> chavesCarregadas = new();

            if (System.IO.File.Exists(arquivo))
            {
                try
                {
                    string conteudo = await System.IO.File.ReadAllTextAsync(arquivo);
                    if (!string.IsNullOrWhiteSpace(conteudo))
                    {
                        var config = JsonSerializer.Deserialize<Options>(conteudo, options);

                        if (config?.Keys != null && config.Keys.Count > 0)
                        {
                            chavesCarregadas = config.Keys;
                            Console.WriteLine($"\n🔑 {chavesCarregadas.Count} chave(s) encontrada(s) no sistema:");

                            foreach (var k in chavesCarregadas)
                            {
                                bool eValida = await TestarApiKeyAsync(k);
                                string status = eValida ? "✅ Válida" : "❌ Inválida";
                                Console.WriteLine($" - {MascararKey(k)} - {status}");
                            }

                            Console.WriteLine("---------------------------------------------");
                            Console.WriteLine("[1] Usar as chaves atuais");
                            Console.WriteLine("[2] Gerenciar/Cadastrar chaves");
                            Console.WriteLine("[3] Voltar");
                            Console.Write("Escolha uma opção: ");

                            string opcao = Console.ReadLine()?.Trim() ?? string.Empty;

                            if (opcao == "1")
                            {
                                CarregarChaves(chavesCarregadas);
                                return chavesCarregadas;
                            }
                            else if (opcao == "2")
                            {
                                var novasChaves = await CadastrarNovasChaves(chavesCarregadas);
                                if (novasChaves?.Any() == true)
                                {
                                    CarregarChaves(novasChaves);
                                    return novasChaves;
                                }
                            }
                            else
                            {
                                Console.WriteLine("Mantendo o fluxo sem alterações nas chaves.");
                                return chavesCarregadas;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao ler configurações: {ex.Message}");
                }
            }

            Console.WriteLine("\nNenhuma chave configurada. É necessário cadastrar pelo menos uma.");
            chavesCarregadas = await NovasChaves(obrigatorio: true);
            CarregarChaves(chavesCarregadas);
            await SalvarChavesEmArquivoAsync(chavesCarregadas);

            return chavesCarregadas;
        }

        private async Task<List<string>?> CadastrarNovasChaves(List<string> listaAtual)
        {
            string[] comandosSair = { "sair", "terminar", "fechar", "exit", "cancelar" };

            Console.WriteLine("------------------------------------------------------");
            Console.WriteLine("\nEscolha uma das opções ou digite 'sair':");
            Console.WriteLine("[1] Cadastrar uma lista nova de chaves (Sobrescrever)");
            Console.WriteLine("[2] Mudar/Substituir uma chave específica");
            Console.WriteLine("[3] Deletar uma chave específica");
            Console.WriteLine("[4] Limpar todas as chaves inválidas automaticamente");

            while (true)
            {
                Console.Write("\nEscolha uma opção: ");
                string opcao = Console.ReadLine()?.Trim() ?? string.Empty;

                if (comandosSair.Contains(opcao, StringComparer.OrdinalIgnoreCase))
                {
                    return listaAtual;
                }

                if (opcao == "1")
                {
                    var novasChaves = await NovasChaves();
                    if (!novasChaves.Any())
                    {
                        Console.WriteLine("Nenhuma nova chave cadastrada. Operação cancelada.");
                        return listaAtual;
                    }
                    await SalvarChavesEmArquivoAsync(novasChaves);
                    return novasChaves;
                }
                else if (opcao == "2")
                {
                    return await MudarChaves(listaAtual);
                }
                else if (opcao == "3")
                {
                    return await DeletarChave(listaAtual);
                }
                else if (opcao == "4")
                {
                    return await DeletarChavesInvalidasAsync(listaAtual);
                }             
                else
                {
                    Console.WriteLine("Opção inválida.");
                }
            }
        }

        private async Task<List<string>> NovasChaves(bool obrigatorio = false)
        {
            List<string> novasChaves = new();
            int numeroDeChaves = 1;
            string[] comandosSair = { "sair", "terminar", "fechar", "exit", "cancelar" };

            Console.WriteLine("---------------------------------------");
            Console.WriteLine("Digite a chave de API. Ao terminar, digite 'fim'. (Ou 'sair' para cancelar)\n");

            while (true)
            {
                Console.Write($"Digite a {numeroDeChaves}ª chave: ");
                string output = Console.ReadLine()?.Trim() ?? string.Empty;

                if (output.Equals("fim", StringComparison.OrdinalIgnoreCase))
                {
                    if (novasChaves.Count == 0)
                    {
                        Console.WriteLine("Cadastre pelo menos uma chave válida ou digite 'sair'.");
                        continue;
                    }
                    break;
                }

                if (comandosSair.Contains(output, StringComparer.OrdinalIgnoreCase))
                {
                    if (obrigatorio && novasChaves.Count == 0)
                    {
                        Console.WriteLine("O cadastro de ao menos uma chave é obrigatório.");
                        continue;
                    }
                    novasChaves.Clear();
                    break;
                }

                if (string.IsNullOrEmpty(output))
                {
                    Console.WriteLine("Entrada inválida.");
                    continue;
                }

                Console.WriteLine("Testando chave...");
                if (!await TestarApiKeyAsync(output))
                {
                    Console.WriteLine("❌ Chave inválida ou sem conexão com a API do Gemini. Tente novamente.");
                    continue;
                }

                Console.WriteLine("✅ Chave válida adicionada!");
                novasChaves.Add(output);
                numeroDeChaves++;
            }

            return novasChaves;
        }

        private async Task<List<string>> MudarChaves(List<string> lista)
        {
            string[] comandosSair = { "sair", "terminar", "fechar", "exit", "cancelar" };

            if (!lista.Any())
            {
                Console.WriteLine("Nenhuma chave cadastrada para alterar.");
                return await NovasChaves(obrigatorio: true);
            }

            await ExibirStatusChavesAsync(lista);

            while (true)
            {
                Console.Write("\nDigite o número da chave que deseja alterar (ou 'sair'): ");
                string output = Console.ReadLine()?.Trim() ?? string.Empty;

                if (comandosSair.Contains(output, StringComparer.OrdinalIgnoreCase))
                    break;

                if (int.TryParse(output, out int index) && index > 0 && index <= lista.Count)
                {
                    Console.Write("Digite a nova chave de API: ");
                    string novaChave = Console.ReadLine()?.Trim() ?? string.Empty;

                    if (!string.IsNullOrEmpty(novaChave) && await TestarApiKeyAsync(novaChave))
                    {
                        lista[index - 1] = novaChave;
                        Console.WriteLine($"✅ Chave na posição {index} alterada com sucesso.");
                        await SalvarChavesEmArquivoAsync(lista);
                        return lista;
                    }

                    Console.WriteLine("❌ Chave digitada é inválida.");
                }
                else
                {
                    Console.WriteLine("Posição inválida.");
                }
            }

            return lista;
        }

        private async Task<List<string>> DeletarChave(List<string> lista)
        {
            string[] comandosSair = { "sair", "terminar", "fechar", "exit", "cancelar" };

            if (!lista.Any())
            {
                Console.WriteLine("Nenhuma chave na lista para remover.");
                return lista;
            }

            await ExibirStatusChavesAsync(lista);

            while (true)
            {
                Console.Write("\nDigite o número da chave que deseja deletar (ou 'sair'): ");
                string output = Console.ReadLine()?.Trim() ?? string.Empty;

                if (comandosSair.Contains(output, StringComparer.OrdinalIgnoreCase))
                    break;

                if (int.TryParse(output, out int index) && index > 0 && index <= lista.Count)
                {
                    lista.RemoveAt(index - 1);
                    Console.WriteLine($"✅ Chave removida com sucesso.");
                    await SalvarChavesEmArquivoAsync(lista);
                    return lista;
                }

                Console.WriteLine("Posição inválida.");
            }

            return lista;
        }

        private async Task<List<string>> DeletarChavesInvalidasAsync(List<string> lista)
        {
            Console.WriteLine("\nVerificando e removendo chaves inválidas...");
            List<string> chavesValidas = new();

            foreach (var key in lista)
            {
                if (await TestarApiKeyAsync(key))
                {
                    chavesValidas.Add(key);
                }
                else
                {
                    Console.WriteLine($"❌ Chave removida por ser inválida: {MascararKey(key)}");
                }
            }

            await SalvarChavesEmArquivoAsync(chavesValidas);
            return chavesValidas;
        }

        private async Task ExibirStatusChavesAsync(List<string> lista)
        {
            Console.WriteLine("\n--- Status das Chaves ---");
            for (int i = 0; i < lista.Count; i++)
            {
                bool eValida = await TestarApiKeyAsync(lista[i]);
                string status = eValida ? "✅ Válida" : "❌ Inválida";
                Console.WriteLine($"[{i + 1}] {MascararKey(lista[i])} - {status}");
            }
        }

        private async Task SalvarChavesEmArquivoAsync(List<string> chaves)
        {
            string pastaApp = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RagKey");
            string arquivo = Path.Combine(pastaApp, "key.json");

            Directory.CreateDirectory(pastaApp);

            var config = new Options { Keys = chaves };
            var options = new JsonSerializerOptions { WriteIndented = true };
            string conteudo = JsonSerializer.Serialize(config, options);

            await System.IO.File.WriteAllTextAsync(arquivo, conteudo);
        }

        public async Task<bool> TestarApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return false;
            try
            {
                using var client = new HttpClient();
                string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
                var response = await client.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        internal string MascararKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length <= 8) return "****";
            return $"{key[..6]}...{key[^4..]}";
        }
    }

    public class RagHook
    {
        private readonly ApiHook _apiHook;
     

        public RagHook()
        {
            _apiHook = new ApiHook();
            
        }

        public async Task InicializarChavesAsync()
        {
            await _apiHook.ObteroudarKeysAsync();
        }

        private static readonly HashSet<string> PastasIgnoradas = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".vs", ".git", ".idea", "node_modules", "packages"
        };

        private static readonly HashSet<string> ExtensoesBinariasIgnoradas = new(StringComparer.OrdinalIgnoreCase)
        {
            ".db", ".sqlite", ".db-shm", ".db-wal", ".ttf", ".otf", ".png", ".jpg", ".jpeg", ".dll", ".exe", ".pdb"
        };

        public async Task ProcessarAtualizacaoIncrementalAsync(
            string caminhoPasta,
            List<string> extensoes,
            string caminhoBancoExistente)
        {
            await InicializarChavesAsync();

            var motor = new MotorBuscaRAG(caminhoBancoExistente);
            var sqliteRepo = new SqliteVectorRepository(caminhoBancoExistente);

            // 1. Carrega os hashes do SQLite
            var hashesNoBanco = motor.ObterHashesPorCaminho();

            // 2. Lista os arquivos do disco
            var arquivosNoDisco = ObterArquivosValidos(caminhoPasta)
                .Where(f => extensoes.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            var caminhosNoDiscoRelativos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int novosOuModificados = 0;
            int ignoradosSemAlteracao = 0;

            foreach (var arquivo in arquivosNoDisco)
            {
                string ext = Path.GetExtension(arquivo).ToLower();
                if (ExtensoesBinariasIgnoradas.Contains(ext)) continue;

                try
                {
                    string conteudo = await System.IO.File.ReadAllTextAsync(arquivo);
                    if (string.IsNullOrWhiteSpace(conteudo)) continue;

                    string caminhoRelativo = Path.GetRelativePath(caminhoPasta, arquivo);
                    caminhosNoDiscoRelativos.Add(caminhoRelativo);

                    string hashAtualDisco = GerarHashSHA256(conteudo);

                    // Se o arquivo não mudou, pula a vetorização
                    if (hashesNoBanco.TryGetValue(caminhoRelativo, out string? hashNoBanco) && hashNoBanco == hashAtualDisco)
                    {
                        ignoradosSemAlteracao++;
                        continue;
                    }

                    AnsiConsole.MarkupLine($"\n[yellow]🔄 Arquivo alterado/novo detectado:[/] [bold white]{caminhoRelativo}[/]");
                    novosOuModificados++;

                    string metadadosExtraidos = ext switch
                    {
                        ".cs" => ExtrairEstruturaCSharp(conteudo),
                        ".xaml" => ExtrairEstruturaXaml(conteudo),
                        _ => $"Arquivo {ext}"
                    };

                    var pedacos = ext switch
                    {
                        ".cs" => Csharp_Chunker.QuebrarCodigoCSharp(conteudo),
                        ".xaml" => Xaml_Chunker.QuebrarCodigoXaml(conteudo, Path.GetFileName(arquivo)),
                        ".slnx" => Sln_Chunker.QuebrarCodigoSln(conteudo, Path.GetFileName(arquivo)),
                        ".csproj" => Csproj_Chunker.QuebrarCodigoCsproj(conteudo, Path.GetFileName(arquivo)),
                        ".editorconfig" => EditorConfig_Chunker.QuebrarCodigoEditorConfig(conteudo, Path.GetFileName(arquivo)),
                        _ => new List<ChunkResult>
                {
                    new ChunkResult
                    {
                        Tipo = "Documento",
                        NomeMembro = Path.GetFileName(arquivo),
                        Conteudo = conteudo
                    }
                }
                    };

                    var documentosParaInserir = new List<DocumentoVetorial>();

                    // 💡 BARRA DE PROGRESSO DO SPECTRE.CONSOLE
                    await AnsiConsole.Progress()
                        .AutoClear(false)
                        .Columns(new ProgressColumn[]
                        {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn()
                        })
                        .StartAsync(async ctx =>
                        {
                            var task = ctx.AddTask($"[cyan]Vetorizando {Path.GetFileName(arquivo)}[/]", maxValue: pedacos.Count);

                            foreach (var chunk in pedacos)
                            {
                                task.Description = $"[cyan]Chunk:[/] [green]{chunk.NomeMembro}[/]";

                                string docXml = string.IsNullOrWhiteSpace(chunk.DocumentacaoXml)
                                    ? string.Empty
                                    : $"[DOC XML]:\n{chunk.DocumentacaoXml}\n";

                                string textoParaEmbedding = $"[ARQUIVO: {caminhoRelativo}]\n" +
                                                            $"[HIERARQUIA: {chunk.HierarquiaCompleta}]\n" +
                                                            $"[TIPO/MEMBRO: {chunk.Tipo} -> {chunk.NomeMembro}]\n" +
                                                            $"[ESTRUTURA/METADADOS: {metadadosExtraidos}]\n" +
                                                            docXml +
                                                            $"\n[CÓDIGO/CONTEÚDO]:\n{chunk.Conteudo}";

                                float[] vectorValues = Array.Empty<float>();
                                int tentativaChunk = 0;

                                while (vectorValues.Length == 0 && tentativaChunk < 3)
                                {
                                    tentativaChunk++;
                                    vectorValues = await GerarEmbeddingHttpAsync(textoParaEmbedding);

                                    if (vectorValues.Length == 0 && tentativaChunk < 3)
                                    {
                                        await Task.Delay(2000);
                                    }
                                }

                                if (vectorValues.Length > 0)
                                {
                                    string idUnicoChunk = GerarHashSHA256($"{caminhoRelativo}_{chunk.HierarquiaCompleta}_{chunk.NomeMembro}");

                                    documentosParaInserir.Add(new DocumentoVetorial
                                    {
                                        IdChunk = idUnicoChunk,
                                        CaminhoRelativo = caminhoRelativo,
                                        NomeArquivo = Path.GetFileName(arquivo),
                                        HashConteudo = hashAtualDisco,
                                        TipoChunk = chunk.Tipo,
                                        NomeMembro = chunk.NomeMembro,
                                        HierarquiaCompleta = chunk.HierarquiaCompleta,
                                        Metadados = metadadosExtraidos,
                                        ConteudoTexto = chunk.Conteudo,
                                        Embedding = vectorValues
                                    });
                                }

                                task.Increment(1);
                                await Task.Delay(1200); // Delay seguro entre chamadas da API
                            }
                        });

                    // Insere no banco SQLite os vetores atualizados do arquivo
                    if (documentosParaInserir.Any())
                    {
                        await sqliteRepo.InserirDocumentosVetoriaisAsync(documentosParaInserir);
                        AnsiConsole.MarkupLine($"  [green]✅ {documentosParaInserir.Count} chunks atualizados com sucesso no SQLite![/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]❌ Erro ao processar arquivo {Path.GetFileName(arquivo)}: {ex.Message}[/]");
                }
            }

            // 3. Remoção de arquivos excluídos
            var arquivosDeletados = hashesNoBanco.Keys.Except(caminhosNoDiscoRelativos).ToList();
            foreach (var arqDeletado in arquivosDeletados)
            {
                AnsiConsole.MarkupLine($"[red]🗑️ Removendo do RAG arquivo deletado:[/] {arqDeletado}");
                await motor.RemoverChunksPorCaminhoAsync(arqDeletado);
            }

            // Painel de Resumo Final
            var panel = new Spectre.Console.Panel(
                $"⚡ [bold green]Arquivos Inalterados:[/] {ignoradosSemAlteracao}\n" +
                $"🔄 [bold yellow]Arquivos Atualizados/Novos:[/] {novosOuModificados}\n" +
                $"🗑️ [bold red]Arquivos Removidos:[/] {arquivosDeletados.Count}")
            {
                Header = new PanelHeader("[bold white]🎉 RESUMO DA ATUALIZAÇÃO INCREMENTAL[/]"),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(panel);
        }

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
            int tamanhoPrefixo = caminhoPasta.Length;
            if (!caminhoPasta.EndsWith(Path.DirectorySeparatorChar) &&
                !caminhoPasta.EndsWith(Path.AltDirectorySeparatorChar))
            {
                tamanhoPrefixo++;
            }

            foreach (var arquivo in Directory.EnumerateFiles(caminhoPasta, "*.*", SearchOption.AllDirectories))
            {
                ReadOnlySpan<char> caminhoRelativo = arquivo.AsSpan(Math.Min(tamanhoPrefixo, arquivo.Length));

                if (!CaminhoContemPastaIgnorada(caminhoRelativo))
                {
                    yield return arquivo;
                }
            }
        }

        private static bool CaminhoContemPastaIgnorada(ReadOnlySpan<char> caminho)
        {
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

        public async Task<List<DocumentoVetorial>> ProcessarArquivosERagAsync(string caminhoPasta, List<string> extensoes)
        {
            await InicializarChavesAsync(); // Garante a leitura síncrona/aguardada das chaves
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
                    string metadadosExtraidos = ext switch
                    {
                        ".cs" => ExtrairEstruturaCSharp(conteudo),
                        ".xaml" => ExtrairEstruturaXaml(conteudo),
                        _ => $"Arquivo {ext}"
                    };

                    var pedacos = ext switch
                    {
                        ".cs" => Csharp_Chunker.QuebrarCodigoCSharp(conteudo),
                        ".xaml" => Xaml_Chunker.QuebrarCodigoXaml(conteudo, Path.GetFileName(arquivo)),
                        ".slnx" => Sln_Chunker.QuebrarCodigoSln(conteudo, Path.GetFileName(arquivo)),
                        ".csproj" => Csproj_Chunker.QuebrarCodigoCsproj(conteudo, Path.GetFileName(arquivo)),
                        ".editorconfig" => EditorConfig_Chunker.QuebrarCodigoEditorConfig(conteudo, Path.GetFileName(arquivo)),
                        _ => new List<ChunkResult>
                {
                    new ChunkResult
                    {
                        Tipo = "Documento",
                        NomeMembro = Path.GetFileName(arquivo),
                        Conteudo = conteudo
                    }
                }
                    };

                    foreach (var chunk in pedacos)
                    {
                        // Constrói o cabeçalho de documentação apenas se existir documentação XML no chunk
                        string docXml = string.IsNullOrWhiteSpace(chunk.DocumentacaoXml)
                            ? string.Empty
                            : $"[DOC XML]:\n{chunk.DocumentacaoXml}\n";

                        // Formatação rica incluindo HierarquiaCompleta e DocumentacaoXml de ChunkResult
                        string textoParaEmbedding = $"[ARQUIVO: {caminhoRelativo}]\n" +
                                                    $"[HIERARQUIA: {chunk.HierarquiaCompleta}]\n" +
                                                    $"[TIPO/MEMBRO: {chunk.Tipo} -> {chunk.NomeMembro}]\n" +
                                                    $"[ESTRUTURA/METADADOS: {metadadosExtraidos}]\n" +
                                                    docXml +
                                                    $"\n[CÓDIGO/CONTEÚDO]:\n{chunk.Conteudo}";

                        Console.Write($"🔄 Vetorizando Chunk ({chunk.HierarquiaCompleta}) em {caminhoRelativo}... ");

                        // Loop de Resiliência: até 3 tentativas por chunk antes de descartar
                        float[] vectorValues = Array.Empty<float>();
                        int tentativaChunk = 0;

                        while (vectorValues.Length == 0 && tentativaChunk < 3)
                        {
                            tentativaChunk++;
                            vectorValues = await GerarEmbeddingHttpAsync(textoParaEmbedding);

                            if (vectorValues.Length == 0 && tentativaChunk < 3)
                            {
                                Console.WriteLine($"\n⚠️ Tentativa {tentativaChunk} falhou. Re-tentando chunk em 2s...");
                                await Task.Delay(2000);
                            }
                        }

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
                                ConteudoTexto = chunk.Conteudo,
                                Embedding = vectorValues
                            });

                            Console.WriteLine("✅ OK");
                        }
                        else
                        {
                            Console.WriteLine($"❌ Chunk [{chunk.NomeMembro}] descartado após 3 tentativas.");
                        }

                        await Task.Delay(200); // Delay seguro de 200ms entre vetorizações
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erro ao processar {Path.GetFileName(arquivo)}: {ex.Message}");
                }
            }

            return documentosVetoriais;
        }

        public async Task<float[]> GerarEmbeddingHttpAsync(string texto)
        {
            // 💡 Mudança Fundamental: Garante as chaves sem solicitar interação do usuário
            await _apiHook.GarantirChavesCarregadasAsync();

            int tentativamax = 5;

            for (int tentativa = 1; tentativa <= tentativamax; tentativa++)
            {
                string? apiKey = _apiHook.ObterProximaChaveValida();

                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("\n⚠️ Todas as chaves do pool estão bloqueadas/temporariamente sem cota. Aguardando 15s...");
                    await Task.Delay(15000);
                    continue;
                }

                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={apiKey}";
                var payload = new
                {
                    content = new
                    {
                        parts = new[] { new { text = texto } }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };

                    using var response = await _apiHook._httpClient.SendAsync(request);

                    if ((int)response.StatusCode == 429)
                    {
                        Console.WriteLine($"\n⚠️ Rate Limit (429) na chave [{_apiHook.MascararKey(apiKey)}]. Ativando cooldown de 60s e alternando chave...");
                        _apiHook.BloquearChave(apiKey, TimeSpan.FromSeconds(60));
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

            var namespaceNode = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            if (namespaceNode != null)
            {
                estrutura.Add($"Namespace: {namespaceNode.Name}");
            }

            var tipos = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();

            foreach (var tipo in tipos)
            {
                var sbTipo = new StringBuilder();

                string tipoKIND = tipo.Kind().ToString().Replace("Declaration", "").ToLower();
                sbTipo.Append($"{tipoKIND} {tipo.Identifier.Text}");

                if (tipo.BaseList != null && tipo.BaseList.Types.Any())
                {
                    var herancas = tipo.BaseList.Types.Select(t => t.ToString().Trim());
                    sbTipo.Append($" : {string.Join(", ", herancas)}");
                }

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

        private string ExtrairEstruturaXaml(string conteudoXaml)
        {
            if (string.IsNullOrWhiteSpace(conteudoXaml))
                return "Arquivo XAML Vazio";

            try
            {
                XElement root = XElement.Parse(conteudoXaml);
                XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

                var estrutura = new List<string>();

                // 1. Tipo do Nó Raiz (Window, UserControl, ResourceDictionary, Application)
                string tipoRaiz = root.Name.LocalName;
                estrutura.Add($"TipoRaiz: {tipoRaiz}");

                // 2. Vínculo com Code-Behind (x:Class)
                string? classeVinculada = root.Attribute(xamlNs + "Class")?.Value;
                if (!string.IsNullOrEmpty(classeVinculada))
                {
                    estrutura.Add($"CodeBehind: {classeVinculada}");
                }

                // 3. Em caso de ResourceDictionary
                if (tipoRaiz == "ResourceDictionary")
                {
                    var qtdRecursos = root.Elements().Count();
                    estrutura.Add($"RecursosTotais: {qtdRecursos}");
                    return string.Join(" | ", estrutura);
                }

                // 4. Captura Namespaces e Assemblies Importados (ex: xmlns:vms, xmlns:views)
                var namespacesCustomizados = root.Attributes()
                    .Where(a => a.IsNamespaceDeclaration && a.Name.LocalName != "xmlns" && a.Name.LocalName != "x")
                    .Select(a => $"{a.Name.LocalName}->{a.Value.Split(';').First().Replace("clr-namespace:", "")}");

                if (namespacesCustomizados.Any())
                {
                    estrutura.Add($"Imports: [{string.Join(", ", namespacesCustomizados.Take(4))}]");
                }

                // 5. Resumo de Controles Principais e Nomeados da Tela (máximo 6 para não estourar o resumo)
                var controlesNomeados = root.Descendants()
                    .Select(e => new
                    {
                        Tipo = e.Name.LocalName,
                        Nome = e.Attribute(xamlNs + "Name")?.Value ?? e.Attribute("Name")?.Value
                    })
                    .Where(x => !string.IsNullOrEmpty(x.Nome))
                    .Select(x => $"{x.Tipo}:{x.Nome}")
                    .Take(6);

                if (controlesNomeados.Any())
                {
                    estrutura.Add($"ControlesChave: [{string.Join(", ", controlesNomeados)}]");
                }

                return string.Join(" | ", estrutura);
            }
            catch
            {
                return "Estrutura XAML (Falha no Parse XML)";
            }
        }
    }

    public class RagSearchHook
    {
        private readonly ApiHook _apiHook = new ApiHook();
        private readonly RagHook _ragHook = new RagHook();

        public async Task InicializarChavesAsync()
        {
            // 💡 Garante que as chaves estão em memória sem abrir o menu do Console!
            await _apiHook.GarantirChavesCarregadasAsync();
        }

        public async Task<float[]> GerarEmbeddingPerguntaAsync(string pergunta)
        {
            await InicializarChavesAsync();
            return await _ragHook.GerarEmbeddingHttpAsync(pergunta);
        }

        public string GerarPerguntaEstruturada(
            string perguntaUsuario,
            List<DocumentoVetorial> documentosRelevantes,
            TipoPreset preset = TipoPreset.ArquiteturaERefatoracao)
        {
            var sb = new StringBuilder();

            sb.AppendLine("### 🤖 CONTEXTO DO PROJETO E SOLICITAÇÃO");
            sb.AppendLine("Abaixo estão fornecidos os trechos de código e a estrutura de metadados extraídos da base do projeto via RAG sintático (Roslyn).\n");

            sb.AppendLine("---");
            sb.AppendLine("### 📁 CONTEXTO E METADADOS DOS ARQUIVOS (ROSLYN RAG)\n");

            foreach (var doc in documentosRelevantes)
            {
                sb.AppendLine($"#### 📄 Arquivo: `{doc.CaminhoRelativo}`");

                if (!string.IsNullOrWhiteSpace(doc.TipoChunk) || !string.IsNullOrWhiteSpace(doc.NomeMembro))
                {
                    sb.AppendLine($"> **Elemento:** `{doc.TipoChunk}` -> `{doc.NomeMembro}`");
                }

                if (!string.IsNullOrWhiteSpace(doc.Metadados))
                {
                    sb.AppendLine($"> **Estrutura / Contrato:** `{doc.Metadados}`");
                }

                sb.AppendLine("```csharp");
                sb.AppendLine(doc.ConteudoTexto);
                sb.AppendLine("```\n");
            }

            sb.AppendLine("---");
            sb.AppendLine("### ❓ PERGUNTA / SOLICITAÇÃO DO USUÁRIO");
            sb.AppendLine($"**\"{perguntaUsuario}\"**\n");

            sb.AppendLine("---");
            sb.AppendLine("### 🎯 INSTRUÇÕES DE RESPOSTA");

            switch (preset)
            {
                case TipoPreset.SugestaoMelhoriaPerformance:
                    sb.AppendLine("Atue como um Engenheiro de Performance C#/.NET. Responda seguindo a estrutura:");
                    sb.AppendLine("1. **Gargalos Identificados:** Análise sintática/algorítmica de possíveis pontos de lentidão ou alocação excessiva de memória (GC).");
                    sb.AppendLine("2. **Otimizações Propostas:** Sugestões de uso de Span<T>, Memory<T>, async/await ou estruturas de dados mais adequadas.");
                    sb.AppendLine("3. **Código Refatorado:** Versão otimizada mantendo os mesmos contratos públicos.");
                    break;

                case TipoPreset.DocumentacaoTecnica:
                    sb.AppendLine("Atue como um Technical Writer especializado em .NET. Responda seguindo a estrutura:");
                    sb.AppendLine("1. **Visão Geral:** Resumo do propósito funcional das classes/métodos citados.");
                    sb.AppendLine("2. **Código em bloco de código**: forneça o código da classe, metodo ou etc para ser visualizado melhor, sem xml docs somente comentarios se necessários");
                    sb.AppendLine("3. **Diagrama de Fluxo (Mermaid):** Exemplo visual básico do fluxo de execução, se aplicável.");
                    break;

                case TipoPreset.AnaliseDeBugsESeguranca:
                    sb.AppendLine("Atue como um Especialista em Code Review e AppSec. Responda seguindo a estrutura:");
                    sb.AppendLine("1. **Vulnerabilidades / Code Smells:** Identificação de falhas de concorrência, vazamento de recursos ou exceções não tratadas.");
                    sb.AppendLine("2. **Plano de Mitigação:** Passo a passo para corrigir os riscos sem quebrar o sistema.");
                    sb.AppendLine("3. **Código Corrigido:** Implementação segura com tratamento rigoroso de borda.");
                    break;

                case TipoPreset.CriacaoDeTestesUnitarios:
                    sb.AppendLine("Atue como um Engenheiro de QA e Automação .NET. Responda seguindo a estrutura:");
                    sb.AppendLine("1. **Cenários de Teste (AAA):** Lista de cenários positivos, negativos e de borda a serem testados.");
                    sb.AppendLine("2. **Código de Teste:** Suíte completa usando xUnit/NUnit com Moq/NSubstitute e FluentAssertions.");
                    break;

                case TipoPreset.ArquiteturaERefatoracao:
                default:
                    sb.AppendLine("Atue como um Arquiteto de Software C#/.NET. Responda de forma objetiva e técnica seguindo a estrutura:");
                    sb.AppendLine("1. **Resumo Executivo:** Explicação direta de 2 a 3 frases sobre a solução proposta.");
                    sb.AppendLine("2. **Análise de Contexto:** Como o problema se relaciona com os membros/estruturas fornecidos acima.");
                    sb.AppendLine("3. **Impactos na Arquitetura:** Possíveis efeitos colaterais nos tipos ou contratos relacionados.");
                    sb.AppendLine("4. **Código / Refatoração:** Implementação pronta respeitando os padrões do projeto.");
                    break;
            }

            return sb.ToString();
        }

    }



    

    
}



