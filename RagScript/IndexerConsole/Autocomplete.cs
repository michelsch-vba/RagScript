using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terminal.Gui;

using GuiApp = Terminal.Gui.Application;
using GuiDialog = Terminal.Gui.Dialog;
using GuiLabel = Terminal.Gui.Label;
using GuiTextField = Terminal.Gui.TextField;
using GuiListView = Terminal.Gui.ListView;
using GuiDim = Terminal.Gui.Dim;

namespace RagScript.IndexerConsole
{
    public static class SeletorTerminalGui
    {
        public static string Abrir(List<string> todosMembros)
        {
            string itemSelecionado = string.Empty;

            try
            {
                // Inicializa a TUI usando o Alias do Terminal.Gui
                GuiApp.Init();

                var dialog = new GuiDialog("💡 Seletor de Membros e Arquivos (Codebase)", 80, 20);

                var lblBusca = new GuiLabel("Filtrar: ")
                {
                    X = 1,
                    Y = 1
                };

                var txtBusca = new GuiTextField("")
                {
                    X = 10,
                    Y = 1,
                    Width = GuiDim.Fill(1)
                };

                var listView = new GuiListView(todosMembros)
                {
                    X = 1,
                    Y = 3,
                    Width = GuiDim.Fill(1),
                    Height = GuiDim.Fill(1)
                };

                // Filtro dinâmico ao digitar no campo de texto
                txtBusca.TextChanged += (oldText) =>
                {
                    string filtro = txtBusca.Text.ToString() ?? string.Empty;
                    var filtrados = todosMembros
                        .Where(m => m.Contains(filtro, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    listView.SetSource(filtrados);
                };

                // Confirmação ao pressionar Enter no item da lista
                listView.OpenSelectedItem += (e) =>
                {
                    if (listView.Source != null && listView.Source.Count > 0)
                    {
                        var listaAtual = listView.Source.ToList();
                        if (listView.SelectedItem >= 0 && listView.SelectedItem < listaAtual.Count)
                        {
                            itemSelecionado = listaAtual[listView.SelectedItem]?.ToString() ?? string.Empty;
                            GuiApp.RequestStop();
                        }
                    }
                };

                dialog.Add(lblBusca);
                dialog.Add(txtBusca);
                dialog.Add(listView);

                GuiApp.Run(dialog);
            }
            finally
            {
                GuiApp.Shutdown();
            }

            return itemSelecionado;
        }
    }

    public static class ConsoleInputManager
    {
        public static string LerPerguntaComSeletor(string prompt, List<string> sugestoes)
        {
            Console.Write(prompt);
            StringBuilder input = new StringBuilder();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                // GATILHO DA BARRA '\': Abre a janela TUI do Terminal.Gui
                if (key.KeyChar == '\\')
                {
                    // Abre o seletor visual em Terminal.Gui
                    string selecionado = SeletorTerminalGui.Abrir(sugestoes);

                    if (!string.IsNullOrEmpty(selecionado))
                    {
                        input.Append(selecionado);
                    }

                    // Limpa e redesenha a linha do prompt com o texto atual + o membro selecionado
                    Console.Clear();
                    Console.Write(prompt + input.ToString());
                    continue;
                }

                // Confirmar entrada (Enter)
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return input.ToString();
                }

                // Apagar caractere (Backspace)
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input.Remove(input.Length - 1, 1);
                        Console.Write("\b \b");
                    }
                    continue;
                }

                // Caracteres comuns
                if (!char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }
        }
    }
}