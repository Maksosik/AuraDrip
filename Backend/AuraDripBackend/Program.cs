using System;

using AuraDripBackend.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

/*builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));*/

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("LabDB"));

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
        //context.Database.Migrate();
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