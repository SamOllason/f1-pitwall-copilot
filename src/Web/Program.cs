using Web.Components;
using Application.Features.DriverPerformance;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddInfrastructure("Data Source=pitwall.db");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PitWallDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await DataSeeder.SeedAsync(dbContext);
}

app.MapGet("/api/driver-performance", async (IDriverPerformanceQueryService queryService, CancellationToken ct) =>
{
    var data = await queryService.GetOverviewAsync(ct);
    return Results.Ok(data);
});

app.MapGet("/internal/seed-validation", async (PitWallDbContext dbContext, CancellationToken ct) =>
{
    var validation = await DataSeeder.ValidateAsync(dbContext, ct);
    return Results.Ok(validation);
});

app.Run();
