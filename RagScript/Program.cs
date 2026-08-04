using Google.GenAI;
using RagScript.Funções;
using RagScript.Hooks;
using RagScript.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FreeRag.IndexerConsole
{
    internal class Program
    {
        private static List<string> _apiKeysAtuais = new();

        [STAThread]
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
                Console.WriteLine("1. Salvar ou editar API keys");
                Console.WriteLine("2. Gerar RAG");
                Console.WriteLine("3. Consultar RAG (Busca Vetorial Local)");
                Console.WriteLine("4. Sair");
                Console.Write("\nDigite sua escolha: ");

                string escolha = Console.ReadLine()?.Trim() ?? "";

                switch (escolha)
                {
                    case "1":
                        _apiKeysAtuais = await Functions.GerenciarApiKeyAsync();
                        Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
                        Console.ReadKey();
                        break;

                    case "2":
                        if (!_apiKeysAtuais.Any())
                        {
                            ApiHook apiHook = new ApiHook();
                            _apiKeysAtuais = await apiHook.ObteroudarKeysAsync();
                        }

                        if (_apiKeysAtuais.Any())
                        {
                            await Functions.GerarRagCompletoAsync();
                        }
                        else
                        {
                            Console.WriteLine("\n⚠️ Ao menos uma API Key válida é necessária para gerar o RAG.");
                        }

                        Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
                        Console.ReadKey();
                        break;

                    case "3":
                        if (!_apiKeysAtuais.Any())
                        {
                            ApiHook apiHook = new ApiHook();
                            _apiKeysAtuais = await apiHook.ObteroudarKeysAsync();
                        }

                        if (_apiKeysAtuais.Any())
                        {
                            await Functions.ConsultarRagAsync();
                        }
                        else
                        {
                            Console.WriteLine("\n⚠️ Ao menos uma API Key válida é necessária para consultar o RAG.");
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
}