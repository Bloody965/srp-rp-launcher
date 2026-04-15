# Apocalypse Launcher API

Защищенный API сервер для системы аутентификации и распространения модпаков.

## Возможности

✅ **Безопасная аутентификация**
- Регистрация с проверкой сложности пароля
- Bcrypt хеширование паролей (12 раундов)
- JWT токены с истечением срока действия
- Rate limiting против брутфорса

✅ **Защита сборки**
- Скачивание только с валидным токеном
- SHA256 проверка целостности файлов
- Whitelist система (опционально)
- Логирование всех действий

✅ **Система аудита**
- Логи всех действий пользователей
- IP адреса и временные метки
- Отслеживание попыток входа
- Ban система

## Быстрый старт

### 1. Первый запуск

```bash
cd D:\лаунчер\src\ApocalypseLauncher.API
dotnet run
```

При первом запуске сервер сгенерирует JWT секретный ключ. **ВАЖНО**: Скопируйте его и сохраните!

### 2. Настройка appsettings.json

Откройте `appsettings.json` и замените `ЗАМЕНИТЕ_ЭТОТ_КЛЮЧ` на сгенерированный ключ:

```json
{
  "Jwt": {
    "SecretKey": "ВАШ_СГЕНЕРИРОВАННЫЙ_КЛЮЧ_ЗДЕСЬ",
    "Issuer": "ApocalypseLauncher.API",
    "Audience": "ApocalypseLauncher.Client",
    "ExpirationHours": "24"
  }
}
```

### 3. Проверка работы

Откройте в браузере: `https://localhost:7000/swagger`

Вы увидите Swagger UI с документацией API.

## API Endpoints

### Аутентификация

**POST /api/auth/register**
```json
{
  "username": "Player123",
  "email": "player@example.com",
  "password": "SecurePass123"
}
```

**POST /api/auth/login**
```json
{
  "username": "Player123",
  "password": "SecurePass123"
}
```

**POST /api/auth/verify**
- Header: `Authorization: Bearer {token}`
- Проверяет валидность токена

### Модпак (требует авторизацию)

**GET /api/modpack/version**
- Получить информацию о последней версии сборки

**GET /api/modpack/download**
- Скачать сборку (только с валидным токеном)

**POST /api/modpack/verify**
```json
{
  "version": "1.0.0",
  "sha256Hash": "хеш_файла"
}
```

## Добавление новой версии модпака

### Через базу данных SQLite

1. Откройте `apocalypse_launcher.db` (DB Browser for SQLite)
2. Добавьте запись в таблицу `ModpackVersions`:

```sql
INSERT INTO ModpackVersions (Version, DownloadUrl, SHA256Hash, FileSizeBytes, Changelog, IsActive)
VALUES ('1.0.0', 'https://your-server.com/modpack-1.0.0.zip', 'SHA256_HASH_HERE', 104857600, 'Initial release', 1);
```

### Генерация SHA256 хеша

**PowerShell:**
```powershell
Get-FileHash -Path "modpack.zip" -Algorithm SHA256
```

**Linux/Mac:**
```bash
sha256sum modpack.zip
```

## Настройка хостинга

### Локальная сеть

Измените `appsettings.json`:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      },
      "Https": {
        "Url": "https://0.0.0.0:7000"
      }
    }
  }
}
```

### Публичный сервер (Railway/Render)

1. Создайте аккаунт на Railway.app или Render.com
2. Подключите GitHub репозиторий
3. Укажите:
   - Build Command: `dotnet build`
   - Start Command: `dotnet run --project src/ApocalypseLauncher.API`
4. Добавьте переменные окружения:
   - `Jwt__SecretKey`: ваш секретный ключ
   - `ConnectionStrings__DefaultConnection`: путь к БД

## Безопасность

### Rate Limiting

- **Регистрация**: 3 попытки в час с одного IP
- **Вход**: 5 попыток в 15 минут с одного IP
- Автоматическая блокировка при превышении

### Требования к паролю

- Минимум 8 символов
- Хотя бы одна заглавная буква
- Хотя бы одна строчная буква
- Хотя бы одна цифра

### JWT Токены

- Срок действия: 24 часа (настраивается)
- Автоматическая проверка истечения
- Отзыв токенов при выходе

## База данных

SQLite база создается автоматически при первом запуске.

**Таблицы:**
- `Users` - пользователи
- `LoginSessions` - активные сессии
- `AuditLogs` - логи действий
- `ModpackVersions` - версии модпаков

## Интеграция с лаунчером

В лаунчере используйте HttpClient для запросов:

```csharp
var client = new HttpClient();
client.BaseAddress = new Uri("https://your-api-server.com");

// Регистрация
var registerData = new { username = "Player", email = "test@test.com", password = "Pass123" };
var response = await client.PostAsJsonAsync("/api/auth/register", registerData);

// Получение токена
var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
string token = result.Token;

// Использование токена
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
var modpackInfo = await client.GetFromJsonAsync<ModpackInfoResponse>("/api/modpack/version");
```

## Troubleshooting

### Ошибка "JWT SecretKey not configured"
- Добавьте секретный ключ в `appsettings.json`

### Ошибка "Database is locked"
- Закройте все программы, использующие БД
- Перезапустите сервер

### CORS ошибки
- API настроен на `AllowAnyOrigin` для лаунчера
- Для production настройте конкретные домены

## Production Checklist

- [ ] Замените JWT SecretKey на безопасный
- [ ] Настройте HTTPS сертификат
- [ ] Настройте CORS для конкретных доменов
- [ ] Включите whitelist систему (`Modpack:RequireWhitelist: true`)
- [ ] Настройте резервное копирование БД
- [ ] Настройте мониторинг логов
- [ ] Обновите JWT пакеты (есть уязвимость в 7.0.3)

## Обновление JWT пакетов

```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.11
```

---

**Создано для SRP-RP проекта**
