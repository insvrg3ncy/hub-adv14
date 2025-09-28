# 🔧 Устранение неполадок

## ❌ Ошибки таймаута при рекламе

Если вы видите ошибки типа:
```
[ERROR] ✗ Исключение при рекламе: The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.
```

### ✅ Решение:

1. **Убедитесь, что мультисерверный сервер запущен**:
   ```bash
   # Запустите мультисерверный сервер
   python3 multi-perfect-ss14-server.py
   ```

2. **Проверьте, что сервер отвечает**:
   ```bash
   curl http://localhost:1218/status
   ```

3. **Обновите конфигурацию MultiServerAdvertiser**:
   - Используйте файл `multisettings.json` из папки VULPS
   - Скопируйте его в папку `../MultiServerAdvertiser/`
   - Или используйте скрипт `run-with-advertiser.sh`

4. **Проверьте настройки в multisettings.json**:
   ```json
   {
     "RequestTimeoutSeconds": 30,
     "CheckServerAvailability": false,
     "Servers": [
       {
         "Address": "ss14://localhost:1218",
         "DisplayName": "https://discord.gg/HSC6Frb6ma | MakeSS14GreatAgain88 | Це пранк друзья"
       }
     ]
   }
   ```

## 🚀 Автоматический запуск

Используйте скрипт `run-with-advertiser.sh` для автоматического запуска сервера и рекламы:

```bash
chmod +x run-with-advertiser.sh
./run-with-advertiser.sh
```

## 🔍 Проверка работы

1. **Проверьте статус сервера**:
   ```bash
   curl http://localhost:1218/servers
   ```

2. **Проверьте переключение серверов**:
   ```bash
   curl "http://localhost:1218/switch?id=server_2"
   curl http://localhost:1218/status
   ```

3. **Проверьте логи рекламы**:
   - Реклама должна показывать успешные регистрации
   - Не должно быть ошибок таймаута

## ⚙️ Настройка прокси

Если используете прокси, убедитесь, что он настроен правильно в `multisettings.json`:

```json
{
  "ProxyUrl": "128.199.202.122:8080",
  "ProxyUsername": "",
  "ProxyPassword": ""
}
```

## 📞 Поддержка

Если проблемы продолжаются:
1. Проверьте, что порт 1218 свободен
2. Убедитесь, что Python 3.6+ установлен
3. Проверьте права доступа к файлам
4. Убедитесь, что .NET установлен для рекламы

