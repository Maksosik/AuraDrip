/*
#include <Arduino.h>
#include <DHT.h>
#include <SPI.h>
#include <SD.h>

// ==========================================
// 1. НАЛАШТУВАННЯ ПІНІВ (ЗМІНЮЙТЕ ТУТ)
// ==========================================
// Датчики та керування
const int transistorPin = 2;  // Керування помпою
const int soilSensorPin = 0;   // Аналоговий датчик ґрунту
const int dhtPin        = 15;  // Датчик температури/вологості
const int flowSensorPin = 7;   // Лічильник води (YF-S401)

// Піни для Micro SD адаптера (SPI)
const int sd_SCK  = 19;        // SCK (Clock)
const int sd_MISO = 21;        // MISO (Data Out з карти)
const int sd_MOSI = 22;        // MOSI (Data In в карту)
const int sd_CS   = 18;        // CS (Chip Select)
// ==========================================

#define DHTTYPE DHT11
DHT dht(dhtPin, DHTTYPE);

// Змінні для датчика витрати води
volatile long pulseCount = 0;
unsigned int flowMilliLitres = 0;
unsigned long totalMilliLitres = 0;

// Функція обробки імпульсу
void IRAM_ATTR pulseCounter() {
  pulseCount++;
}

unsigned long lastMsgTime = 0; 
const long interval = 5000; // Запис і вивід статусу кожні 5 секунд

void setup() {
  Serial.begin(115200);
  while (!Serial && millis() < 5000) { delay(10); }

  pinMode(transistorPin, OUTPUT);
  digitalWrite(transistorPin, LOW);
  pinMode(soilSensorPin, INPUT);
  
  pinMode(flowSensorPin, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(flowSensorPin), pulseCounter, FALLING);

  dht.begin();

  // Ініціалізація користувацького SPI для SD-карти
  SPI.begin(sd_SCK, sd_MISO, sd_MOSI, sd_CS);
  
  Serial.println("Ініціалізація SD-карти...");
  if (!SD.begin(sd_CS)) {
    Serial.println("[!] Помилка: SD-карту не знайдено!");
  } else {
    Serial.println(">>> SD-карта готова.");
    
    // Створюємо файл із заголовками, якщо його немає
    File file = SD.open("/datalog.csv", FILE_READ);
    if (!file) {
      file = SD.open("/datalog.csv", FILE_WRITE);
      if (file) {
        file.println("Time(ms),Temp(C),Hum(%),Soil(ADC),Water(ml),Pump");
        file.close();
      }
    } else {
      file.close();
    }
  }

  Serial.println("\n=== СИСТЕМА ГОТОВА ===");
  Serial.println("Команди:");
  Serial.println(" '1' - Увімкнути помпу");
  Serial.println(" '0' - Вимкнути помпу");
  Serial.println(" '2' - Вивантажити дані з SD-карти");
  Serial.println("======================\n");
}

void loop() {
  // 1. КЕРУВАННЯ ТА КОМАНДИ
  if (Serial.available() > 0) {
    char cmd = Serial.read();
    
    if (cmd == '1') {
      digitalWrite(transistorPin, HIGH);
      Serial.println(">>> КОМАНДА: ПОМПА ВКЛ");
    } 
    else if (cmd == '0') {
      digitalWrite(transistorPin, LOW);
      Serial.println(">>> КОМАНДА: ПОМПА ВИКЛ");
    } 
    else if (cmd == '2') {
      // Вивантаження файлу
      Serial.println("\n\n--- ПОЧАТОК ЕКСПОРТУ ДАНИХ (datalog.csv) ---");
      File dataFile = SD.open("/datalog.csv", FILE_READ);
      if (dataFile) {
        while (dataFile.available()) {
          Serial.write(dataFile.read()); // Читаємо і відправляємо побайтово
        }
        dataFile.close();
        Serial.println("--- КІНЕЦЬ ЕКСПОРТУ ДАНИХ ---\n\n");
      } else {
        Serial.println("[!] Помилка: Не вдалося відкрити файл для читання.");
      }
    }
  }

  // 2. МОНІТОРИНГ ТА ЗАПИС (кожні 5 сек)
  unsigned long currentMillis = millis();
  if (currentMillis - lastMsgTime >= interval) {
    
    noInterrupts();
    long currentPulses = pulseCount;
    pulseCount = 0; 
    interrupts();

    flowMilliLitres = (currentPulses * 1000) / 5880; 
    totalMilliLitres += flowMilliLitres;
    lastMsgTime = currentMillis;

    int soilValue = analogRead(soilSensorPin);
    float h = dht.readHumidity();
    float t = dht.readTemperature();
    bool status = digitalRead(transistorPin);

    // Вивід у термінал
    Serial.println("--- СТАТУС ---");
    Serial.printf("Повітря: %.1f°C, %.1f%%\n", isnan(t)?0:t, isnan(h)?0:h);
    Serial.printf("Ґрунт: %d | Вода: %lu мл | Помпа: %s\n", soilValue, totalMilliLitres, status ? "ON" : "OFF");

    // Запис на SD-карту
    File dataFile = SD.open("/datalog.csv", FILE_APPEND);
    if (dataFile) {
      dataFile.printf("%lu,%.1f,%.1f,%d,%lu,%d\n", currentMillis, isnan(t)?0:t, isnan(h)?0:h, soilValue, totalMilliLitres, status?1:0);
      dataFile.close();
    } else {
      Serial.println("[!] Помилка запису на SD!");
    }
  }
}
  */

#include <Arduino.h>
#include <WiFi.h>
#include <ArduinoOTA.h>
#include <Adafruit_NeoPixel.h>

// --- НАЛАШТУВАННЯ WI-FI ---
const char* ssid = "zaharonuzh 2G";
const char* password = "Zakhar2611";

// --- НАЛАШТУВАННЯ СВІТЛОДІОДА ---
#define RGB_PIN 8 
#define NUMPIXELS 1 
Adafruit_NeoPixel pixels(NUMPIXELS, RGB_PIN, NEO_GRB + NEO_KHZ800);

void setup() {
  Serial.begin(115200);
  
  pixels.begin();
  pixels.setBrightness(10);
  pixels.clear();
  pixels.show();

  Serial.println("\n--- ЗАПУСК СИСТЕМИ ---");
  Serial.println("Підключення до Wi-Fi: " + String(ssid));
  
  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, password);

  int attempts = 0;
  while (WiFi.status() != WL_CONNECTED && attempts < 20) {
    delay(500);
    Serial.print(".");
    attempts++;
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("\nУРА! Підключено!");
    Serial.print("IP-адреса вашої ESP32: ");
    Serial.println(WiFi.localIP());

    // Налаштування OTA
    ArduinoOTA.onStart([]() { Serial.println("\nПочаток оновлення OTA..."); });
    ArduinoOTA.onEnd([]() { Serial.println("\nОновлення успішно завершено!"); });
    ArduinoOTA.onProgress([](unsigned int progress, unsigned int total) {
      Serial.printf("Прогрес: %u%%\r", (progress / (total / 100)));
    });
    ArduinoOTA.onError([](ota_error_t error) {
      Serial.printf("Помилка OTA [%u]\n", error);
    });
    
    ArduinoOTA.begin();
    Serial.println("OTA працює! Готові до прошивки по повітрю.");
  } else {
    Serial.println("\nПОМИЛКА ПІДКЛЮЧЕННЯ WI-FI!");
    Serial.println("Перезавантаження...");
    delay(5000);
    ESP.restart();
  }

  // Виводимо меню для користувача
  Serial.println("\n=== КЕРУВАННЯ СВІТЛОДІОДОМ ===");
  Serial.println(" 1 - Червоний");
  Serial.println(" 2 - Зелений");
  Serial.println(" 3 - Синій");
  Serial.println(" 4 - Білий (всі кристали)");
  Serial.println(" 0 - Вимкнути");
  Serial.println("==============================\n");
}

void loop() {
  // 1. СЛУХАЄМО WI-FI ДЛЯ ПРОШИВКИ (ОБОВ'ЯЗКОВО)
  if (WiFi.status() == WL_CONNECTED) {
    ArduinoOTA.handle(); 
  }

  // 2. СЛУХАЄМО КАБЕЛЬ (UART) ДЛЯ КОМАНД
  if (Serial.available() > 0) {
    char cmd = Serial.read();
    
    // Ігноруємо пусті символи від терміналу
    if (cmd == '\n' || cmd == '\r') return;

    if (cmd == '1') {
      pixels.setPixelColor(0, pixels.Color(255, 0, 0)); // (Червоний, Зелений, Синій)
      pixels.show();
      Serial.println("-> Увімкнено ЧЕРВОНИЙ");
    } 
    else if (cmd == '2') {
      pixels.setPixelColor(0, pixels.Color(0, 255, 0)); 
      pixels.show();
      Serial.println("-> Увімкнено ЗЕЛЕНИЙ");
    }
    else if (cmd == '3') {
      pixels.setPixelColor(0, pixels.Color(0, 0, 255)); 
      pixels.show();
      Serial.println("-> Увімкнено СИНІЙ");
    }
    else if (cmd == '4') {
      pixels.setPixelColor(0, pixels.Color(255, 255, 255)); // Всі на максимум
      pixels.show();
      Serial.println("-> Увімкнено БІЛИЙ");
    }
    else if (cmd == '0') {
      pixels.clear(); // Вимкнути все
      pixels.show();
      Serial.println("-> Світлодіод ВИМКНЕНО");
    }
    else {
      Serial.println("-> Невідома команда! Введіть 1, 2, 3, 4 або 0.");
    }
  }
}