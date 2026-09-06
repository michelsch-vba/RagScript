using Google.GenAI;
using RagScript.Hooks;
using RagScript.IndexerConsole;
using RagScript.Models;
using RagScript.Services;
using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace RagScript.Funções
{
    public static class Functions
    {
        public static async Task<List<string>> GerenciarApiKeyAsync()
        {
            ApiHook apiHook = new ApiHook();
            List<string>? keys = await apiHook.ObteroudarKeysAsync();

            if (keys == null || !keys.Any())
            {
                Console.WriteLine("⚠️ Nenhuma chave foi selecionada.");
                return new List<string>();
            }

            Console.WriteLine("\n🔍 Testando conectividade do pool de chaves...");
            int chavesValidas = 0;

            foreach (var key in keys)
            {
                bool conexaoOk = await apiHook.TestarApiKeyAsync(key);
                if (conexaoOk)
                {
                    chavesValidas++;
                }
            }

            Console.WriteLine($"\n📊 Resultado Heartbeat: {chavesValidas}/{keys.Count} chaves ativas e operacionais!");
            return keys;
        }

        public static async Task GerarRagCompletoAsync()
        {
            RagHook ragHook = new RagHook();

            Console.WriteLine("\n📂 Selecione a pasta contendo os arquivos na janela do Windows...");
            string pastaOrigem = ragHook.SelecionarPastaOrigem();

            if (string.IsNullOrEmpty(pastaOrigem))
            {
                Console.WriteLine("⚠️ Nenhuma pasta selecionada. Operação cancelada.");
                return;
            }

            Console.WriteLine($"📌 Pasta selecionada: {pastaOrigem}");

            List<string> extensoesEscolhidas = ragHook.SelecionarExtensoes(pastaOrigem);

            if (!extensoesEscolhidas.Any())
            {
                Console.WriteLine("⚠️ Nenhuma extensão selecionada para processamento.");
                return;
            }

            var documentosVetoriais = await ragHook.ProcessarArquivosERagAsync(pastaOrigem, extensoesEscolhidas);

            if (documentosVetoriais == null || !documentosVetoriais.Any())
            {
                Console.WriteLine("⚠️ Nenhum documento pôde ser vetorizado com sucesso.");
                return;
            }

            string pastaDocumentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string pastaMeusRags = Path.Combine(pastaDocumentos, "Meus_RAGs");
            Directory.CreateDirectory(pastaMeusRags);

            string nomeArquivoJson = $"rag_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string caminhoFinalJson = Path.Combine(pastaMeusRags, nomeArquivoJson);

            SqliteVectorRepository sqliteRepo = new SqliteVectorRepository();

            await sqliteRepo.InserirDocumentosVetoriaisAsync(documentosVetoriais);

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string jsonOutput = JsonSerializer.Serialize(documentosVetoriais, jsonOptions);

            await File.WriteAllTextAsync(caminhoFinalJson, jsonOutput);

            Console.WriteLine("\n=========================================================================");
            Console.WriteLine("🎉 RAG GERADO E VETORIZADO COM SUCESSO!");
            Console.WriteLine("=========================================================================");
            Console.WriteLine($"📊 Total de Chunks/Vetores indexados: {documentosVetoriais.Count}");
            Console.WriteLine($"💾 Arquivo salvo em: {caminhoFinalJson}");
            Console.WriteLine("=========================================================================");
            Console.WriteLine("\n👉 Pressione QUALQUER TECLA para voltar ao menu principal...");

            Console.ReadKey();
        }

        public static async Task ConsultarRagAsync()
        {
            RagSearchHook ragHook = new RagSearchHook();

            string pastaDocumentos = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pastaMeusRags = Path.Combine(pastaDocumentos, "MeuRAGApp");

            if (!Directory.Exists(pastaMeusRags))
            {
                Console.WriteLine("⚠️ A pasta 'MeuRAGApp' ainda não existe. Gere um RAG primeiro.");
                Directory.CreateDirectory(pastaMeusRags);
                return;
            }

            var arquivosRag = Directory.GetFiles(pastaMeusRags, "*.db").OrderByDescending(f => f).ToList();

            if (!arquivosRag.Any())
            {
                Console.WriteLine("⚠️ Nenhum arquivo de RAG (.db) foi encontrado em MeuRAGApp.");
                return;
            }

            Console.WriteLine("\n📚 Escolha uma base de RAG para consultar:");
            Console.WriteLine("---------------------------------------------");
            for (int i = 0; i < arquivosRag.Count; i++)
            {
                Console.WriteLine($"[{i + 1}] {Path.GetFileName(arquivosRag[i])}");
            }
            Console.Write("\nDigite o número do RAG desejado: ");

            if (!int.TryParse(Console.ReadLine()?.Trim(), out int escolha) || escolha < 1 || escolha > arquivosRag.Count)
            {
                Console.WriteLine("❌ Seleção inválida.");
                return;
            }

            string caminhoRagEscolhido = arquivosRag[escolha - 1];
            var motorBusca = new MotorBuscaRAG(caminhoRagEscolhido);

            // Carrega todas as opções do SQLite para o seletor TUI
            List<string> sugestoesCodebase = motorBusca.ObterSugestoesMembros();

            while (true)
            {
                Console.WriteLine("\n---------------------------------------------");

                // Chama o leitor de linha de comando que abre a TUI ao apertar '\'
                string pergunta = ConsoleInputManager.LerPerguntaComSeletor(
                    "❓ Digite sua pergunta (aperte '\\' para abrir o seletor): ",
                    sugestoesCodebase
                )?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(pergunta)) continue;
                if (pergunta.Equals("sair", StringComparison.OrdinalIgnoreCase)) break;

                Console.Write("\n🧠 Vetorizando sua pergunta...");

                float[] embeddingPergunta = await ragHook.GerarEmbeddingPerguntaAsync(pergunta);

                if (embeddingPergunta == null || embeddingPergunta.Length == 0)
                {
                    Console.WriteLine("\n❌ Não foi possível gerar o vetor para a sua pergunta. Tente novamente.");
                    continue;
                }
                Console.WriteLine(" ✅ OK");

                Console.Write("🔍 Buscando trechos mais relevantes via SIMD + SQLite...");

                // Executa a busca com Top-K e Threshold
                List<ResultadoBusca> resultados = motorBusca.BuscarTopK(
                    embeddingPergunta: embeddingPergunta,
                    perguntaUsuario: pergunta,
                    topK: 5,
                    threshold: 0.50f
                );

                Console.WriteLine(" ✅ OK\n");

                if (!resultados.Any())
                {
                    Console.WriteLine("⚠️ Nenhum trecho relevante foi encontrado para essa pergunta (Score abaixo do limite de 50%).");
                    continue;
                }

                Console.WriteLine("🎯 Trechos Encontrados:");
                foreach (var res in resultados)
                {
                    Console.WriteLine($"   📄 [{res.Documento.CaminhoRelativo}] -> {res.Documento.TipoChunk}: {res.Documento.NomeMembro} ({res.Similaridade * 100:F1}%)");
                }

                // =========================================================================
                // 💡 SELEÇÃO DE PRESET DINÂMICO
                // =========================================================================
                Console.WriteLine("\n🎯 Escolha o tipo de instrução do Prompt:");
                Console.WriteLine("[1] 🏗️ Arquitetura & Refatoração (Padrão)");
                Console.WriteLine("[2] ⚡ Sugestões de Melhoria & Performance");
                Console.WriteLine("[3] 📄 Documentação Técnica & XML Docs");
                Console.WriteLine("[4] 🛡️ Análise de Bugs, Exceções & Segurança");
                Console.WriteLine("[5] 🧪 Gerar Testes Unitários (xUnit/NUnit)");
                Console.Write("Digite o número desejado (Enter para 1): ");

                string opcaoPreset = Console.ReadLine()?.Trim() ?? "";
                TipoPreset presetEscolhido = opcaoPreset switch
                {
                    "2" => TipoPreset.SugestaoMelhoriaPerformance,
                    "3" => TipoPreset.DocumentacaoTecnica,
                    "4" => TipoPreset.AnaliseDeBugsESeguranca,
                    "5" => TipoPreset.CriacaoDeTestesUnitarios,
                    _ => TipoPreset.ArquiteturaERefatoracao
                };

                // Extrai a lista de DocumentoVetorial com TODOS os metadados já preenchidos do SQLite
                List<DocumentoVetorial> docsRelevantes = resultados.Select(r => r.Documento).ToList();

                // Alimenta o hook de geração de pergunta estruturada
                string promptEstruturado = ragHook.GerarPerguntaEstruturada(pergunta, docsRelevantes, presetEscolhido);

                Console.WriteLine("\n📋 PROMPT ESTRUTURADO GERADO COM SUCESSO:");
                Console.WriteLine("=========================================================================");
                Console.WriteLine(promptEstruturado);
                Console.WriteLine("=========================================================================");

                bool copiadoComSucesso = false;

                Thread threadClipboard = new Thread(() =>
                {
                    try
                    {
                        Clipboard.SetDataObject(promptEstruturado, true, 3, 100);
                        copiadoComSucesso = true;
                    }
                    catch
                    {
                        copiadoComSucesso = false;
                    }
                });

                threadClipboard.SetApartmentState(ApartmentState.STA);
                threadClipboard.IsBackground = true;
                threadClipboard.Start();
                threadClipboard.Join();

                if (copiadoComSucesso)
                {
                    Console.WriteLine("\n✨ Prompt copiado automaticamente para a sua Área de Transferência (Ctrl+V)!");
                    Console.WriteLine("💡 Basta colar no chat para receber a resposta formatada.");
                }
                else
                {
                    Console.WriteLine("\n⚠️ Não foi possível acessar a Área de Transferência automaticamente.");
                    Console.WriteLine("💡 Copie o texto exibido acima manualmente e cole no chat!");
                }
            }
        }
    

    public static async Task AtualizarRagExistenteAsync()
        {
            string pastaDocumentos = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pastaMeusRags = Path.Combine(pastaDocumentos, "MeuRAGApp");

            var arquivosRag = Directory.GetFiles(pastaMeusRags, "*.db").OrderByDescending(f => f).ToList();
            if (!arquivosRag.Any())
            {
                Console.WriteLine("⚠️ Nenhum arquivo de RAG (.db) encontrado para atualizar.");
                return;
            }

            Console.WriteLine("\n📚 Escolha o RAG (.db) que deseja atualizar:");
            for (int i = 0; i < arquivosRag.Count; i++)
            {
                Console.WriteLine($"[{i + 1}] {Path.GetFileName(arquivosRag[i])}");
            }
            Console.Write("Digite o número desejado: ");

            if (!int.TryParse(Console.ReadLine()?.Trim(), out int escolha) || escolha < 1 || escolha > arquivosRag.Count)
            {
                Console.WriteLine("❌ Seleção inválida.");
                return;
            }

            string caminoDbEscolhido = arquivosRag[escolha - 1];

            RagHook ragHook = new RagHook();
            Console.WriteLine("\n📂 Selecione a pasta da codebase atualizada na janela do Windows...");
            string pastaOrigem = ragHook.SelecionarPastaOrigem();

            if (string.IsNullOrEmpty(pastaOrigem)) return;

            List<string> extensoes = ragHook.SelecionarExtensoes(pastaOrigem);
            if (!extensoes.Any()) return;

            // Executa a sincronização inteligente
            await ragHook.ProcessarAtualizacaoIncrementalAsync(pastaOrigem, extensoes, caminoDbEscolhido);
        }
    }
}