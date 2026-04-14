using Application.Features.DriverPerformance;
using Infrastructure.Features.DriverPerformance;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PitWallDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDriverPerformanceQueryService, DriverPerformanceQueryService>();
        return services;
    }
}
