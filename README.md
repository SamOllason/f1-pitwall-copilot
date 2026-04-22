# PitWall OS - F1 Team Data and AI Insights

PitWall OS is a full-stack .NET 10 (`Blazor`, `Azure OpenAI`, `.NET 10`, `C#`) application that simulates an internal tool used by Formula 1 teams to:

- Inspect driver and car performance
- Analyze race trends
- Ask AI-powered questions grounded in real data

This is a focused learning project to build senior-level full-stack .NET and AI system design skills.

## Run Locally

### Prerequisites

- .NET SDK 10 installed (`dotnet --version`)
- Optional (HTTPS on first run): trust local dev certificate
  - `dotnet dev-certs https --trust`

### Start the app

From the repository root:

```bash
cp .env.example src/Web/.env
# edit src/Web/.env with your Azure OpenAI + Azure AI Search keys
cd src/Web
dotnet run --launch-profile https
```

PowerShell equivalent:

```powershell
Copy-Item .env.example src/Web/.env
```

What happens on startup:

- SQLite database file (`pitwall.db`) is created automatically if missing
- Seed data is inserted automatically
- Blazor server app starts on:
  - `https://localhost:7019`
  - `http://localhost:5209`

### Verify it is working

1. Open `https://localhost:7019` in your browser.
2. Hit the API endpoint in another terminal:

```bash
curl http://localhost:5209/api/driver-performance
```

You should get a JSON response with driver performance data.

## Project Goal

Build a production-style internal tool with:

- Realistic domain modeling
- Clean full-stack architecture (`Blazor` + `.NET`)
- A tool-driven AI layer (not just a chatbot)
- Basic evaluation, logging, and reliability

## Scope (V1 - Intentionally Tight)

### Core System

#### Domain (Keep It Small)

- `Driver`
- `Race`
- `LapSummary` (aggregated, not full telemetry)
- `CarPart`

#### Data

- Seeded dataset (`10-20` races)
- Simulated lap times and performance trends
- Basic part upgrades
- No ingestion pipelines (keep it simple)

#### UI (Blazor)

- One high-quality dashboard only: **Driver Performance View**
  - Average lap time
  - Delta to teammate
  - Trend over races
  - Filtering by driver and race

#### Architecture

- Vertical Slice structure
- Queries separated from UI
- Lightweight design (avoid over-engineering)

## AI Layer (Minimal but Correct)

### Ask PitWall

A simple interface where users can ask:

- "Who is more consistent?"
- "Why did Driver A lose time in Race 3?"

### Key Principle: Tool Calling

The AI must call real backend functions (no hallucinated data).

Example tools:

- `GetDriverPerformance(driverId)`
- `CompareDrivers(driverA, driverB)`

### Deterministic Core

All calculations are done in C#. AI is only used to:

- Explain
- Summarize
- Interpret

## What Makes This Project Strong

1. **Evaluation (Top Signal)**
  - `5-10` fixed questions
  - Expected answers stored
  - AI responses compared against expected outputs
2. **Logging**
  - User question
  - Tool calls
  - AI response
  - Latency
3. **Cost Awareness**
  - Token usage tracked per request
4. **Fallbacks**
  - If AI fails, show raw stats

## Tech Stack

### App

- `.NET 10`
- Blazor Web App (Interactive Server)
- ASP.NET Core

### Data

- EF Core
- Azure SQL (or PostgreSQL)

### AI

- Azure OpenAI (tool calling)

### Infrastructure

- Docker
- Azure Container Apps
- Azure Container Registry

### Observability

- Application Insights
- Structured logging

## First Steps Plan (Execution Order)

### Step 1 - Project Foundation (Day 1)

- Step 1 complete
- Create the Blazor Web App skeleton
- Set up solution structure using Vertical Slices
- Configure EF Core with local DB provider for dev
- Add initial domain entities: `Driver`, `Race`, `LapSummary`, `CarPart`

### Step 2 - Seed and Verify Data (Day 1)

- Step 2 complete
- Build deterministic seed data for `10-20` races
- Add simple validation checks (record counts, relationships, value ranges)
- Expose one internal query endpoint to verify seeded data quickly

Quick verify endpoint:

```bash
curl http://localhost:5209/internal/seed-validation
```

### Step 3 - Build Driver Performance Dashboard (Days 2-3)

- Step 3 complete
- Implement core read queries for average lap time
- Implement core read queries for teammate delta
- Implement core read queries for multi-race trend
- Build one polished page with filtering by driver and race
- Add loading/error/empty states

### Step 4 - Add Ask PitWall with Tool Calling (Days 4-5)

- Step 4 complete
- Define tool contracts (`GetDriverPerformance`, `CompareDrivers`)
- Wire AI chat flow to call backend tools only
- Render final responses with source metrics included
- Add fallback path to show raw stats when AI/tool call fails

### Step 5 - Add Quality Signals (End of Week 1)

- Step 5 in progress
- Done: request logging (question, tool calls, latency, answer)
- Done: token usage tracking
- Pending: create a small golden-question eval set (`5-10` prompts)
- Pending: run baseline evaluation and capture initial quality metrics

## Definition of Done (V1)

The project is complete when:

- Users can explore driver performance
- Users can ask questions and get data-backed answers
- AI responses are logged and evaluated
- The app is deployed and usable
- The app uses **RAG** to ground responses with simulated data (see `/sample-data/rag`). Two PowerShell scripts handle ingestion: one generates embeddings for each chunk via Azure OpenAI, and one uploads them to Azure AI Search. At query time the flow is:
  1. **Embed** — the user's question is converted into a vector (a list of numbers representing its meaning) using Azure OpenAI
  2. **Retrieve** — that vector is used to find the top-k most semantically similar document chunks in Azure AI Search via vector similarity search (using geometric closeness)
  3. **Inject** — the retrieved chunks are placed into the system prompt so the model answers from real source material rather than hallucinating facts

## Future Roadmap

### Missing Foundations (Keep It Tight)

- **Security and Compliance**: secret management (`Key Vault` / env), RBAC, audit/privacy policy, dependency/SAST scanning
- **Data Governance**: data contracts/versioning, lineage, retention/deletion policy, PII classification
- **Testing Strategy**: contract tests for tools/APIs, load/perf coverage, failure-injection smoke tests
- **Release Safety**: feature flags, canary/blue-green rollout, rollback playbooks
- **Model Operations**: prompt/version registry, model change management, drift detection, re-baselining
- **Incident Operations**: on-call runbooks, SLO/SLA + error budgets, postmortem process

### Advanced AI Systems

- Multi-agent systems (strategy, finance, performance agents)
- Agent orchestration (planning -> tool use -> evaluation loops)
- Memory systems (short-term vs long-term)
- Feedback loops from past predictions and outcomes
- Human-in-the-loop approval workflows

### Data and Retrieval (RAG)

- Retrieval over race reports, engineer notes, and setup history
- Vector search (embeddings + similarity)
- Hybrid search (keyword + semantic)
- Create and upload embeddings for chunked race knowledge into Azure AI Search index
- Data freshness pipelines
- Optional pre-RAG ingestion pipeline: transcribe voice recordings and normalize social/media notes into canonical markdown, then chunk + enrich with metadata for indexing

### Evaluation and AI Quality

- Golden datasets for regression testing
- Offline evaluation pipelines
- LLM-as-judge scoring systems
- A/B testing AI strategies
- Evaluation dashboards (accuracy, latency, cost)

### Simulation and Decision Systems

- Race simulation engine (Monte Carlo scenarios)
- Strategy optimization (pit windows, tire strategy)
- Predictive modeling (performance + risk)
- Scenario testing at scale

### Backend and Distributed Systems

- Event-driven architecture (for example, `RaceCompleted`, `PartFailed`)
- Background processing (workers, queues)
- Idempotent command handling
- Advanced caching (Redis)
- Scalable data access patterns

### Cloud and Infrastructure

- Container orchestration (Kubernetes / AKS)
- Infrastructure as Code (Bicep / Terraform)
- Multi-environment CI/CD pipelines
- Cost monitoring and optimization

### Reliability and Production Readiness

- Resilient AI systems (fallbacks, retries, timeouts)
- Guardrails (schema validation, constrained outputs)
- Rate limiting and protection
- Failure analysis and recovery flows

### Observability (AI and Systems)

- End-to-end tracing (request -> AI -> tool calls)
- Structured logs for AI decision flow
- Monitoring latency and failure rates
- Debugging AI behavior like distributed systems

### Architecture Evolution

- Modular monolith to microservices (if needed)
- Service boundaries and domain ownership
- Read/write workload scaling
- High concurrency handling

### AI Product and UX Design

- Explainable AI outputs (confidence, reasoning)
- Decision transparency for users
- AI-assisted workflows (not full automation)
- Trust-building via audit logs

### Cost-Aware Engineering

- Cost per AI request
- Token usage optimization
- Latency/quality/cost tradeoff tuning
- Cost-efficient pipeline design

### Advanced Engineering Mindset

- Design feedback-loop systems, not static features
- Think in tradeoffs (latency vs accuracy, cost vs quality)
- Build for real-world constraints, not demos
- Prioritize decision impact over technical novelty

## Outcome

This project demonstrates:

- Full-stack .NET capability
- Clean architecture and domain modeling
- Real-world AI integration (tool-based, not superficial)
- Production-minded engineering (logging, evaluation, reliability)

Goal: build something that feels like a real internal product, not a demo.