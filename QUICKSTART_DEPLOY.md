# ⚡ Быстрый старт для публичного запуска

## 🎯 Что нужно сделать (5 шагов)

### 1️⃣ Разверни API на Railway.app

1. Зарегистрируйся на https://railway.app
2. Создай новый проект → Deploy from GitHub
3. Подключи свой репозиторий
4. Получи публичный URL (например: `https://srp-launcher.up.railway.app`)

### 2️⃣ Измени URL в лаунчере

Файл: `src/ApocalypseLauncher/ViewModels/MainWindowViewModel.cs` (строка 32)

**Было:**
```csharp
_apiService = new ApiService("http://localhost:5000");
```

**Стало:**
```csharp
_apiService = new ApiService("https://srp-launcher.up.railway.app");
```

### 3️⃣ Пересобери лаунчер

```powershell
cd D:\лаунчер\src\ApocalypseLauncher
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o D:\лаунчер\publish
```

### 4️⃣ Добавь модпак

1. Загрузи ZIP с модами на GitHub Releases
2. Получи SHA256 хеш: `Get-FileHash modpack.zip -Algorithm SHA256`
3. Добавь в базу через Railway Dashboard:

```sql
INSERT INTO ModpackVersions (Version, DownloadUrl, SHA256Hash, FileSizeBytes, Changelog, IsActive, CreatedAt)
VALUES ('1.0.0', 'https://github.com/USER/REPO/releases/download/v1.0.0/modpack.zip', 'SHA256_HASH', 104857600, 'Первая версия', 1, datetime('now'));
```

### 5️⃣ Раздай лаунчер

Раздай файл: `D:\лаунчер\publish\ApocalypseLauncher.exe`

---

## 📋 Системные требования для игроков

- Windows 10/11 (64-bit)
- Java 17+
- 4 GB RAM
- 2 GB свободного места

---

## 🔍 Проверка что всё работает

1. Открой в браузере: `https://твой-домен.railway.app/api/health`
   - Должно вернуть: `{"status":"healthy",...}`

2. Запусти лаунчер и зарегистрируйся
   - Если работает - всё готово! 🎉

---

## 💰 Стоимость

- **Railway.app:** $0-5/месяц
- **GitHub (модпаки):** $0
- **Итого:** $0-5/месяц

---

## ⚠️ ВАЖНО: Безопасность

Перед запуском измени JWT секрет в `appsettings.json`:

```powershell
# Сгенерируй случайную строку
-join ((65..90) + (97..122) + (48..57) | Get-Random -Count 64 | % {[char]$_})
```

Вставь результат в `Jwt.SecretKey`

---

📖 **Полная инструкция:** см. `DEPLOYMENT.md`
