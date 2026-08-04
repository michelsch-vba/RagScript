
using Google.GenAI;
using RagScript.Funções; // O namespace de utilitários
using RagScript.Models;
using RagScript.Hooks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FreeRag.IndexerConsole;
internal class Program
{
    private static string _apiKeyAtual = string.Empty;

    [STAThread] // Permite abrir a janela de diálogo do Windows (FolderBrowserDialog)
    static async Task Main(string[] args)
    {
        Console.Title = "Gerador de RAG com Gemini - FreeRag";

        bool executando = true;

        while (executando)
        {
            Console.Clear();
            Console.WriteLine("=============================================");
            Console.WriteLine("   GERADOR DE RAG EM JSON (GEMINI API)       ");
            Console.WriteLine("=============================================\n");
            Console.WriteLine("-------------Escolha uma opção---------------\n");
            Console.WriteLine("1. Salvar ou editar API key");
            Console.WriteLine("2. Gerar RAG");
            Console.WriteLine("3. Consultar RAG (Busca Vetorial Local)");
            Console.WriteLine("4. Sair");
            Console.Write("\nDigite sua escolha: ");

            string escolha = Console.ReadLine()?.Trim() ?? "";

            switch (escolha)
            {
                case "1":
                    _apiKeyAtual = await Functions.GerenciarApiKeyAsync();
                    Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
                    Console.ReadKey();
                    break;

                case "2":
                    if (string.IsNullOrEmpty(_apiKeyAtual))
                    {
                        ApiHook apiHook = new ApiHook();
                        _apiKeyAtual = await apiHook.ObteroudarKey();
                    }

                    if (!string.IsNullOrEmpty(_apiKeyAtual))
                    {
                        await Functions.GerarRagCompletoAsync(_apiKeyAtual);
                    }
                    else
                    {
                        Console.WriteLine("\n⚠️ Uma API Key válida é necessária para gerar o RAG.");
                    }

                    Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
                    Console.ReadKey();
                    break;

                case "3":
                    if (string.IsNullOrEmpty(_apiKeyAtual))
                    {
                        ApiHook apiHook = new ApiHook();
                        _apiKeyAtual = await apiHook.ObteroudarKey();
                    }

                    if (!string.IsNullOrEmpty(_apiKeyAtual))
                    {
                        await Functions.ConsultarRagAsync(_apiKeyAtual);
                    }
                    else
                    {
                        Console.WriteLine("\n⚠️ Uma API Key válida é necessária para consultar o RAG.");
                    }

                    Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
                    Console.ReadKey();
                    break;

                case "4":
                    executando = false;
                    Console.WriteLine("\nEncerrando aplicação...");
                    break;

                default:
                    Console.WriteLine("\n❌ Opção inválida. Tente novamente.");
                    Task.Delay(1500).Wait();
                    break;
            }
        }
    }
}