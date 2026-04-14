using Application.Features.AskPitWall;
using Application.Features.DriverPerformance;
using Infrastructure.Features.AskPitWall;
using Infrastructure.Features.DriverPerformance;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        var openAiApiKey = configuration["OPENAI_API_KEY"];
        var openAiModel = configuration["OPENAI_MODEL"] ?? "gpt-4.1-mini";
        var openAiEndpoint = configuration["OPENAI_ENDPOINT"];
        var openAiEmbeddingModel = configuration["OPENAI_EMBEDDING_MODEL"] ?? "text-embedding-3-small";
        var azureSearchEndpoint = configuration["AZURE_SEARCH_ENDPOINT"];
        var azureSearchApiKey = configuration["AZURE_SEARCH_API_KEY"];
        var azureSearchIndexName = configuration["AZURE_SEARCH_INDEX_NAME"] ?? "f1-rag-index";

        services.AddHttpClient();
        services.AddDbContext<PitWallDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDriverPerformanceQueryService, DriverPerformanceQueryService>();
        services.AddScoped<IPitWallToolService, PitWallToolService>();
        services.AddSingleton(new OpenAiSettings(openAiApiKey, openAiModel, openAiEndpoint, openAiEmbeddingModel));
        services.AddSingleton(new AzureSearchSettings(azureSearchEndpoint, azureSearchApiKey, azureSearchIndexName));
        services.AddSingleton<IRagContextService, AzureSearchRagService>();
        services.AddSingleton<IRagIndexBootstrapper, AzureSearchRagService>();
        services.AddScoped<AskPitWallService>();
        services.AddScoped<IAskPitWallService, OpenAiAskPitWallService>();
        return services;
    }
}
