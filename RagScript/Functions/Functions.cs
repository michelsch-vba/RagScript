using Google.GenAI;
using RagScript.Hooks;
using RagScript.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RagScript.Funções
{
    public static class Functions
    {
        public static async Task<List<string>> GerenciarApiKeyAsync()
        {
            ApiHook apiHook = new ApiHook();
            List<string> keys = await apiHook.ObteroudarKeysAsync();

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

            var extensoesEscolhidas = ragHook.SelecionarExtensoes(pastaOrigem);

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

            string pastaDocumentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string pastaMeusRags = Path.Combine(pastaDocumentos, "Meus_RAGs");

            if (!Directory.Exists(pastaMeusRags))
            {
                Console.WriteLine("⚠️ A pasta 'Meus_RAGs' ainda não existe. Gere um RAG primeiro (Opção 2).");
                return;
            }

            var arquivosRag = Directory.GetFiles(pastaMeusRags, "*.json").OrderByDescending(f => f).ToList();

            if (!arquivosRag.Any())
            {
                Console.WriteLine("⚠️ Nenhum arquivo de RAG (.json) foi encontrado em Meus_RAGs.");
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

            Console.WriteLine("\n🔄 Carregando base vetorial...");
            List<DocumentoVetorial>? baseVetorial = null;

            try
            {
                string jsonConteudo = await File.ReadAllTextAsync(caminhoRagEscolhido);
                baseVetorial = JsonSerializer.Deserialize<List<DocumentoVetorial>>(jsonConteudo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao ler o arquivo RAG: {ex.Message}");
                return;
            }

            if (baseVetorial == null || !baseVetorial.Any())
            {
                Console.WriteLine("❌ A base de RAG está vazia ou é inválida.");
                return;
            }

            Console.WriteLine($"✅ Base carregada! Total de documentos vetorizados: {baseVetorial.Count}");

            while (true)
            {
                Console.WriteLine("\n---------------------------------------------");
                Console.Write("❓ Digite sua pergunta sobre o projeto (ou 'sair' para voltar): ");
                string pergunta = Console.ReadLine()?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(pergunta)) continue;
                if (pergunta.Equals("sair", StringComparison.OrdinalIgnoreCase)) break;

                Console.Write("🧠 Vetorizando sua pergunta...");

                float[] embeddingPergunta = await ragHook.GerarEmbeddingPerguntaAsync(pergunta);

                if (embeddingPergunta.Length == 0)
                {
                    Console.WriteLine("\n❌ Não foi possível gerar o vetor para a sua pergunta. Tente novamente.");
                    continue;
                }
                Console.WriteLine(" ✅ OK");

                Console.Write("🔍 Buscando trechos mais relevantes...");
                var resultadosOrdenados = baseVetorial
                    .Select(doc => new
                    {
                        Documento = doc,
                        Similaridade = ragHook.CalcularScorePonderado(doc, embeddingPergunta, pergunta)
                    })
                    .OrderByDescending(r => r.Similaridade)
                    .Take(3)
                    .ToList();

                Console.WriteLine(" ✅ OK\n");

                Console.WriteLine("🎯 Trechos Encontrados:");
                foreach (var res in resultadosOrdenados)
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

                var docsRelevantes = resultadosOrdenados.Select(r => r.Documento).ToList();
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
    }
}