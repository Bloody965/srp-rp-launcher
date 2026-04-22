# SRP-RP: сетевые скины (Forge 1.20.1)

Цель: чтобы **в мультиплеере** у всех игроков корректно отображались **скины/плащи**, сохранённые в вашем API.

Схема такая:
- **API** выступает как кастомный Yggdrasil/sessionserver: `<BASE_URL>/api/yggdrasil`
- **все клиенты** и **сервер** должны использовать **один и тот же** sessionserver через `authlib-injector`

## 1) Что должно быть опубликовано в API

- Yggdrasil metadata: `GET <BASE_URL>/api/yggdrasil`
- Sessionserver profile: `GET <BASE_URL>/api/yggdrasil/sessionserver/session/minecraft/profile/<uuid>`
- Handshake endpoints (для мультиплеера):
  - `POST <BASE_URL>/api/yggdrasil/sessionserver/session/minecraft/join`
  - `GET <BASE_URL>/api/yggdrasil/sessionserver/session/minecraft/hasJoined?username=...&serverId=...`
- Отдача картинок:
  - `GET <BASE_URL>/api/skins/download/<userId>` (PNG)
  - `GET <BASE_URL>/api/skins/capes/download/<userId>` (PNG)

## 2) Сервер: подключение authlib-injector

### Шаг 1: скачать authlib-injector

Скачайте JAR (пример версии):
- `authlib-injector-1.2.5.jar` из релизов проекта authlib-injector

Положите файл рядом со стартовым скриптом сервера, например:
- `server/authlib-injector.jar`

### Шаг 2: добавить `-javaagent` в запуск сервера

В `run.bat` / `run.sh` добавьте JVM аргумент **до** `-jar`:

```bat
java ^
  -javaagent:authlib-injector.jar=<BASE_URL>/api/yggdrasil ^
  -Xmx6G -Xms2G ^
  -jar forge-1.20.1-47.3.0.jar nogui
```

Где `<BASE_URL>` — ваш домен API, например:
- `https://srp-rp-launcher-production.up.railway.app`

## 3) Клиенты: authlib-injector должен быть включён у всех

Лаунчер уже добавляет аргумент вида:
- `-javaagent:<gameDir>/authlib-injector.jar=<BASE_URL>/api/yggdrasil`

Важно: **все игроки** должны запускать игру именно через ваш лаунчер (или вручную с тем же javaagent).

## 4) Быстрая проверка, что всё работает

1) Запустите сервер с `-javaagent:...=<BASE_URL>/api/yggdrasil`
2) Запустите 2 клиента через лаунчер
3) У одного игрока загрузите скин/плащ
4) Зайдите на сервер обоими игроками:
   - проверьте отображение скинов в мире и в TAB

Если скины не отображаются:
- проверьте, что `<BASE_URL>` одинаковый на сервере и клиентах
- проверьте, что URL картинок (`/api/skins/download/*`) открывается извне по HTTPS
