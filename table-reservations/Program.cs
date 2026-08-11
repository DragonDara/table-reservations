using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using table_reservations.Services;


namespace table_reservations
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });
            builder.Services.AddEndpointsApiExplorer();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            builder.Services.AddScoped<IGoogleSheetsService, GoogleSheetsService>();
            builder.Services.AddHttpClient<IWhatsAppNotificationService, WhatsAppNotificationService>();
            builder.Services.AddHostedService<ReservationReminderService>();
            builder.Services.AddHttpClient<DgisRatingService>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowWebFlow", policy =>
                {
                    policy.WithOrigins("https://tablereserve-829889.webflow.io", "https://www.bron.cafe", "https://bron.cafe", "https://theveil.bron.cafe" , "https://the-tochka-bot-clzgj.ondigitalocean.app" , "http://localhost:5173" , "https://thetochka.bron.cafe")
                           .AllowAnyHeader()
                           .AllowAnyMethod()
                           .AllowCredentials();
                });
            });

            #region POS-интеграция(iiko)

            builder.Services.AddHttpClient(nameof(table_reservations.Pos.IikoAdapter), client =>
            {
                client.BaseAddress = new Uri(builder.Configuration["Iiko:BaseUrl"]!);
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            // Регистрируем IPosAdapter как AuthenticatingPosAdapter(IikoAdapter) —
            // декоратор сам следит, что токен не протух, и реавторизуется при необходимости.
            // Scoped — как и остальные сервисы в проекте, живёт в пределах одного HTTP-запроса.
            builder.Services.AddScoped<table_reservations.Pos.IPosAdapter>(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var http = httpClientFactory.CreateClient(nameof(table_reservations.Pos.IikoAdapter));

                var iiko = new table_reservations.Pos.IikoAdapter(
                    http,
                    builder.Configuration["Iiko:ApiLogin"]!,
                    builder.Configuration["Iiko:OrganizationId"]!);

                return new table_reservations.Pos.AuthenticatingPosAdapter(iiko);
            });

            // Фабрика — пригодится, если позже подключишь второй POS (Paloma, r_keeper и т.д.)
            builder.Services.AddScoped<table_reservations.Pos.PosAdapterFactory>();
            
            // Фасад над фабрикой — именно его внедряют контроллеры
            builder.Services.AddScoped<table_reservations.Pos.PosBookingService>(); // ← добавить эту строку
            #endregion

            var app = builder.Build();
 
           app.UseCors("AllowWebFlow");

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference();
                app.UseHttpsRedirection();
            }

            app.UseDefaultFiles();   // ищет index.html как дефолтный документ
            app.UseStaticFiles();    // раздаёт файлы из wwwroot

            app.UseAuthorization();
            app.MapControllers();

            app.MapFallbackToFile("index.html"); // SPA-роутинг: всё, что не API — на index.html

            app.Run();
        }
    }
}

