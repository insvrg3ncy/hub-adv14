#!/usr/bin/env python3
"""
Тестовый скрипт для проверки работы ngrok менеджера
"""

import json
import time
from ngrok_manager import NgrokManager, load_proxy_from_file

def test_ngrok():
    print("🧪 Тестируем ngrok менеджер...")
    
    # Загружаем прокси
    proxy = load_proxy_from_file()
    if not proxy:
        print("❌ Не удалось загрузить прокси")
        return False
    
    proxy_host, proxy_port = proxy
    print(f"🌐 Используем прокси: {proxy_host}:{proxy_port}")
    
    # Создаем менеджер
    ngrok_manager = NgrokManager(proxy_host, proxy_port)
    
    # Тестируем создание одного туннеля
    print("🔧 Создаем тестовый туннель для порта 1212...")
    if ngrok_manager.start_ngrok(1212):
        print("✅ Туннель создан успешно")
        
        # Проверяем URL
        tunnel_url = ngrok_manager.get_tunnel_url(1212)
        if tunnel_url:
            print(f"🔗 URL туннеля: {tunnel_url}")
            
            # Обновляем конфиг
            if ngrok_manager.update_config():
                print("✅ Конфигурация обновлена")
                
                # Проверяем, что в конфиге появился правильный URL
                with open('advertiser_config.json', 'r') as f:
                    config = json.load(f)
                
                first_server = config['servers'][0]
                print(f"📝 Первый сервер в конфиге: {first_server['address']}")
                
                if 'localhost' not in first_server['address']:
                    print("✅ Конфигурация обновлена правильно!")
                    return True
                else:
                    print("❌ В конфиге всё ещё localhost!")
                    return False
            else:
                print("❌ Не удалось обновить конфигурацию")
                return False
        else:
            print("❌ Не удалось получить URL туннеля")
            return False
    else:
        print("❌ Не удалось создать туннель")
        return False
    
    # Останавливаем ngrok
    ngrok_manager.stop_ngrok()

if __name__ == "__main__":
    test_ngrok()
