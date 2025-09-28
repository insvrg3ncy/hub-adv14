# 🎮 VULPS - Мультисерверный SS14 Сервер

Этот пакет содержит все необходимое для запуска мультисерверного фейкового SS14 сервера с поддержкой множественных серверов.

## 📁 Содержимое папки

- `multi-perfect-ss14-server.py` - Основной файл сервера
- `multiservers.json` - Конфигурация серверов
- `run-multi-server.sh` - Скрипт для запуска
- `status.json` - Fallback данные статуса (опционально)
- `info.json` - Fallback данные информации (опционально)
- `MULTI-SERVER-README.md` - Подробная документация

## 🚀 Быстрый запуск

```bash
# Сделать скрипт исполняемым
chmod +x run-multi-server.sh

# Запустить сервер
./run-multi-server.sh

# Или напрямую
python3 multi-perfect-ss14-server.py
```

## 🎯 Ваши серверы

1. **MakeSS14GreatAgain88 | Це пранк друзья**
2. **PINK VULP SUPREMACY** 
3. **SAY MEWO IN OUR DISCORD PWWEASE~~**

## 📡 API Эндпоинты

- `http://localhost:1218/status` - Статус текущего сервера
- `http://localhost:1218/info` - Информация о сервере
- `http://localhost:1218/servers` - Список всех серверов
- `http://localhost:1218/switch?id=server_2` - Переключиться на сервер
- `http://localhost:1218/add?name=Name&port=1234` - Добавить сервер
- `http://localhost:1218/remove?id=server_id` - Удалить сервер

## ⚙️ Настройка

Отредактируйте `multiservers.json` для изменения:
- Названий серверов
- Портов
- Файлов данных
- Порта сервера

## 🔧 Требования

- Python 3.6+
- Стандартные библиотеки Python

## 📖 Подробная документация

См. `MULTI-SERVER-README.md` для полной документации.
