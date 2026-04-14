# Architecture Notes

## RAG Flow (Simple)

```mermaid
flowchart LR
    A["sample-data/rag/chunks.ndjson"] --> B["Startup Indexer"]
    B --> C["Azure OpenAI Embeddings<br/>OPENAI_EMBEDDING_MODEL"]
    C --> D["Azure AI Search Index<br/>content + metadata + vector"]

    E["User question (Ask PitWall UI/API)"] --> F["Query Embedding"]
    F --> C
    C --> G["Vector Search (top-k chunks)"]
    G --> D
    D --> H["Retrieved context chunks"]
    H --> I["LLM + Tool Calling (OpenAI chat)"]
    I --> J["Grounded answer + source metrics"]
```

## What Happens

- At startup, the app indexes chunked F1 notes by generating embeddings and storing vectors + metadata in Azure AI Search.
- For each question, the app retrieves top matching chunks, injects that context into Ask PitWall, and returns a grounded response with source traces.
