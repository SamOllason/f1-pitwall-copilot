using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Features.AskPitWall;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Features.AskPitWall;

public sealed class AzureSearchRagService(
    OpenAiSettings openAiSettings,
    AzureSearchSettings searchSettings,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment hostEnvironment,
    ILogger<AzureSearchRagService> logger) : IRagContextService, IRagIndexBootstrapper
{
    private const string SearchApiVersion = "2024-07-01";
    private const string OpenAiApiVersion = "2024-02-15-preview";
    private const int EmbeddingDimensions = 1536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnsureIndexedAsync(CancellationToken cancellationToken = default)
    {
        var configurationIssues = GetConfigurationIssues();
        if (configurationIssues.Count > 0)
        {
            logger.LogWarning(
                "RAG bootstrap skipped because configuration is incomplete. Missing/placeholder settings: {Settings}.",
                string.Join(", ", configurationIssues));
            return;
        }

        try
        {
            // Check document count first — if the index already has data, skip the schema
            // PUT entirely. Azure Search rejects PUT requests that try to change immutable
            // fields (like algorithm config) on an existing index.
            var count = await GetDocumentCountAsync(cancellationToken);
            if (count > 0)
            {
                logger.LogInformation("RAG index already contains {DocumentCount} documents, skipping bootstrap.", count);
                return;
            }

            await EnsureIndexExistsAsync(cancellationToken);

            var chunksPath = Path.GetFullPath(Path.Combine(
                hostEnvironment.ContentRootPath,
                "..",
                "..",
                "sample-data",
                "rag",
                "chunks.ndjson"));

            if (!File.Exists(chunksPath))
            {
                logger.LogWarning("RAG chunks file not found at {ChunksPath}.", chunksPath);
                return;
            }

            var chunks = await LoadChunksAsync(chunksPath, cancellationToken);
            if (chunks.Count == 0)
            {
                logger.LogWarning("RAG chunks file is empty, skipping indexing.");
                return;
            }

            var actions = new List<object>(chunks.Count);
            foreach (var chunk in chunks)
            {
                var embedding = await CreateEmbeddingAsync(chunk.Content, cancellationToken);
                actions.Add(new Dictionary<string, object?>
                {
                    ["@search.action"] = "mergeOrUpload",
                    ["id"] = chunk.Id,
                    ["content"] = chunk.Content,
                    ["contentVector"] = embedding,
                    ["season"] = chunk.Season,
                    ["race"] = chunk.Race,
                    ["circuit"] = chunk.Circuit,
                    ["driver"] = chunk.Driver,
                    ["docType"] = chunk.DocType,
                    ["source"] = chunk.Source
                });
            }

            var payload = JsonSerializer.Serialize(new { value = actions }, JsonOptions);
            using var request = BuildSearchRequest(
                HttpMethod.Post,
                $"indexes/{searchSettings.IndexName}/docs/index?api-version={SearchApiVersion}",
                payload);

            using var response = await CreateSearchClient().SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Indexed {ChunkCount} RAG chunks into Azure AI Search.", chunks.Count);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "RAG bootstrap skipped because Azure Search/OpenAI endpoint is unreachable.");
        }
    }

    public async Task<IReadOnlyList<RagContextChunkDto>> RetrieveAsync(
        string question,
        int top = 5,
        CancellationToken cancellationToken = default)
    {
        if (GetConfigurationIssues().Count > 0 || string.IsNullOrWhiteSpace(question))
        {
            return [];
        }

        var embedding = await CreateEmbeddingAsync(question, cancellationToken);
        var payload = JsonSerializer.Serialize(new
        {
            count = false,
            search = "*",
            top,
            vectorQueries = new[]
            {
                new
                {
                    kind = "vector",
                    vector = embedding,
                    fields = "contentVector",
                    k = top
                }
            },
            select = "id,content,source,driver,race,circuit,docType"
        }, JsonOptions);

        using var request = BuildSearchRequest(
            HttpMethod.Post,
            $"indexes/{searchSettings.IndexName}/docs/search?api-version={SearchApiVersion}",
            payload);

        using var response = await CreateSearchClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("RAG retrieval failed with status {StatusCode}.", response.StatusCode);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<RagContextChunkDto>();
        foreach (var row in rows.EnumerateArray())
        {
            var score = row.TryGetProperty("@search.score", out var scoreElement)
                ? scoreElement.GetDouble()
                : 0d;

            results.Add(new RagContextChunkDto(
                Id: row.GetProperty("id").GetString() ?? string.Empty,
                Content: row.GetProperty("content").GetString() ?? string.Empty,
                Source: row.GetProperty("source").GetString() ?? string.Empty,
                Driver: row.GetProperty("driver").GetString() ?? string.Empty,
                Race: row.GetProperty("race").GetString() ?? string.Empty,
                Circuit: row.GetProperty("circuit").GetString() ?? string.Empty,
                DocType: row.GetProperty("docType").GetString() ?? string.Empty,
                Score: score));
        }

        return results;
    }

    private async Task EnsureIndexExistsAsync(CancellationToken cancellationToken)
    {
        var schema = JsonSerializer.Serialize(new
        {
            name = searchSettings.IndexName,
            fields = new object[]
            {
                new { name = "id", type = "Edm.String", key = true, searchable = false, filterable = true, sortable = false, facetable = false },
                new { name = "content", type = "Edm.String", searchable = true, filterable = false, sortable = false, facetable = false },
                new { name = "contentVector", type = "Collection(Edm.Single)", searchable = true, dimensions = EmbeddingDimensions, vectorSearchProfile = "rag-vector-profile" },
                new { name = "season", type = "Edm.Int32", searchable = false, filterable = true, sortable = true, facetable = true },
                new { name = "race", type = "Edm.String", searchable = true, filterable = true, sortable = true, facetable = true },
                new { name = "circuit", type = "Edm.String", searchable = true, filterable = true, sortable = true, facetable = true },
                new { name = "driver", type = "Edm.String", searchable = true, filterable = true, sortable = true, facetable = true },
                new { name = "docType", type = "Edm.String", searchable = true, filterable = true, sortable = true, facetable = true },
                new { name = "source", type = "Edm.String", searchable = true, filterable = true, sortable = true, facetable = false }
            },
            vectorSearch = new
            {
                algorithms = new[]
                {
                    new { name = "rag-hnsw", kind = "hnsw" }
                },
                profiles = new[]
                {
                    new { name = "rag-vector-profile", algorithm = "rag-hnsw" }
                }
            }
        }, JsonOptions);

        using var request = BuildSearchRequest(
            HttpMethod.Put,
            $"indexes/{searchSettings.IndexName}?api-version={SearchApiVersion}",
            schema);

        using var response = await CreateSearchClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Azure AI Search index creation failed with {StatusCode}: {Body}", response.StatusCode, body);
            return;
        }

        logger.LogInformation("Azure AI Search index '{IndexName}' is ready.", searchSettings.IndexName);
    }

    private async Task<long> GetDocumentCountAsync(CancellationToken cancellationToken)
    {
        using var request = BuildSearchRequest(
            HttpMethod.Get,
            $"indexes/{searchSettings.IndexName}/docs/$count?api-version={SearchApiVersion}");
        using var response = await CreateSearchClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return long.TryParse(content, out var count) ? count : 0;
    }

    private async Task<List<float>> CreateEmbeddingAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(openAiSettings.Endpoint) || string.IsNullOrWhiteSpace(openAiSettings.ApiKey))
        {
            return [];
        }

        var endpoint = openAiSettings.Endpoint.TrimEnd('/');
        var url = $"{endpoint}/openai/deployments/{openAiSettings.EmbeddingModel}/embeddings?api-version={OpenAiApiVersion}";
        var payload = JsonSerializer.Serialize(new { input }, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", openAiSettings.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await CreateOpenAiClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var embedding = json.RootElement.GetProperty("data")[0].GetProperty("embedding");
        return embedding.EnumerateArray().Select(x => x.GetSingle()).ToList();
    }

    private HttpClient CreateSearchClient()
    {
        var client = httpClientFactory.CreateClient(nameof(AzureSearchRagService) + ":search");
        client.BaseAddress = new Uri(searchSettings.Endpoint!.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("api-key", searchSettings.ApiKey);
        return client;
    }

    private HttpClient CreateOpenAiClient()
    {
        var client = httpClientFactory.CreateClient(nameof(AzureSearchRagService) + ":openai");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private HttpRequestMessage BuildSearchRequest(HttpMethod method, string relativePath, string? body = null)
    {
        var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private List<string> GetConfigurationIssues()
    {
        var issues = new List<string>();
        AddIssueIfInvalid(issues, searchSettings.Endpoint, "AzureSearch:Endpoint");
        AddIssueIfInvalid(issues, searchSettings.ApiKey, "AzureSearch:ApiKey");
        AddIssueIfInvalid(issues, searchSettings.IndexName, "AzureSearch:IndexName");
        AddIssueIfInvalid(issues, openAiSettings.Endpoint, "OpenAi:Endpoint");
        AddIssueIfInvalid(issues, openAiSettings.ApiKey, "OpenAi:ApiKey");
        AddIssueIfInvalid(issues, openAiSettings.EmbeddingModel, "OpenAi:EmbeddingModel");
        return issues;
    }

    private static bool LooksLikePlaceholder(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("your-")
            || normalized.Contains("example")
            || normalized.Contains("placeholder")
            || normalized.Contains("<");
    }

    private static void AddIssueIfInvalid(List<string> issues, string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value) || LooksLikePlaceholder(value))
        {
            issues.Add(key);
        }
    }

    private static async Task<List<RagChunkSeedRow>> LoadChunksAsync(string path, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var rows = new List<RagChunkSeedRow>();
        foreach (var line in lines.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var row = JsonSerializer.Deserialize<RagChunkSeedRow>(line, JsonOptions);
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private sealed record RagChunkSeedRow(
        string Id,
        string Content,
        int Season,
        string Race,
        string Circuit,
        string Driver,
        string DocType,
        string Source);
}
