using AuraDripBackend.Data;
using AuraDripBackend.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PostHog;

namespace AuraDripBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]// /api/app
    public class AppController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPostHogClient _posthog;

        public AppController(AppDbContext context, IPostHogClient posthog)
        {
            _context = context;
            _posthog = posthog;
        }

        [HttpGet("plants/{plantId}/status")]
        public async Task<IActionResult> GetStatus(int plantId)
        {
            var plant = await _context.Plants.FindAsync(plantId);

            if (plant == null)
            {
                return NotFound(new { message = "Plant not found" });
            }

            var LastTelemetry = await _context.Telemetries.Where(t => t.PlantId == plantId).OrderByDescending(t => t.Timestamp).FirstOrDefaultAsync();

            return Ok(new
            {
                AgeDays = (DateTime.UtcNow - plant.DatePlanted).Days,
                CurrentMoisture = LastTelemetry?.SoilMoisture ?? 0,
                CurrentTemp = LastTelemetry?.AirTemperature ?? 0,
                LastUpdate = LastTelemetry?.Timestamp
            });
        }

        [HttpPatch("plants/{plantId}/config")] // /api/app/plants/{plantId}/config
        public async Task<IActionResult> UpdateConfig(int plantId, [FromBody] UpdateConfigDto config)
        {
            // Знайходимо рослину в базі 
            var plant = await _context.Plants.FindAsync(plantId);

            if (plant == null)
            {
                return NotFound(new { message = "Plant not found" });
            }
            // Оновлюємо режим (Авто/Ручний/Фікс. поріг)
            plant.ControlMode = config.ControlMode;
            // Якщо користувач вибрав 3-й режим(Фікс. поріг):
            if (config.ControlMode == 3 && config.ManualThreshold.HasValue)
            {
                plant.MinMoistureThreshold = config.ManualThreshold.Value;
            }

            // Зберегаємо зміни в базі 
            await _context.SaveChangesAsync();

            // Відправляємо подію про зміну налаштувань у PostHog
            _posthog?.Capture("mobile_user", "config_updated", new Dictionary<string, object>
            {
                { "plant_id", plant.Id },
                { "new_mode", config.ControlMode },
                { "new_threshold", config.ManualThreshold ?? 0 }
            });

            return Ok(new
            {
                message = "Settings updated",
                mode = plant.ControlMode,
                threshold = plant.MinMoistureThreshold
            });
        }

        [HttpPost("plants/{plantId}/force-water")]// /api/app/plants/{plantId}/force-water
        public async Task<IActionResult> ForceWater(int plantId)
        {

            var plant = await _context.Plants.FindAsync(plantId);

            if (plant == null)
            {
                return NotFound(new { message = "Plant not found" });
            }

            // Поклав "записку" в поштову скриньку(полити при нагоді)
            plant.HasPendingWaterCommand = true;

            await _context.SaveChangesAsync();

            // Відправляємо подію про примусовий полив
            _posthog?.Capture("mobile_user", "water_command_sent", new Dictionary<string, object>
            {
                { "plant_id", plant.Id },
                { "plant_name", plant.Name }
            });

            return Ok(new { message = "The forced irrigation command has been successfully added to the queue!" });
        }

        [HttpGet("plants/{plantId}/statistics")]// /api/app/plants/{plantId}/statistics
        public async Task<IActionResult> GetStatistics(int plantId, [FromQuery] int days = 14)
        {
            // 1. Визначаємо дату початку (віднімаємо потрібну кількість днів від "зараз")
            var startDate = DateTime.UtcNow.AddDays(-days);

            // 2. Дістаємо всі записи для цієї рослини, які новіші за startDate
            // Використовуємо ToListAsync(), бо нам потрібні всі записи за період!
            var data = await _context.Telemetries
                .Where(t => t.PlantId == plantId && t.Timestamp >= startDate)
                .ToListAsync();

            // Перевірка: якщо за цей період ще немає жодних записів
            if (!data.Any())
            {
                return Ok(new { message = "No data" });
            }

            // 3.Використав LINQ, щоб порахувати середнє значення для SoilMoisture та AirTemperature.
            var avgMoisture = data.Average(t => t.SoilMoisture);
            var avgTemp = data.Average(t => t.AirTemperature);

            // Повертаємо звіт
            return Ok(new
            {
                PeriodDays = days,
                TotalRecords = data.Count, // Покажемо, скільки разів ESP виходила на зв'язок
                AverageMoisture = Math.Round(avgMoisture, 1), // Округлюємо до 1 знака після коми
                AverageTemperature = Math.Round(avgTemp, 1)
            });
        }
        //контролер для перевірки статусу проекту
        [HttpGet("env-status")]
        public IActionResult GetEnvironmentStatus([FromServices] IConfiguration config)
        {
            // Читаємо нашу змінну з appsettings
            var currentStatus = config["AppStatus"];
            return Ok(new { environment = currentStatus });
        }

        // Спеціальний метод для тестування Sentry (Лабораторна 6, Крок 2)
        [HttpGet("test-error")] // Маршрут: /api/app/test-error
        public IActionResult TestError()
        {
            // Крок 3.1: Додаємо контекст користувача перед помилкою
            SentrySdk.ConfigureScope(scope =>
            {
                scope.User = new SentryUser
                {
                    Id = "maksym_admin_01",
                    Email = "maksym@auradrip.local",
                    Username = "Maksym"
                };
            });

            // Навмисно генеруємо критичну помилку
            throw new Exception("Sentry Backend Test Error: Упс, сервер зламався!");
        }

        [HttpPost("logout")] // Маршрут: /api/app/logout
        public IActionResult Logout()
        {
            // Крок 3.3: Очищення контексту користувача
            SentrySdk.ConfigureScope(scope =>
            {
                // Замість null безпечніше передати порожній об'єкт SentryUser
                scope.User = new SentryUser();
            });

            return Ok(new { message = "Успішний вихід. Контекст Sentry очищено." });
        }
    }
}