using Google.GenAI;
using RagScript.Funções;
using RagScript.Hooks;
using RagScript.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SQLitePCL;
using BenchmarkDotNet.Running;

namespace RagScript.Services
{
    internal class Program
    {
        private static List<string> _apiKeysAtuais = new();

        [STAThread]
        static async Task Main(string[] args)
        {
            // Inicializa os drivers nativos do SQLite em C#
            SQLitePCL.Batteries.Init();

            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "RagScript CLI - Motor de RAG Sintático .NET";

            bool executando = true;

            while (executando)
            {
                Console.Clear();
                Console.WriteLine("=============================================");
                Console.WriteLine("         RAGSCRIPT CLI (GEMINI API)         ");
                Console.WriteLine("=============================================\n");
                Console.WriteLine("-------------Escolha uma opção---------------\n");
                Console.WriteLine("1. Salvar ou editar API keys");
                Console.WriteLine("2. Gerar RAG (Novo)");
                Console.WriteLine("3. Consultar RAG (Busca Vetorial Local)");
                Console.WriteLine("4. Atualizar RAG existente (Sincronização Incremental)");
                Console.WriteLine("5. Sair");
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
                        await GarantirChavesCarregadasAsync();
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
                        await GarantirChavesCarregadasAsync();
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
                        await GarantirChavesCarregadasAsync();
                        if (_apiKeysAtuais.Any())
                        {
                            await Functions.AtualizarRagExistenteAsync();
                        }
                        else
                        {
                            Console.WriteLine("\n⚠️ Ao menos uma API Key válida é necessária para atualizar o RAG.");
                        }

                        Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
                        Console.ReadKey();
                        break;

                    case "5":
                        executando = false;
                        Console.WriteLine("\nEncerrando aplicação...");
                        break;

                    default:
                        Console.WriteLine("\n❌ Opção inválida. Tente novamente.");
                        await Task.Delay(1500); // Uso de await não-bloqueante
                        break;
                }
            }
        }

        private static async Task GarantirChavesCarregadasAsync()
        {
            if (!_apiKeysAtuais.Any())
            {
                ApiHook apiHook = new ApiHook();
                await apiHook.GarantirChavesCarregadasAsync();
                _apiKeysAtuais = await apiHook.ObteroudarKeysAsync() ?? new List<string>();
            }
        }
    }
}