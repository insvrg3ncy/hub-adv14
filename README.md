# SS14 Multi-Server Advertiser

Система для рекламы множественных SS14 серверов с обходом блокировки IP через localtunnel.

## 🚀 Быстрый запуск

```bash
./run_system.sh
```

## 📋 Компоненты

### VULPS (Виртуальный SS14 сервер)
- **Файл**: `VULPS/multi-perfect-ss14-server.py`
- **Порты**: 1212-1224 (13 серверов)
- **Функции**: Эмулирует SS14 серверы с API endpoints

### MultiServerAdvertiser (Рекламщик)
- **Файл**: `MultiServerAdvertiser/`
- **Функции**: Рекламирует серверы в SS14 хаб через SOCKS5 прокси
- **Конфигурация**: `multisettings.json`

## ⚙️ Настройка

### 1. VULPS сервер
```bash
cd VULPS
python3 multi-perfect-ss14-server.py
```

### 2. Localtunnel туннели
```bash
# Установка localtunnel
npm install localtunnel

# Запуск туннелей для всех портов
for port in {1212..1224}; do
    npx localtunnel --port $port --subdomain "ss14-$port" &
done
```

### 3. Advertiser
```bash
cd MultiServerAdvertiser
dotnet run
```

## 🔧 Конфигурация

### multisettings.json
- **HubUrl**: URL SS14 хаба
- **ServerCount**: Количество серверов (13)
- **Socks5ProxyList**: Список SOCKS5 прокси
- **Servers**: Конфигурация серверов с localtunnel URL

### multiservers.json
- Конфигурация VULPS сервера
- Настройки портов и названий серверов

## 🌐 Туннели

Серверы доступны по адресам:
- `https://ss14-1212.loca.lt` → порт 1212
- `https://ss14-1213.loca.lt` → порт 1213
- ...
- `https://ss14-1224.loca.lt` → порт 1224

## 📊 Мониторинг

- **VULPS**: Логи в консоли
- **Advertiser**: Логи рекламы в консоли
- **Localtunnel**: Автоматические URL

## 🛠️ Требования

- Python 3.x
- .NET 9.0
- Node.js (для localtunnel)
- SOCKS5 прокси

## 📝 Примечания

- Система использует localtunnel для обхода блокировки IP
- Все серверы имеют одинаковое название: "t.me/VT_SS14 | PJB go smd, hub will cry soon"
- Advertiser автоматически тестирует и удаляет нерабочие прокси