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
            # Запускаем ngrok
            cmd = ['ngrok', 'tcp', str(port), '--log=stdout']
            self.ngrok_process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True
            )
            
            # Ждем запуска
            time.sleep(5)
            
            # Получаем URL туннеля
            tunnel_url = self.get_tunnel_url(port)
            if tunnel_url:
                self.tunnels[port] = tunnel_url
                print(f"✅ Туннель создан для порта {port}: {tunnel_url}")
                return True
            else:
                print(f"❌ Не удалось создать туннель для порта {port}")
                return False
                
        except Exception as e:
            print(f"❌ Ошибка запуска ngrok: {e}")
            return False
    
    def get_tunnel_url(self, port: int) -> Optional[str]:
        """Получает URL туннеля для указанного порта"""
        try:
            # Запрашиваем информацию о туннелях
            response = requests.get('http://localhost:4040/api/tunnels', timeout=10)
            if response.status_code == 200:
                data = response.json()
                for tunnel in data.get('tunnels', []):
                    if tunnel.get('config', {}).get('addr') == f'localhost:{port}':
                        return tunnel.get('public_url', '').replace('tcp://', '')
            return None
        except Exception as e:
            print(f"❌ Ошибка получения URL туннеля: {e}")
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
            
            # Обновляем адреса серверов
            for i, server in enumerate(config.get('servers', [])):
                port = 1212 + i
                if port in self.tunnels:
                    host, tunnel_port = self.tunnels[port].split(':')
                    server['address'] = f"ss14://{host}:{tunnel_port}"
                    print(f"✅ Обновлен сервер {i+1}: {server['address']}")
                else:
                    # Оставляем localhost для неактивных туннелей
                    server['address'] = f"ss14://localhost:{port}"
            
            # Сохраняем конфиг
            with open(config_file, 'w', encoding='utf-8') as f:
                json.dump(config, f, indent=2, ensure_ascii=False)
            
            print("✅ Конфигурация обновлена")
            return True
            
        except Exception as e:
            print(f"❌ Ошибка обновления конфигурации: {e}")
            return False
    
    def create_tunnels_for_ports(self, ports: List[int]) -> bool:
        """Создает туннели для списка портов"""
        success_count = 0
        
        for port in ports:
            if self.start_ngrok(port):
                success_count += 1
                time.sleep(3)  # Задержка между туннелями
            else:
                print(f"⚠️ Пропускаем порт {port}")
        
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
