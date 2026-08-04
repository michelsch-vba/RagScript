using Google.GenAI;
using RagScript.Hooks;
using RagScript.Models;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace RagScript.Funções;

public static class Functions
{

    //Função API Key
    public static async Task<string> GerenciarApiKeyAsync()
    {
        ApiHook apiHook = new ApiHook();

        // 1. Chama o Hook para obter ou editar a chave
        string key = await apiHook.ObteroudarKey();

        if (string.IsNullOrEmpty(key))
        {
            Console.WriteLine("⚠️ Nenhuma chave foi selecionada.");
            return string.Empty;
        }

        // 2. Chama o Hook de Heartbeat/Teste
        bool conexaoOk = await apiHook.TestarApiKeyAsync(key);

        if (conexaoOk)
        {
            Console.WriteLine("💚 Heartbeat OK: Conexão com a API do Gemini estabelecida com sucesso!");
        }
        else
        {
            Console.WriteLine("💔 Heartbeat FAIL: A API Key informada parece ser inválida ou sem acesso.");
        }

        return key;
    }

    //Função de Gerar RAG
    public static async Task GerarRagCompletoAsync(string apiKey)
    {
        RagHook ragHook = new RagHook();

        // 1. Abre a caixa de diálogo para selecionar a pasta
        Console.WriteLine("\n📂 Selecione a pasta contendo os arquivos na janela do Windows...");
        string pastaOrigem = ragHook.SelecionarPastaOrigem();

        if (string.IsNullOrEmpty(pastaOrigem))
        {
            Console.WriteLine("⚠️ Nenhuma pasta selecionada. Operação cancelada.");
            return;
        }

        Console.WriteLine($"📌 Pasta selecionada: {pastaOrigem}");

        // 2. Chama a função auxiliar que varre e deixa escolher as extensões
        var extensoesEscolhidas = ragHook.SelecionarExtensoes(pastaOrigem);

        if (!extensoesEscolhidas.Any())
        {
            Console.WriteLine("⚠️ Nenhuma extensão selecionada para processamento.");
            return;
        }

        // 3. Executa a extração e vetorização dos arquivos (com Chunking)
        var documentosVetoriais = await ragHook.ProcessarArquivosERagAsync(pastaOrigem, extensoesEscolhidas, apiKey);

        if (!documentosVetoriais.Any())
        {
            Console.WriteLine("⚠️ Nenhum documento pôde ser vetorizado com sucesso.");
            return;
        }

        // 4. Prepara a pasta 'Meus_RAGs' em Meus Documentos
        string pastaDocumentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string pastaMeusRags = Path.Combine(pastaDocumentos, "Meus_RAGs");
        Directory.CreateDirectory(pastaMeusRags);

        // Cria um nome único com timestamp para o arquivo JSON
        string nomeArquivoJson = $"rag_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string caminhoFinalJson = Path.Combine(pastaMeusRags, nomeArquivoJson);

        // 5. Salva o RAG final em formato JSON
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string jsonOutput = JsonSerializer.Serialize(documentosVetoriais, jsonOptions);

        await File.WriteAllTextAsync(caminhoFinalJson, jsonOutput);

        // =========================================================================
        // 💡 TELA DE SUCESSO E PAUSA
        // =========================================================================
        Console.WriteLine("\n=========================================================================");
        Console.WriteLine("🎉 RAG GERADO E VETORIZADO COM SUCESSO!");
        Console.WriteLine("=========================================================================");
        Console.WriteLine($"📊 Total de Chunks/Vetores indexados: {documentosVetoriais.Count}");
        Console.WriteLine($"💾 Arquivo salvo em: {caminhoFinalJson}");
        Console.WriteLine("=========================================================================");
        Console.WriteLine("\n👉 Pressione QUALQUER TECLA para voltar ao menu principal...");

        Console.ReadKey(); // Trava a tela aqui antes de permitir que o Program.cs limpe o console!
    }


    //FUnção de Consultar RAG
    public static async Task ConsultarRagAsync(string apiKey)
    {
        RagSearchHook ragHook = new RagSearchHook();

        // 1. Localizar pasta Meus_RAGs e listar arquivos JSON
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

        // 2. Menu para seleção do arquivo de RAG
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

        // 3. Carregar e deserializar a base vetorial
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

        // 4. Loop de Perguntas/Consultas
        while (true)
        {
            Console.WriteLine("\n---------------------------------------------");
            Console.Write("❓ Digite sua pergunta sobre o projeto (ou 'sair' para voltar): ");
            string pergunta = Console.ReadLine()?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(pergunta)) continue;
            if (pergunta.Equals("sair", StringComparison.OrdinalIgnoreCase)) break;

            Console.Write("🧠 Vetorizando sua pergunta...");
            float[] embeddingPergunta = await ragHook.GerarEmbeddingPerguntaAsync(pergunta, apiKey);

            if (embeddingPergunta.Length == 0)
            {
                Console.WriteLine("\n❌ Não foi possível gerar o vetor para a sua pergunta. Tente novamente.");
                continue;
            }
            Console.WriteLine(" ✅ OK");

            // 5. Ranking por Similaridade de Cosseno
            Console.Write("🔍 Buscando trechos mais relevantes...");
            var resultadosOrdenados = baseVetorial
                .Select(doc => new
                {
                    Documento = doc,
                    Similaridade = ragHook.CalcularScorePonderado(doc, embeddingPergunta, pergunta)
                })
                .OrderByDescending(r => r.Similaridade)
                .Take(3) // Seleciona o TOP 3 mais relevantes
                .ToList();

            Console.WriteLine(" ✅ OK\n");

            Console.WriteLine("🎯 Trechos Encontrados:");
            foreach (var res in resultadosOrdenados)
            {
                Console.WriteLine($"   📄 [{res.Documento.CaminhoRelativo}] -> {res.Documento.TipoChunk}: {res.Documento.NomeMembro} ({res.Similaridade * 100:F1}%)");
            }

            // =========================================================================
            // 6. NOVO PASSO: Gerar e Exibir o Prompt Estruturado de RAG
            // =========================================================================
            var docsRelevantes = resultadosOrdenados.Select(r => r.Documento).ToList();
            string promptEstruturado = ragHook.GerarPerguntaEstruturada(pergunta, docsRelevantes);

            Console.WriteLine("\n📋 PROMPT ESTRUTURADO GERADO COM SUCESSO:");
            Console.WriteLine("=========================================================================");
            Console.WriteLine(promptEstruturado);
            Console.WriteLine("=========================================================================");

            // Copia para a Área de Transferência com isolamento de thread e tratamento de erro interno
            bool copiadoComSucesso = false;

            Thread threadClipboard = new Thread(() =>
            {
                try
                {
                    // 💡 O uso de retry (3 tentativas com delay de 100ms) evita concorrência no Clipboard do Windows
                    Clipboard.SetDataObject(promptEstruturado, true, 3, 100);
                    copiadoComSucesso = true;
                }
                catch (Exception ex)
                {
                    // Captura qualquer erro de COM/STA dentro da própria thread
                    copiadoComSucesso = false;
                }
            });

            threadClipboard.SetApartmentState(ApartmentState.STA);
            threadClipboard.IsBackground = true; // Garante que não trave o encerramento do processo caso algo dê errado
            threadClipboard.Start();
            threadClipboard.Join(); // Aguarda a finalização da thread de UI

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
