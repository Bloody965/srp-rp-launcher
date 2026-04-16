# 🚂 Настройка Railway для постоянных сессий

## Проблема
После каждого деплоя пользователи вылетают из аккаунта и должны перерегистрироваться.

## Причины
1. JWT секрет генерируется заново при каждом деплое
2. База данных SQLite хранится в эфемерном хранилище и удаляется при деплое

## ✅ Решение

### 1. Установить постоянный JWT секрет

**Шаг 1:** Сгенерируй секретный ключ (64 символа)

В PowerShell:
```powershell
-join ((65..90) + (97..122) + (48..57) | Get-Random -Count 64 | % {[char]$_})
```

Пример результата:
```
aB3dE5fG7hI9jK1lM3nO5pQ7rS9tU1vW3xY5zA7bC9dE1fG3hI5jK7lM9nO1pQ3r
```

**Шаг 2:** Добавь переменную окружения в Railway

1. Зайди в Railway Dashboard
2. Открой свой проект API
3. Перейди в **Variables**
4. Добавь новую переменную:
   - **Name:** `JWT__SECRETKEY`
   - **Value:** твой сгенерированный ключ (64 символа)
5. Нажми **Add**

**Важно:** Используй двойное подчеркивание `__` (не одинарное `_`)!

### 2. Настроить постоянное хранилище для базы данных

#### Вариант A: Добавить Volume (для SQLite)

1. В Railway Dashboard → твой проект
2. Перейди в **Settings**
3. Найди раздел **Volumes**
4. Нажми **+ New Volume**
5. Mount Path: `/app/data`
6. Нажми **Add**

#### Вариант B: Использовать PostgreSQL (рекомендуется)

1. В Railway Dashboard → твой проект
2. Нажми **+ New** → **Database** → **Add PostgreSQL**
3. Railway автоматически создаст переменную `DATABASE_URL`
4. Обнови код для использования PostgreSQL вместо SQLite

### 3. Проверка

После настройки:

1. **Задеплой** проект (Railway сделает это автоматически после изменения переменных)
2. **Зарегистрируйся** в лаунчере
3. **Закрой** лаунчер
4. **Открой** снова - ты должен остаться залогиненным
5. **Задеплой** еще раз (git push) - ты всё еще должен быть залогинен

### 4. Применить миграцию базы данных

После первого деплоя с новыми изменениями:

**Если используешь SQLite:**
1. Подключись к Railway через CLI: `railway shell`
2. Выполни:
```bash
sqlite3 /app/data/apocalypse_launcher.db "ALTER TABLE Users ADD COLUMN PlayTimeMinutes INTEGER NOT NULL DEFAULT 0;"
sqlite3 /app/data/apocalypse_launcher.db "ALTER TABLE Users ADD COLUMN LastPlayTimeUpdate TEXT;"
```

**Если используешь PostgreSQL:**
1. Зайди в Railway Dashboard → PostgreSQL → Query
2. Выполни:
```sql
ALTER TABLE "Users" ADD COLUMN "PlayTimeMinutes" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Users" ADD COLUMN "LastPlayTimeUpdate" TIMESTAMP;
```

## 🎉 Готово!

Теперь:
- ✅ JWT токены работают после деплоя
- ✅ База данных сохраняется между деплоями
- ✅ Пользователи остаются залогиненными
- ✅ Никнеймы можно менять
- ✅ Игровое время отслеживается

## 🔍 Проверка переменных

Убедись что в Railway Variables установлено:

```
JWT__SECRETKEY=твой_64_символьный_ключ
DATABASE_URL=автоматически (если PostgreSQL)
```

## ⚠️ Важно

**НЕ коммить JWT секрет в Git!** Храни его только в переменных окружения Railway.
