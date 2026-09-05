using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using table_reservations.Configuration;
using table_reservations.Data;
using table_reservations.Models;
using table_reservations.Middleware;
using table_reservations.Services;
using table_reservations.Services.BusinessTypes;
using table_reservations.Services.Tenancy;


namespace table_reservations
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var migrate = args.Contains("--migrate");
            var checkDb = args.Contains("--check-db");
            var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate" && arg != "--check-db").ToArray());

            // Add services to the container.
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });
            builder.Services.AddEndpointsApiExplorer();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            #region Multitenancy (per-organization) + pluggable business types

            builder.Services.Configure<OrganizationsOptions>(
                builder.Configuration.GetSection(OrganizationsOptions.SectionName));
            builder.Services.Configure<TenantRoutingOptions>(
                builder.Configuration.GetSection(TenantRoutingOptions.SectionName));

            builder.Services.AddSingleton<OrganizationRegistry>();
            builder.Services.AddScoped<TenantContext>();

            // Business-type strategies + resolver.
            builder.Services.AddSingleton<IBusinessTypeStrategy, RestaurantStrategy>();
            builder.Services.AddSingleton<IBusinessTypeStrategy, CarWashStrategy>();
            builder.Services.AddSingleton<IBusinessTypeStrategyResolver, BusinessTypeStrategyResolver>();

            #endregion

            builder.Services.Configure<TursoOptions>(
                builder.Configuration.GetSection(TursoOptions.SectionName));
            builder.Services.AddHttpClient<ITursoClient, TursoClient>();
            builder.Services.AddSingleton<DatabaseInitializer>();
            builder.Services.AddScoped<IReservationRepository, TursoReservationRepository>();
            builder.Services.AddHttpClient<IWhatsAppNotificationService, WhatsAppNotificationService>();
            if (builder.Configuration.GetValue("ReservationReminders:Enabled", true))
                builder.Services.AddHostedService<ReservationReminderService>();
            builder.Services.AddHttpClient<DgisRatingService>();

            // Allowed CORS origins: static list plus every configured tenant subdomain
            // under bron.cafe, so new organizations work without editing code.
            var organizations = builder.Configuration
                .GetSection(OrganizationsOptions.SectionName)
                .Get<OrganizationsOptions>() ?? new OrganizationsOptions();

            var allowedOrigins = new List<string>
            {
                "https://tablereserve-829889.webflow.io",
                "https://www.bron.cafe",
                "https://bron.cafe",
                "https://theveil.bron.cafe",
                "https://the-tochka-bot-clzgj.ondigitalocean.app",
                "http://localhost:5173",
                "https://thetochka.bron.cafe"
            };

            foreach (var subdomain in organizations.Items
                         .SelectMany(o => o.Subdomains ?? Array.Empty<string>())
                         .Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                allowedOrigins.Add($"https://{subdomain}.bron.cafe");
            }

            var corsOrigins = allowedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowWebFlow", policy =>
                {
                    policy.WithOrigins(corsOrigins)
                           .AllowAnyHeader()
                           .AllowAnyMethod()
                           .AllowCredentials();
                });
            });

            #region POS integration

            // Each request creates its POS adapter from the resolved tenant's
            // configuration. A shared named client is safe because its base address
            // is assigned on the request-scoped adapter instance.
            builder.Services.AddHttpClient("TenantPos", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            builder.Services.AddScoped<table_reservations.Pos.PosBookingService>();

            #endregion

            var app = builder.Build();

            // Создаём/обновляем схему Turso до начала обслуживания запросов.
            if (migrate)
            {
                app.Services.GetRequiredService<DatabaseInitializer>().MigrateAsync().GetAwaiter().GetResult();
                return;
            }
            app.Services.GetRequiredService<DatabaseInitializer>()
                .InitializeAsync()
                .GetAwaiter()
                .GetResult();

            if (checkDb)
            {
                foreach (var organization in app.Services.GetRequiredService<OrganizationRegistry>().All)
                {
                    using var scope = app.Services.CreateScope();
                    scope.ServiceProvider.GetRequiredService<TenantContext>().Set(organization);
                    var repository = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
                    var now = Constants.ReservationDateTime.KazakhstanNow();
                    var day = DateOnly.FromDateTime(now).AddDays(1);
                    if (organization.BusinessType == Models.Tenancy.BusinessType.CarWash)
                    {
                        var catalog = repository.GetCarWashCatalogAsync().GetAwaiter().GetResult();
                        var availability = repository.GetCarWashAvailabilityAsync(day, new("car", ["complex_wash"])).GetAwaiter().GetResult();
                        app.Logger.LogInformation("{Organization}: {Categories} categories, {Prices} prices, full wash {Minutes} min / {Price} KZT, {Slots} available starts tomorrow.",
                            organization.Id, catalog.Categories.Count, catalog.Services.Count, availability.Quote.DurationMinutes, availability.Quote.TotalKzt, availability.Slots.Count);
                    }
                    else
                    {
                        var tables = repository.GetTablesAsync().GetAwaiter().GetResult();
                        var slots = repository.GetAvailableSlotsAsync(day, now).GetAwaiter().GetResult();
                        app.Logger.LogInformation("{Organization}: {Tables} tables, {Slots} available starts tomorrow.", organization.Id, tables.Count, slots.Count);
                    }
                }
                return; // Read-only check: never start hosted notification workers.
            }

            app.UseCors("AllowWebFlow");
            app.Use(async (context, next) =>
            {
                try { await next(context); }
                catch (BookingException ex)
                {
                    context.Response.StatusCode = ex.Status;
                    await context.Response.WriteAsJsonAsync(new { message = ex.Message, code = ex.Code, existing = ex.Existing });
                }
                catch (Exception ex) when ((ex is TursoException or HttpRequestException or TaskCanceledException) && !context.RequestAborted.IsCancellationRequested)
                {
                    app.Logger.LogError(ex, "Booking database request failed.");
                    context.Response.StatusCode = 503;
                    await context.Response.WriteAsJsonAsync(new { message = "Сервис записи временно недоступен. Попробуйте позже." });
                }
            });

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference();
                app.UseHttpsRedirection();
            }

            app.UseDefaultFiles();   // ищет index.html как дефолтный документ
            app.UseStaticFiles();    // раздаёт файлы из wwwroot

            // Определяем организацию (tenant) по субдомену / заголовку X-Organization-Id.
            app.UseMiddleware<TenantResolutionMiddleware>();

            app.UseAuthorization();
            app.MapControllers();

            app.MapFallbackToFile("index.html"); // SPA-роутинг: всё, что не API — на index.html

            app.Run();
        }
    }
}

