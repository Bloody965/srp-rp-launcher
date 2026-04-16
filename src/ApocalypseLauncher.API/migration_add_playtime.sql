-- Добавление полей для игрового времени
ALTER TABLE Users ADD COLUMN PlayTimeMinutes INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Users ADD COLUMN LastPlayTimeUpdate TEXT;
