# 🏎️ PitWall Copilot

> AI-native F1 strategy copilot with tool orchestration, RAG grounding, and transparent reasoning.

PitWall Copilot is a portfolio-focused full-stack Azure/.NET AI project that simulates an internal race engineering assistant for a Formula 1 team.

Users ask natural-language strategy questions, and the system responds with grounded analysis by combining:

- deterministic telemetry tools (driver pace, teammate delta, consistency)
- retrieval from race briefings and driver debriefs
- explicit decision tracing and confidence scoring

The goal is not just "chatbot answers." The goal is **observable AI behavior** you can inspect end-to-end.

## Why did I built this

To explore RAG and AI orchestration using Azure and .NET.

- **AI orchestration, not a single prompt**: retrieval + function/tool calling + fallback policy
- **Transparent output**: visible audit trail shows how the answer was produced
- **Production-minded behavior**: graceful degradation when keys/services are missing
- **Grounded by design**: answers are tied to retrieved context and explicit tool outputs

## Screenshots

Replace the placeholders below after you capture your images.

| Ask + Answer + Audit Trail | RAG Retrieval in Action |
| --- | --- |
| ![Ask PitWall Placeholder](documentation/screenshots/pitwall-ask-answer-placeholder.png) | ![RAG Audit Placeholder](documentation/screenshots/pitwall-rag-audit-placeholder.png) |

| Suggested Prompts UX | Driver Performance Dashboard |
| --- | --- |
| ![Prompts Placeholder](documentation/screenshots/pitwall-suggested-prompts-placeholder.png) | ![Dashboard Placeholder](documentation/screenshots/pitwall-dashboard-placeholder.png) |

## What it does

PitWall Copilot answers questions like:

- "At Silverstone 2025, what were the top performance risks and setup priorities?"
- "Why did Lando lose consistency after instability spikes?"
- "How should we think about soft vs medium tire behavior over a stint?"

Each response includes:

- final answer
- why-summary
- confidence level and confidence reasons
- full audit trail (input -> policy -> retrieval -> tool decisions -> result)
- tool usage and token metrics

## How it works

```text
User Question
  -> Embed query
  -> Retrieve context from Azure AI Search
  -> LLM policy decides which tools to call
  -> Tool results + retrieved context fed into final response
  -> Confidence + audit trail returned to UI
```

### Core tools

- `SearchRagContext` - semantic retrieval over race briefings/debriefs
- `FindDriversByName` - fuzzy name resolution
- `GetDriverPerformance` - single-driver telemetry summary
- `CompareDrivers` - side-by-side comparisons

### Fallback behavior

If AI config is missing or invalid, the app falls back safely:

- deterministic stats still work for supported question types
- strategy-style questions return an explicit "AI path required" response
- startup logs report missing/placeholder config keys

## Tech stack

| Layer | Technology |
| --- | --- |
| Frontend | Blazor Server (.NET 10) |
| Backend | ASP.NET Core, EF Core, SQLite |
| AI | OpenAI client integration (`OpenAI.Chat`) |
| Retrieval | Azure AI Search (vector search) |
| Architecture | Vertical slices + clean dependency boundaries |

## Run locally

### Prerequisites

- .NET SDK 10
- OpenAI-compatible API key/model access
- Azure AI Search service (for RAG)
- optional: `dotnet dev-certs https --trust`

### Setup

```powershell
# Edit src/Web/.env with your keys
cd src/Web
dotnet run --launch-profile https
```

Open `https://localhost:7019`.

### Environment variables

```bash
OPENAI_API_KEY=...
OPENAI_ENDPOINT=https://YOUR-RESOURCE.openai.azure.com
OPENAI_MODEL=gpt-4o-mini
OPENAI_EMBEDDING_MODEL=text-embedding-3-small

AZURE_SEARCH_ENDPOINT=https://YOUR-SERVICE.search.windows.net
AZURE_SEARCH_API_KEY=...
AZURE_SEARCH_INDEX_NAME=pitwall-rag-index
```

On startup the app seeds SQLite data and attempts to initialize/index the RAG store.

## Roadmap

- evaluation harness with golden prompts
- hybrid retrieval (semantic + keyword)
- richer token/cost reporting in UI
- prompt/model version tracking
- Azure deployment with Key Vault + app telemetry
