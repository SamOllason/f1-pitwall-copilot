# Sample RAG Materials (F1 Pitwall)

This folder contains sample source documents and pre-chunked records you can ingest into Azure AI Search for a fast RAG demo.

## Folder layout

- `source-docs/` -> human-readable source material (markdown)
- `chunks.ndjson` -> chunk-level records ready for indexing

## Suggested Azure AI Search index fields

- `id` (key, string)
- `content` (searchable string)
- `contentVector` (collection of floats, embedding output)
- `season` (int, filterable)
- `race` (string, filterable/facetable)
- `circuit` (string, filterable/facetable)
- `driver` (string, filterable/facetable)
- `docType` (string, filterable/facetable)
- `source` (string, retrievable)

## Ingestion flow

1. Read each `content` value from `chunks.ndjson`.
2. Generate embeddings using your Azure OpenAI embedding deployment.
3. Upload document with:
   - original fields from `chunks.ndjson`
   - `contentVector` from the embedding model

## Notes

- These are synthetic, demo-friendly materials inspired by race-weekend briefing Q&A and driver debrief analysis use cases.
- Keep doc IDs stable so you can rerun ingestion safely with merge/upsert behavior.
