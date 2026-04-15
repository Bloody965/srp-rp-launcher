# 🚀 Инструкция по развертыванию SRP-RP Launcher

## 📋 Что нужно для публичного запуска

Сейчас лаунчер работает только на твоем компьютере (`localhost:5000`). Чтобы другие игроки могли использовать лаунчер, нужно:

1. **Развернуть API сервер** на публичном хостинге
2. **Изменить URL в лаунчере** с localhost на публичный домен
3. **Пересобрать лаунчер** с новым URL
4. **Раздать EXE файл** игрокам

---

## 🌐 Шаг 1: Выбор хостинга для API

### Рекомендуемые варианты:

#### **Вариант A: Railway.app (Рекомендуется)**
- ✅ Бесплатный план: 500 часов/месяц
- ✅ Автоматический SSL сертификат
- ✅ Простой деплой через GitHub
- ✅ База данных SQLite работает из коробки
- 🔗 https://railway.app

**Стоимость:** $0-5/месяц

#### **Вариант B: Render.com**
- ✅ Бесплатный план (засыпает после 15 минут неактивности)
- ✅ Автоматический SSL
- ✅ Простой деплой
- 🔗 https://render.com

**Стоимость:** $0-7/месяц

#### **Вариант C: VPS (DigitalOcean, Hetzner, Contabo)**
- ✅ Полный контроль
- ✅ Можно хостить несколько проектов
- ⚠️ Нужно настраивать SSL вручную
- 🔗 https://www.hetzner.com (от €4.5/месяц)

**Стоимость:** €4-10/месяц

---

## 🔧 Шаг 2: Развертывание API на Railway.app

### 2.1. Подготовка проекта

1. **Создай GitHub репозиторий:**
   ```bash
   cd D:\лаунчер
   git init
   git add .
   git commit -m "Initial commit"
   git remote add origin https://github.com/ВАШ_ЮЗЕРНЕЙМ/srp-rp-launcher.git
   git push -u origin main
   ```

2. **Создай `.dockerignore` файл:**
   ```
   publish/
   logs/
   bin/
   obj/
   *.log
   *.pdb
   ```

3. **Создай `Dockerfile` в корне проекта:**
   ```dockerfile
   FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
   WORKDIR /app
   EXPOSE 5000

   FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
   WORKDIR /src
   COPY ["src/ApocalypseLauncher.API/ApocalypseLauncher.API.csproj", "ApocalypseLauncher.API/"]
   RUN dotnet restore "ApocalypseLauncher.API/ApocalypseLauncher.API.csproj"
   COPY src/ApocalypseLauncher.API/ ApocalypseLauncher.API/
   WORKDIR "/src/ApocalypseLauncher.API"
   RUN dotnet build "ApocalypseLauncher.API.csproj" -c Release -o /app/build

   FROM build AS publish
   RUN dotnet publish "ApocalypseLauncher.API.csproj" -c Release -o /app/publish

   FROM base AS final
   WORKDIR /app
   COPY --from=publish /app/publish .
   ENTRYPOINT ["dotnet", "ApocalypseLauncher.API.dll"]
   ```

4. **Обнови `appsettings.json`:**
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Information",
         "Microsoft.AspNetCore": "Warning"
       }
     },
     "AllowedHosts": "*",
     "Jwt": {
       "SecretKey": "ЗАМЕНИ_НА_СЛУЧАЙНУЮ_СТРОКУ_МИНИМУМ_32_СИМВОЛА",
       "Issuer": "ApocalypseLauncher.API",
       "Audience": "ApocalypseLauncher.Client",
       "ExpirationHours": "24"
     },
     "Modpack": {
       "RequireWhitelist": false,
       "StoragePath": "/app/modpacks"
     },
     "ConnectionStrings": {
       "DefaultConnection": "Data Source=/app/data/apocalypse_launcher.db"
     }
   }
   ```

5. **Сгенерируй секретный ключ JWT:**
   ```powershell
   # В PowerShell
   -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 64 | % {[char]$_})
   ```
   Скопируй результат и вставь в `Jwt.SecretKey`

### 2.2. Деплой на Railway

1. **Зарегистрируйся на Railway.app**
   - Перейди на https://railway.app
   - Войди через GitHub

2. **Создай новый проект:**
   - Нажми "New Project"
   - Выбери "Deploy from GitHub repo"
   - Выбери свой репозиторий

3. **Настрой переменные окружения:**
   - Перейди в Settings → Variables
   - Добавь:
     ```
     JWT_SECRET=твой_секретный_ключ_64_символа
     ASPNETCORE_URLS=http://0.0.0.0:5000
     ```

4. **Получи публичный URL:**
   - Перейди в Settings → Networking
   - Нажми "Generate Domain"
   - Получишь URL типа: `https://srp-rp-launcher-production.up.railway.app`

5. **Проверь что API работает:**
   ```
   https://твой-домен.railway.app/api/health
   ```
   Должен вернуть: `{"status":"healthy","timestamp":"...","version":"1.0.0"}`

---

## 🔄 Шаг 3: Обновление лаунчера с новым URL

### 3.1. Измени URL в коде

Открой файл: `D:\лаунчер\src\ApocalypseLauncher\ViewModels\MainWindowViewModel.cs`

Найди строку (примерно строка 32):
```csharp
_apiService = new ApiService("http://localhost:5000");
```

Замени на:
```csharp
_apiService = new ApiService("https://твой-домен.railway.app");
```

### 3.2. Пересобери лаунчер

```powershell
cd D:\лаунчер\src\ApocalypseLauncher
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o D:\лаунчер\publish
```

### 3.3. Проверь что работает

1. Запусти `D:\лаунчер\publish\ApocalypseLauncher.exe`
2. Попробуй зарегистрироваться
3. Если всё работает - лаунчер готов к раздаче!

---

## 📦 Шаг 4: Добавление модпака

### 4.1. Подготовь модпак

1. **Создай ZIP архив с модами:**
   ```
   modpack-1.0.0.zip
   ├── mods/
   │   ├── mod1.jar
   │   ├── mod2.jar
   │   └── ...
   └── config/
       └── ...
   ```

2. **Получи SHA256 хеш:**
   ```powershell
   Get-FileHash -Path "modpack-1.0.0.zip" -Algorithm SHA256
   ```

### 4.2. Загрузи модпак на хостинг

**Вариант A: GitHub Releases (Рекомендуется)**
1. Перейди в свой репозиторий на GitHub
2. Releases → Create a new release
3. Загрузи `modpack-1.0.0.zip`
4. Получи прямую ссылку на файл

**Вариант B: Облачное хранилище**
- Google Drive (получи прямую ссылку)
- Dropbox
- Mega.nz

### 4.3. Добавь модпак в базу данных

**Через Railway Dashboard:**
1. Перейди в свой проект на Railway
2. Data → SQLite
3. Выполни SQL:
   ```sql
   INSERT INTO ModpackVersions (Version, DownloadUrl, SHA256Hash, FileSizeBytes, Changelog, IsActive, CreatedAt)
   VALUES (
       '1.0.0',
       'https://github.com/USER/REPO/releases/download/v1.0.0/modpack-1.0.0.zip',
       'твой_sha256_хеш',
       104857600,
       'Первая версия сборки',
       1,
       datetime('now')
   );
   ```

---

## 🎮 Шаг 5: Раздача лаунчера игрокам

### 5.1. Что раздавать

Только файл: `D:\лаунчер\publish\ApocalypseLauncher.exe` (78 MB)

### 5.2. Системные требования

**Минимальные:**
- Windows 10/11 (64-bit)
- Java 17+ (для Minecraft)
- 4 GB RAM
- 2 GB свободного места

**Рекомендуемые:**
- Windows 11
- Java 21
- 8 GB RAM
- 5 GB свободного места

### 5.3. Инструкция для игроков

```
1. Скачай ApocalypseLauncher.exe
2. Запусти лаунчер
3. Зарегистрируйся (имя, email, пароль)
4. Нажми "УСТАНОВИТЬ" для загрузки Minecraft
5. Нажми "ИГРАТЬ" после установки
```

---

## 🔒 Шаг 6: Безопасность (ВАЖНО!)

### 6.1. Обнови JWT пакеты

В файле `src/ApocalypseLauncher.API/ApocalypseLauncher.API.csproj` замени:
```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.0.0" />
```

Затем:
```powershell
cd D:\лаунчер\src\ApocalypseLauncher.API
dotnet restore
```

### 6.2. Включи HTTPS Only

В `Program.cs` раскомментируй:
```csharp
app.UseHttpsRedirection();
```

### 6.3. Настрой CORS для продакшена

В `Program.cs` измени:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("LauncherPolicy", policy =>
    {
        policy.WithOrigins("https://твой-домен.railway.app")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

### 6.4. Включи Whitelist (опционально)

В `appsettings.json`:
```json
"Modpack": {
    "RequireWhitelist": true
}
```

Затем добавляй игроков в whitelist через SQL:
```sql
UPDATE Users SET IsWhitelisted = 1 WHERE Username = 'имя_игрока';
```

---

## 📊 Шаг 7: Мониторинг

### 7.1. Просмотр логов

**Railway:**
- Deployments → View Logs

### 7.2. Проверка базы данных

**Подключись к SQLite:**
```sql
-- Все пользователи
SELECT Id, Username, Email, CreatedAt, LastLoginAt FROM Users;

-- Логи безопасности
SELECT Action, COUNT(*) as Count 
FROM AuditLogs 
WHERE CreatedAt > datetime('now', '-24 hours')
GROUP BY Action;

-- Попытки взлома
SELECT * FROM AuditLogs 
WHERE Action LIKE '%SQL_INJECTION%' OR Action LIKE '%RATE_LIMITED%'
ORDER BY CreatedAt DESC;
```

---

## 🆘 Troubleshooting

### Проблема: "Ошибка подключения к серверу"
**Решение:**
1. Проверь что API запущен: `https://твой-домен.railway.app/api/health`
2. Проверь что URL в лаунчере правильный
3. Проверь логи на Railway

### Проблема: "Токен недействителен"
**Решение:**
1. Проверь что JWT_SECRET одинаковый на сервере и в коде
2. Перезапусти API сервер
3. Попробуй войти заново

### Проблема: "Слишком много попыток"
**Решение:**
1. Подожди 15 минут
2. Или перезапусти API сервер (сбросит rate limit)

---

## 💰 Примерная стоимость

| Компонент | Стоимость |
|-----------|-----------|
| Railway.app (API) | $0-5/мес |
| Домен (опционально) | $10/год |
| GitHub (хостинг модпаков) | $0 |
| **ИТОГО** | **$0-5/мес** |

---

## ✅ Чеклист перед запуском

- [ ] API развернут на Railway/Render
- [ ] Получен публичный URL
- [ ] JWT секретный ключ изменен
- [ ] URL в лаунчере обновлен на публичный
- [ ] Лаунчер пересобран
- [ ] Модпак загружен и добавлен в БД
- [ ] Тестовая регистрация работает
- [ ] Тестовый вход работает
- [ ] Скачивание модпака работает
- [ ] Запуск игры работает

---

## 📞 Поддержка

Если что-то не работает:
1. Проверь логи на Railway
2. Проверь что API отвечает на `/api/health`
3. Проверь что URL в лаунчере правильный
4. Проверь что JWT секрет правильный

---

**Создано для SRP-RP проекта | 2026**
