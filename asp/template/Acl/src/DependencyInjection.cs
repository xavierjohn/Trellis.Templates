namespace TodoSample.AntiCorruptionLayer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoSample.Application;
using TodoSample.Domain;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;

public static class DependencyInjection
{
    public static IServiceCollection AddAntiCorruptionLayer(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString)
                   .AddTrellisInterceptors());

        services.AddScoped<ITodoRepository, TodoRepository>();
        services.AddScoped<SharedResourceLoaderById<TodoItem, TodoId>, TodoItemResourceLoader>();

        return services;
    }
}
