using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RagScript.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RagScript.Services.Chunker
{
    public static class Csharp_Chunker
    {
        public static List<ChunkResult> QuebrarCodigoCSharp(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return new List<ChunkResult>(0);

            SyntaxTree tree = CSharpSyntaxTree.ParseText(codigo);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            var collector = new CSharpSyntaxCollector();
            collector.Visit(root);

            int totalMembrosSemPropriedadesSimples = collector.Methods.Count + collector.Constructors.Count;
            int totalElementos = collector.Types.Count + collector.Methods.Count + collector.Constructors.Count + collector.Properties.Count;

            if (totalElementos == 0)
            {
                return new List<ChunkResult>(1)
                {
                    new ChunkResult
                    {
                        Tipo = "Arquivo/Estrutura",
                        NomeMembro = "Geral",
                        HierarquiaCompleta = "Geral",
                        DocumentacaoXml = string.Empty,
                        Conteudo = codigo
                    }
                };
            }

            // PREFERÊNCIA 1: Granularidade Fina (Membros com corpo funcional ou XML Docs)
            if (totalMembrosSemPropriedadesSimples > 0 || collector.Properties.Any(p => !string.IsNullOrEmpty(ExtrairSummaryXml(p))))
            {
                var chunks = new List<ChunkResult>();

                foreach (var method in collector.Methods)
                {
                    chunks.Add(new ChunkResult
                    {
                        Tipo = "Metodo",
                        NomeMembro = method.Identifier.Text,
                        HierarquiaCompleta = ObterCaminhoHierarquico(method),
                        DocumentacaoXml = ExtrairSummaryXml(method),
                        Conteudo = method.ToFullString().Trim()
                    });
                }

                foreach (var ctor in collector.Constructors)
                {
                    chunks.Add(new ChunkResult
                    {
                        Tipo = "Construtor",
                        NomeMembro = ctor.Identifier.Text,
                        HierarquiaCompleta = ObterCaminhoHierarquico(ctor),
                        DocumentacaoXml = ExtrairSummaryXml(ctor),
                        Conteudo = ctor.ToFullString().Trim()
                    });
                }

                // Inclui apenas propriedades relevantes (com XML docs ou blocos lógicos complexos)
                foreach (var prop in collector.Properties)
                {
                    string xmlDoc = ExtrairSummaryXml(prop);
                    bool possuiLogicaCustomizada = prop.AccessorList != null &&
                        prop.AccessorList.Accessors.Any(a => a.Body != null || a.ExpressionBody != null);

                    if (!string.IsNullOrEmpty(xmlDoc) || possuiLogicaCustomizada)
                    {
                        chunks.Add(new ChunkResult
                        {
                            Tipo = "Propriedade",
                            NomeMembro = prop.Identifier.Text,
                            HierarquiaCompleta = ObterCaminhoHierarquico(prop),
                            DocumentacaoXml = xmlDoc,
                            Conteudo = prop.ToFullString().Trim()
                        });
                    }
                }

                if (chunks.Count > 0)
                    return chunks;
            }

            // PREFERÊNCIA 2: Granularidade Estrutural (Tipos/Classes/Enums/Records)
            var typeChunks = new List<ChunkResult>(collector.Types.Count);
            foreach (var typeNode in collector.Types)
            {
                typeChunks.Add(new ChunkResult
                {
                    Tipo = ObterNomeTipo(typeNode.Kind()),
                    NomeMembro = typeNode.Identifier.Text,
                    HierarquiaCompleta = ObterCaminhoHierarquico(typeNode),
                    DocumentacaoXml = ExtrairSummaryXml(typeNode),
                    Conteudo = typeNode.ToFullString().Trim()
                });
            }

            return typeChunks;
        }

        private static string ExtrairSummaryXml(SyntaxNode node)
        {
            var docComment = node.GetLeadingTrivia()
                .Select(t => t.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            if (docComment == null)
                return string.Empty;

            var summaryNode = docComment.Content
                .OfType<XmlElementSyntax>()
                .FirstOrDefault(e => e.StartTag.Name.ToString().Equals("summary", StringComparison.OrdinalIgnoreCase));

            if (summaryNode == null)
                return string.Empty;

            string conteudoRaw = summaryNode.Content.ToString();

            // Limpeza eficiente do resumo XML
            var linhas = conteudoRaw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder(conteudoRaw.Length);

            foreach (var linha in linhas)
            {
                string linhaLimpa = linha.Replace("///", "").Trim();
                if (!string.IsNullOrEmpty(linhaLimpa))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(linhaLimpa);
                }
            }

            return sb.ToString();
        }

        private static string ObterCaminhoHierarquico(SyntaxNode node)
        {
            var ancestrais = new List<string>(4);

            for (SyntaxNode? atual = node.Parent; atual != null; atual = atual.Parent)
            {
                if (atual is BaseTypeDeclarationSyntax typeDecl)
                {
                    ancestrais.Add(typeDecl.Identifier.Text);
                }
                else if (atual is BaseNamespaceDeclarationSyntax nsDecl)
                {
                    ancestrais.Add(nsDecl.Name.ToString());
                }
            }

            if (ancestrais.Count == 0) return string.Empty;
            if (ancestrais.Count == 1) return ancestrais[0];

            var sb = new StringBuilder(64);
            for (int i = ancestrais.Count - 1; i >= 0; i--)
            {
                sb.Append(ancestrais[i]);
                if (i > 0) sb.Append('.');
            }

            return sb.ToString();
        }

        private static string ObterNomeTipo(SyntaxKind kind) => kind switch
        {
            SyntaxKind.ClassDeclaration => "Class",
            SyntaxKind.StructDeclaration => "Struct",
            SyntaxKind.InterfaceDeclaration => "Interface",
            SyntaxKind.EnumDeclaration => "Enum",
            SyntaxKind.RecordDeclaration => "Record",
            SyntaxKind.RecordStructDeclaration => "RecordStruct",
            _ => "Type"
        };
    }

    internal class CSharpSyntaxCollector : CSharpSyntaxWalker
    {
        public List<MethodDeclarationSyntax> Methods { get; } = new();
        public List<BaseTypeDeclarationSyntax> Types { get; } = new();
        public List<PropertyDeclarationSyntax> Properties { get; } = new();
        public List<ConstructorDeclarationSyntax> Constructors { get; } = new();

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            Methods.Add(node);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            Properties.Add(node);
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            Constructors.Add(node);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            Types.Add(node);
            base.VisitClassDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            Types.Add(node);
            base.VisitStructDeclaration(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            Types.Add(node);
            base.VisitInterfaceDeclaration(node);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            Types.Add(node);
            base.VisitRecordDeclaration(node);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            Types.Add(node);
            base.VisitEnumDeclaration(node);
        }
    }
}