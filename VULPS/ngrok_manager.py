#!/usr/bin/env python3
"""
Менеджер для работы с ngrok через прокси
"""

import json
import time
import subprocess
import requests
import threading
import os
import signal
import sys
import socks
import socket
from typing import List, Dict, Optional

class NgrokManager:
    """Менеджер для создания ngrok туннелей через прокси"""
    
    def __init__(self, proxy_host: str, proxy_port: int):
        self.proxy_host = proxy_host
        self.proxy_port = proxy_port
        self.tunnels = {}
        self.ngrok_process = None
        
        # Настраиваем переменные окружения для прокси
        proxy_url = f"socks5://{proxy_host}:{proxy_port}"
        os.environ['HTTP_PROXY'] = proxy_url
        os.environ['HTTPS_PROXY'] = proxy_url
        os.environ['ALL_PROXY'] = proxy_url
        
        print(f"🌐 Настроен прокси: {proxy_host}:{proxy_port}")
    
    def start_ngrok(self, port: int) -> bool:
        """Запускает ngrok туннель для указанного порта"""
        try:
            # Временно отключаем прокси для запуска ngrok
            old_http_proxy = os.environ.get('HTTP_PROXY')
            old_https_proxy = os.environ.get('HTTPS_PROXY')
            
            # Очищаем переменные окружения для ngrok
            if 'HTTP_PROXY' in os.environ:
                del os.environ['HTTP_PROXY']
            if 'HTTPS_PROXY' in os.environ:
                del os.environ['HTTPS_PROXY']
            
            try:
                # Запускаем ngrok БЕЗ прокси в фоне
                cmd = ['ngrok', 'tcp', str(port), '--log=stdout']
                self.ngrok_process = subprocess.Popen(
                    cmd,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    text=True
                )
                
                # Ждем запуска
                time.sleep(8)
                
                # Получаем URL туннеля
                tunnel_url = self.get_tunnel_url(port)
                if tunnel_url:
                    self.tunnels[port] = tunnel_url
                    print(f"✅ Туннель создан для порта {port}: {tunnel_url}")
                    return True
                else:
                    print(f"❌ Не удалось создать туннель для порта {port}")
                    return False
                    
            finally:
                # Восстанавливаем переменные окружения
                if old_http_proxy:
                    os.environ['HTTP_PROXY'] = old_http_proxy
                if old_https_proxy:
                    os.environ['HTTPS_PROXY'] = old_https_proxy
                
        except Exception as e:
            print(f"❌ Ошибка запуска ngrok: {e}")
            return False
    
    def get_tunnel_url(self, port: int) -> Optional[str]:
        """Получает URL туннеля для указанного порта"""
        try:
            # Ждем немного для стабилизации
            time.sleep(2)
            
            # Временно отключаем глобальный прокси для localhost
            old_http_proxy = os.environ.get('HTTP_PROXY')
            old_https_proxy = os.environ.get('HTTPS_PROXY')
            old_all_proxy = os.environ.get('ALL_PROXY')
            
            # Очищаем переменные окружения для localhost
            if 'HTTP_PROXY' in os.environ:
                del os.environ['HTTP_PROXY']
            if 'HTTPS_PROXY' in os.environ:
                del os.environ['HTTPS_PROXY']
            if 'ALL_PROXY' in os.environ:
                del os.environ['ALL_PROXY']
            
            try:
                # Запрашиваем информацию о туннелях
                response = requests.get('http://localhost:4040/api/tunnels', timeout=10)
                if response.status_code == 200:
                    data = response.json()
                    for tunnel in data.get('tunnels', []):
                        if tunnel.get('config', {}).get('addr') == f'localhost:{port}':
                            public_url = tunnel.get('public_url', '')
                            if public_url:
                                # Убираем tcp:// префикс
                                if public_url.startswith('tcp://'):
                                    public_url = public_url[6:]
                                return public_url
                return None
            finally:
                # Восстанавливаем переменные окружения
                if old_http_proxy:
                    os.environ['HTTP_PROXY'] = old_http_proxy
                if old_https_proxy:
                    os.environ['HTTPS_PROXY'] = old_https_proxy
                if old_all_proxy:
                    os.environ['ALL_PROXY'] = old_all_proxy
                    
        except Exception as e:
            print(f"❌ Ошибка получения URL туннеля для порта {port}: {e}")
            return None
    
    def stop_ngrok(self):
        """Останавливает ngrok"""
        if self.ngrok_process:
            self.ngrok_process.terminate()
            self.ngrok_process.wait()
            self.ngrok_process = None
            print("🛑 ngrok остановлен")
    
    def update_config(self, config_file: str = 'advertiser_config.json'):
        """Обновляет конфигурацию с URL'ами туннелей"""
        try:
            # Читаем конфиг
            with open(config_file, 'r', encoding='utf-8') as f:
                config = json.load(f)
            
            print(f"🔧 Обновляем конфигурацию с {len(self.tunnels)} туннелями...")
            
            # Обновляем адреса серверов
            for i, server in enumerate(config.get('servers', [])):
                port = 1212 + i
                if port in self.tunnels:
                    tunnel_url = self.tunnels[port]
                    # Убираем tcp:// если есть
                    if tunnel_url.startswith('tcp://'):
                        tunnel_url = tunnel_url[6:]
                    
                    server['address'] = f"ss14://{tunnel_url}"
                    print(f"✅ Обновлен сервер {i+1}: {server['address']}")
                else:
                    # Если туннель не создан, используем localhost
                    server['address'] = f"ss14://localhost:{port}"
                    print(f"⚠️ Сервер {i+1} остался localhost:{port} (туннель не создан)")
            
            # Сохраняем конфиг
            with open(config_file, 'w', encoding='utf-8') as f:
                json.dump(config, f, indent=2, ensure_ascii=False)
            
            print("✅ Конфигурация обновлена и сохранена")
            return True
            
        except Exception as e:
            print(f"❌ Ошибка обновления конфигурации: {e}")
            return False
    
    def create_tunnels_for_ports(self, ports: List[int]) -> bool:
        """Создает туннели для списка портов"""
        success_count = 0
        
        # Создаем только один туннель для основного порта
        main_port = ports[0] if ports else 1212
        print(f"🔧 Создаем основной туннель для порта {main_port}...")
        
        if self.start_ngrok(main_port):
            success_count += 1
            print(f"✅ Основной туннель создан для порта {main_port}")
        else:
            print(f"❌ Не удалось создать туннель для порта {main_port}")
        
        print(f"📊 Создано {success_count}/{len(ports)} туннелей")
        return success_count > 0

def load_proxy_from_file(proxy_file: str = 'socks5_proxy_list.txt') -> Optional[tuple]:
    """Загружает первый прокси из файла"""
    try:
        with open(proxy_file, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        for line in lines:
            line = line.strip()
            if line and not line.startswith('#'):
                parts = line.split(':')
                if len(parts) == 2:
                    return parts[0], int(parts[1])
        
        return None
    except Exception as e:
        print(f"❌ Ошибка загрузки прокси: {e}")
        return None

def main():
    """Основная функция"""
    print("🚀 Запуск ngrok менеджера...")
    
    # Загружаем прокси
    proxy = load_proxy_from_file()
    if not proxy:
        print("❌ Не удалось загрузить прокси из файла")
        return
    
    proxy_host, proxy_port = proxy
    print(f"🌐 Используем прокси: {proxy_host}:{proxy_port}")
    
    # Создаем менеджер
    ngrok_manager = NgrokManager(proxy_host, proxy_port)
    
    # Обработчик сигналов
    def signal_handler(signum, frame):
        print("\n🛑 Получен сигнал завершения...")
        ngrok_manager.stop_ngrok()
        sys.exit(0)
    
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    try:
        # Создаем туннели для основных портов
        ports = [1212, 1213, 1214, 1215, 1216, 1217, 1218, 1219, 1220, 1221, 1222, 1223, 1224]  # Все порты
        if ngrok_manager.create_tunnels_for_ports(ports):
            # Обновляем конфигурацию
            ngrok_manager.update_config()
            
            print("✅ Туннели созданы успешно")
            print("🎮 Теперь можно запускать VULPS сервер")
            print("⏹️  Нажмите Ctrl+C для остановки")
            
            # Ждем
            while True:
                time.sleep(1)
        else:
            print("❌ Не удалось создать туннели")
    
    except KeyboardInterrupt:
        pass
    finally:
        ngrok_manager.stop_ngrok()

if __name__ == "__main__":
    main()
