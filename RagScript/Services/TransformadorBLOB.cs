using Microsoft.Data.Sqlite;
using RagScript.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RagScript.Services
{
    public class SqliteVectorRepository
    {
        private readonly string _caminhoBanco;

        public SqliteVectorRepository()
            : this(ObterCaminhoPadraoAppData("MeuRAGApp", $"rag_{DateTime.Now:yyyyMMdd_HHmmss}.db"))
        {
        }

        public SqliteVectorRepository(string caminhoBanco)
        {
            _caminhoBanco = caminhoBanco;
            GarantirDiretorioExistente(_caminhoBanco);
        }

        public string CaminhoBanco => _caminhoBanco;

        private static string ObterCaminhoPadraoAppData(string nomeApp, string nomeArquivoDb)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, nomeApp, nomeArquivoDb);
        }

        private static void GarantirDiretorioExistente(string caminhoArquivo)
        {
            string pastaPai = Path.GetDirectoryName(caminhoArquivo);
            if (!string.IsNullOrEmpty(pastaPai) && !Directory.Exists(pastaPai))
            {
                Directory.CreateDirectory(pastaPai);
            }
        }

        public async Task InicializarBancoAsync()
        {
            using var conexao = new SqliteConnection($"Data Source={_caminhoBanco}");
            await conexao.OpenAsync();

            using (var cmdWal = new SqliteCommand("PRAGMA journal_mode = WAL;", conexao))
            {
                await cmdWal.ExecuteNonQueryAsync();
            }

            // Tabela nomeada como Chunks para alinhar com o MotorBuscaRAG
            string sqlCriarTabela = @"
            CREATE TABLE IF NOT EXISTS Chunks (
                IdChunk TEXT PRIMARY KEY,
                CaminhoRelativo TEXT NOT NULL,
                NomeArquivo TEXT NOT NULL,
                HashConteudo TEXT NOT NULL,
                TipoChunk TEXT NOT NULL,
                NomeMembro TEXT,
                HierarquiaCompleta TEXT,
                Metadados TEXT,
                ConteudoTexto TEXT NOT NULL,
                VetorBlob BLOB NOT NULL,
                TamanhoVetor INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Chunks_CaminhoRelativo 
                ON Chunks(CaminhoRelativo);

            CREATE INDEX IF NOT EXISTS IX_Chunks_HashConteudo 
                ON Chunks(HashConteudo);";

            using var comando = new SqliteCommand(sqlCriarTabela, conexao);
            await comando.ExecuteNonQueryAsync();
        }

        public async Task InserirDocumentosVetoriaisAsync(List<DocumentoVetorial> documentos)
        {
            if (documentos == null || documentos.Count == 0)
                return;

            await InicializarBancoAsync();

            using var conexao = new SqliteConnection($"Data Source={_caminhoBanco}");
            await conexao.OpenAsync();
            using var transacao = conexao.BeginTransaction();

            string sqlUpsert = @"
            INSERT OR REPLACE INTO Chunks (
                IdChunk,
                CaminhoRelativo,
                NomeArquivo,
                HashConteudo,
                TipoChunk,
                NomeMembro,
                HierarquiaCompleta,
                Metadados,
                ConteudoTexto,
                VetorBlob,
                TamanhoVetor
            ) VALUES (
                $idChunk,
                $caminhoRelativo,
                $nomeArquivo,
                $hashConteudo,
                $tipoChunk,
                $nomeMembro,
                $hierarquiaCompleta,
                $metadados,
                $conteudoTexto,
                $vetorBlob,
                $tamanhoVetor
            );";

            using var comando = new SqliteCommand(sqlUpsert, conexao, transacao);

            var paramIdChunk = comando.Parameters.Add("$idChunk", SqliteType.Text);
            var paramCaminhoRelativo = comando.Parameters.Add("$caminhoRelativo", SqliteType.Text);
            var paramNomeArquivo = comando.Parameters.Add("$nomeArquivo", SqliteType.Text);
            var paramHashConteudo = comando.Parameters.Add("$hashConteudo", SqliteType.Text);
            var paramTipoChunk = comando.Parameters.Add("$tipoChunk", SqliteType.Text);
            var paramNomeMembro = comando.Parameters.Add("$nomeMembro", SqliteType.Text);
            var paramHierarquiaCompleta = comando.Parameters.Add("$hierarquiaCompleta", SqliteType.Text);
            var paramMetadados = comando.Parameters.Add("$metadados", SqliteType.Text);
            var paramConteudoTexto = comando.Parameters.Add("$conteudoTexto", SqliteType.Text);
            var paramVetorBlob = comando.Parameters.Add("$vetorBlob", SqliteType.Blob);
            var paramTamanhoVetor = comando.Parameters.Add("$tamanhoVetor", SqliteType.Integer);

            foreach (var doc in documentos)
            {
                if (doc.Embedding == null || doc.Embedding.Length == 0)
                    continue;

                byte[] vetorBytes = VetorConverter.NormalizarQuantizarEConverterParaBytes(doc.Embedding);

                paramIdChunk.Value = doc.IdChunk ?? Guid.NewGuid().ToString();
                paramCaminhoRelativo.Value = doc.CaminhoRelativo ?? string.Empty;
                paramNomeArquivo.Value = doc.NomeArquivo ?? string.Empty;
                paramHashConteudo.Value = doc.HashConteudo ?? string.Empty;
                paramTipoChunk.Value = doc.TipoChunk ?? "ArquivoCompleto";
                paramNomeMembro.Value = doc.NomeMembro ?? string.Empty;
                paramHierarquiaCompleta.Value = doc.HierarquiaCompleta ?? string.Empty;
                paramMetadados.Value = doc.Metadados ?? string.Empty;
                paramConteudoTexto.Value = doc.ConteudoTexto ?? string.Empty;
                paramVetorBlob.Value = vetorBytes;
                paramTamanhoVetor.Value = doc.Embedding.Length;

                await comando.ExecuteNonQueryAsync();
            }

            await transacao.CommitAsync();
        }
    }
}