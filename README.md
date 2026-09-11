# 🚀 RagScript

> **Engine de RAG Sintático e Busca Vetorial de Alta Performance para .NET (C# & XAML/WPF)**  
*Indexação ultracompacta via Roslyn, quantização `int8` em SQLite BLOBs e busca acelerada via ferragens modernas (SIMD).*

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform: Windows | Linux | macOS](https://img.shields.io/badge/Platform-Cross--Platform-lightgrey.svg)]()

---

## ⚡ O que é o RagScript?

O **RagScript** é um motor RAG (Retrieval-Augmented Generation) focado no ecossistema **.NET (C#, XAML/WPF, `.csproj`, `.slnx`)**. 

Diferente de frameworks RAG genéricos que tratam código como texto bruto (causando estouro de janelas de contexto e perda de semântica), o RagScript utiliza a **API do Roslyn** e parsers de XML para realizar **chunking sintático**. Ele identifica métodos, tipos, propriedades, DataBindings e documentações XML Docs (`<summary>`), indexando os dados em uma base SQLite com quantização de vetores e busca vetorial por hardware via **SIMD**.

---

## 💡 Destaques de Arquitetura & Engenharia

* **Fatiamento Sintático Avançado (Roslyn AST):** Extração cirúrgica por AST de métodos, construtores, tipos e documentação XML em C#, mantendo o vínculo completo da hierarquia de namespaces.
* **Extração XAML & Contratos:** Mapeamento de nós raiz, vínculos de Code-Behind (`x:Class`), controles chave e `ResourceDictionaries` em arquivos de UI.
* **Quantização `int8` de Alta Densidade (ADC):** Redução de **~75% no consumo de armazenamento** — vetores de 3072 posições (`float32`) são normalizados via L2 e convertidos para `sbyte` (armazenados como `BLOB` no SQLite).
* **Aceleração Hardware SIMD (`TensorPrimitives`):** Desquantização na memória RAM e cálculo de Produto Escalar (Dot Product) / Cosseno via rotinas otimizadas para AVX2/AVX-512 (`System.Numerics.Tensors`).
* **Zero-Allocation Hot Path:** Uso intensivo de `ArrayPool<T>`, `MemoryMarshal`, `Span<T>` e `ReadOnlySpan<char>` no loop crítico de busca e fatiamento.
* **Resiliência e Rotação de API:** Pool de chaves com cooldown automático em requisições HTTP 429 (Rate Limit) para a API do Gemini.
* **Sincronização Incremental Inteligente:** Mapeamento por hash SHA-256 no SQLite — re-vetoriza apenas arquivos alterados/novos e remove os excluídos.

---

## 🏗️ Fluxo de Funcionamento

```text
┌─────────────────────────┐
│  Código C# / XAML / XML │
└────────────┬────────────┘
             │ (Roslyn AST / Parser XML)
             ▼
┌─────────────────────────┐
│   Chunking Sintático    │ ──> Captura de métodos, metadados e <summary>
└────────────┬────────────┘
             │ (Google Gemini API - gemini-embedding-001)
             ▼
┌─────────────────────────┐
│   Vetor Float (3072d)   │
└────────────┬────────────┘
             │ (Normalização L2 + Quantização int8)
             ▼
┌─────────────────────────┐
│   SQLite (Tabela Chunks)│ ──> Vetores salvos em BLOB (~3 KB / chunk)
└────────────┬────────────┘
             │
             │  (Busca Vetorial Local)
             ▼
┌─────────────────────────┐
│ SIMD + Re-ranking       │ ──> TensorPrimitives.Dot + Bônus de Metadados
└────────────┬────────────┘
             │
             ▼
┌─────────────────────────┐
│   Prompt Estruturado    │ ──> Copiado automaticamente para a Clipboard (Ctrl+V)
└────────────┬────────────┘

## 🎥 Demonstração

![RagScript CLI Demo](docs/RagScriptDemo.gif)