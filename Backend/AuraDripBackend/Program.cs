using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using AuraDripBackend.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PostHog;

var builder = WebApplication.CreateBuilder(args);

// Підключаємо Sentry
var sentryDsn = builder.Configuration["Sentry:Dsn"];
// Перевіряємо, чи є ключ і чи він справжній (щоб не пропустити заглушку з appsettings.json)
bool isSentryConfigured = !string.IsNullOrEmpty(sentryDsn) && sentryDsn.StartsWith("http");

if (isSentryConfigured)
{
    // Підключаємо Sentry тільки якщо є реальний ключ
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = sentryDsn;
        o.TracesSampleRate = 1.0;
        o.EnableLogs = true;
    });
}
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 1. Читаємо налаштування з appsettings.json
var postHogKey = builder.Configuration["PostHog:ApiKey"] ?? "dummy_key_for_tests";
var postHogHost = builder.Configuration["PostHog:Host"] ?? "https://us.i.posthog.com";

// 2. Реєструємо клієнт PostHog, використовуючи ці змінні
builder.Services.AddSingleton<IPostHogClient>(sp =>
    new PostHogClient(new PostHogOptions
    {
        ProjectApiKey = postHogKey,
        HostUrl = new Uri(postHogHost)
    })
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

if (isSentryConfigured)
{
    app.UseSentryTracing();
}

app.UseAuthorization();

app.MapControllers();

// --- Блок автоматичного завантаження JSON в базу ---
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AuraDripBackend.Data.AppDbContext>();

    // Перевіряємо, чи БД підтримує міграції(справжня БД, а не InMemory для тестів)
    if (context.Database.IsRelational())
    {
        context.Database.Migrate();
    }

    // 1. Формуємо шлях до файлу
    var basePath = AppContext.BaseDirectory;
    var filePath = Path.Combine(basePath, "Data", "plantscatalog.json");

    // 2. Якщо файл є і база порожня - завантажуємо
    if (File.Exists(filePath) && !context.PlantCatalogs.Any())
    {
        var jsonText = File.ReadAllText(filePath);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var plantsFromJson = System.Text.Json.JsonSerializer.Deserialize<List<AuraDripBackend.Models.PlantCatalog>>(jsonText, options);

        if (plantsFromJson != null && plantsFromJson.Any())
        {
            context.PlantCatalogs.AddRange(plantsFromJson);
            context.SaveChanges();
            Console.WriteLine("Plant catalog successfully loaded from JSON!");
        }
    }

    /* ==== ТИМЧАСОВО ВІДКЛЮЧЕНО ГЕНЕРАЦІЮ ТЕЛЕМЕТРІЇ ====
    // Перевіряємо, чи існує взагалі рослина з Id = 1
    var testPlant = context.Plants.FirstOrDefault(p => p.Id == 1);

    if (testPlant != null)
    {
        // Перевіряємо, чи порожня телеметрія для цієї рослини
        bool hasTelemetry = context.Telemetries.Any(t => t.PlantId == 1);

        if (!hasTelemetry)
        {
            var telemetries = new List<AuraDripBackend.Models.Telemetry>();
            var random = new Random();

            // Починаємо генерувати дані з моменту "14 днів тому"
            var startDate = DateTime.UtcNow.AddDays(-14);

            int currentMoisture = 85; // Початкова вологість (щойно полили)

            // Генеруємо дані кожні 4 години (6 записів на день * 14 днів = 84 записи)
            for (int i = 0; i <= 14 * 6; i++)
            {
                var recordDate = startDate.AddHours(i * 4);

                // Імітуємо висихання ґрунту: кожні 4 години вологість падає на 2-5%
                currentMoisture -= random.Next(2, 6);

                // Якщо ґрунт висох (впав нижче 25%) - імітуємо полив
                if (currentMoisture <= 25)
                {
                    currentMoisture = random.Next(80, 95); // Різкий скачок вологості
                }

                telemetries.Add(new AuraDripBackend.Models.Telemetry
                {
                    PlantId = 1,
                    Timestamp = recordDate,
                    SoilMoisture = currentMoisture,
                    // Температура кімнатна: від 20.0 до 24.9
                    AirTemperature = 20.0 + random.NextDouble() * 5.0,
                    // Вологість повітря: від 40% до 60%
                    AirHumidity = 40.0 + random.NextDouble() * 20.0
                });
            }

            // Зберігаємо згенеровані дані в базу
            context.Telemetries.AddRange(telemetries);
            context.SaveChanges();
            Console.WriteLine("Успішно згенеровано 2 тижні тестової телеметрії для рослини #1!");
        }
    }
    else
    {
        Console.WriteLine("Рослина з Id = 1 не знайдена. Телеметрія не згенерована. Створіть рослину спочатку.");
    }
    ==== КІНЕЦЬ ЗАКРИТОГО БЛОКУ ==== */
}

// Тестове посилання з підтримкою Feature Flags (Лабораторна 5, Крок 5)
app.MapGet("/api/check-catalog", async (AuraDripBackend.Data.AppDbContext db, IPostHogClient ph) =>
{
    // 1. Перевіряємо статус прапорця у PostHog для користувача "server_admin"
    // Назви прапорець у панелі PostHog як 'show-my-plants'
    var isExtendedEnabled = await ph.IsFeatureEnabledAsync("show-my-plants", "server_admin");

    // 2. Отримуємо основний каталог
    var catalog = await db.PlantCatalogs.ToListAsync();

    // 3. Якщо прапорець увімкнено — додаємо особисті рослини користувача
    if (isExtendedEnabled)
    {
        var myPlants = await db.Plants.ToListAsync();

        return Results.Ok(new
        {
            Message = "Feature Flag 'show-my-plants' активний! Отримано розширені дані.",
            Catalog = catalog,
            MyCurrentPlants = myPlants // Ті самі "додаткові рослини, що наявні в нього"
        });
    }

    // 4. Якщо прапорець вимкнено — повертаємо лише стандартний каталог
    return Results.Ok(new
    {
        Message = "Стандартний режим. Feature Flag вимкнено.",
        Catalog = catalog
    });
});

var posthog = app.Services.GetRequiredService<IPostHogClient>();
posthog.Capture("server_admin", "backend_started");

app.Run();

public partial class Program { }
//до невидимого класу Program, який компілятор сам створив, просимо додати статус public (публічний),
//щоб мої тести з сусіднього проєкту могли його бачити і запускати