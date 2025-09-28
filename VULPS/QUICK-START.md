# 🚀 VULPS - Быстрый старт

## Установка и запуск

1. **Скопируйте папку VULPS** на ваш сервер
2. **Перейдите в папку VULPS**:
   ```bash
   cd VULPS
   ```

3. **Сделайте скрипт исполняемым**:
   ```bash
   chmod +x run-multi-server.sh
   ```

4. **Запустите сервер**:
   ```bash
   ./run-multi-server.sh
   ```

## 🎯 Ваши серверы

- **MakeSS14GreatAgain88 | Це пранк друзья**
- **PINK VULP SUPREMACY** 
- **SAY MEWO IN OUR DISCORD PWWEASE~~**

## 📡 Тестирование

```bash
# Проверить список серверов
curl http://localhost:1218/servers

# Переключиться на PINK VULP SUPREMACY
curl "http://localhost:1218/switch?id=server_2"

# Проверить статус
curl http://localhost:1218/status
```

## ⚙️ Настройка

Отредактируйте `multiservers.json` для изменения:
- Названий серверов
- Портов
- Порта сервера (по умолчанию 1218)

## 🔧 Остановка

Нажмите `Ctrl+C` в терминале где запущен сервер.

## 📖 Полная документация

См. `MULTI-SERVER-README.md` для подробной документации.

