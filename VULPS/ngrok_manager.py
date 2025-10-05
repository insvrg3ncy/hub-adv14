#!/usr/bin/env python3

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
    
    def __init__(self, proxy_host: str, proxy_port: int):
        self.proxy_host = proxy_host
        self.proxy_port = proxy_port
        self.tunnels = {}
        self.ngrok_process = None
    
    def start_ngrok(self, port: int) -> bool:
        try:
            old_http_proxy = os.environ.get('HTTP_PROXY')
            old_https_proxy = os.environ.get('HTTPS_PROXY')
            
            if 'HTTP_PROXY' in os.environ:
                del os.environ['HTTP_PROXY']
            if 'HTTPS_PROXY' in os.environ:
                del os.environ['HTTPS_PROXY']
            
            try:
                cmd = ['ngrok', 'tcp', str(port), '--log=stdout']
                self.ngrok_process = subprocess.Popen(
                    cmd,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    text=True
                )
                
                time.sleep(8)
                
                tunnel_url = self.get_tunnel_url(port)
                if tunnel_url:
                    self.tunnels[port] = tunnel_url
                    print(f"✅ Туннель создан для порта {port}: {tunnel_url}")
                    return True
                else:
                    print(f"❌ Не удалось создать туннель для порта {port}")
                    return False
                    
            finally:
                if old_http_proxy:
                    os.environ['HTTP_PROXY'] = old_http_proxy
                if old_https_proxy:
                    os.environ['HTTPS_PROXY'] = old_https_proxy
                
        except Exception as e:
            print(f"❌ Ошибка запуска ngrok: {e}")
            return False
    
    def get_tunnel_url(self, port: int) -> Optional[str]:
        try:
            time.sleep(2)
            
            old_http_proxy = os.environ.get('HTTP_PROXY')
            old_https_proxy = os.environ.get('HTTPS_PROXY')
            
            if 'HTTP_PROXY' in os.environ:
                del os.environ['HTTP_PROXY']
            if 'HTTPS_PROXY' in os.environ:
                del os.environ['HTTPS_PROXY']
            
            try:
                session = requests.Session()
                response = session.get('http://localhost:4040/api/tunnels', timeout=10)
                
                if response.status_code == 200:
                    data = response.json()
                    for tunnel in data.get('tunnels', []):
                        if tunnel.get('config', {}).get('addr') == f'localhost:{port}':
                            public_url = tunnel.get('public_url', '')
                            if public_url:
                                if public_url.startswith('tcp://'):
                                    public_url = public_url[6:]
                                return public_url
                return None
                
            finally:
                if old_http_proxy:
                    os.environ['HTTP_PROXY'] = old_http_proxy
                if old_https_proxy:
                    os.environ['HTTPS_PROXY'] = old_https_proxy
                    
        except Exception as e:
            print(f"❌ Ошибка получения URL туннеля для порта {port}: {e}")
            return None
    
    def stop_ngrok(self):
        if self.ngrok_process:
            self.ngrok_process.terminate()
            self.ngrok_process.wait()
            self.ngrok_process = None
            print("🛑 ngrok остановлен")
    
    def update_config(self, config_file: str = 'advertiser_config.json'):
        try:
            with open(config_file, 'r', encoding='utf-8') as f:
                config = json.load(f)
            
            print(f"🔧 Обновляем конфигурацию с {len(self.tunnels)} туннелями...")
            
            for i, server in enumerate(config.get('servers', [])):
                port = 1212 + i
                if port in self.tunnels:
                    tunnel_url = self.tunnels[port]
                    if tunnel_url.startswith('tcp://'):
                        tunnel_url = tunnel_url[6:]
                    
                    server['address'] = f"ss14://{tunnel_url}"
                    print(f"✅ Обновлен сервер {i+1}: {server['address']}")
                else:
                    server['address'] = f"ss14://localhost:{port}"
                    print(f"⚠️ Сервер {i+1} остался localhost:{port} (туннель не создан)")
            
            with open(config_file, 'w', encoding='utf-8') as f:
                json.dump(config, f, indent=2, ensure_ascii=False)
            
            print("✅ Конфигурация обновлена и сохранена")
            return True
            
        except Exception as e:
            print(f"❌ Ошибка обновления конфигурации: {e}")
            return False
    
    def create_tunnels_for_ports(self, ports: List[int]) -> bool:
        success_count = 0
        
        print("🚀 Запускаем ngrok daemon...")
        if not self._start_ngrok_daemon():
            print("❌ Не удалось запустить ngrok daemon")
            return False
        
        time.sleep(5)
        
        for port in ports:
            print(f"🔧 Создаем туннель для порта {port}...")
            if self._create_tunnel_via_api(port):
                success_count += 1
                print(f"✅ Туннель создан для порта {port}")
                time.sleep(1)
            else:
                print(f"❌ Не удалось создать туннель для порта {port}")
        
        print(f"📊 Создано {success_count}/{len(ports)} туннелей")
        return success_count > 0
    
    def _start_ngrok_daemon(self) -> bool:
        try:
            old_http_proxy = os.environ.pop('HTTP_PROXY', None)
            old_https_proxy = os.environ.pop('HTTPS_PROXY', None)
            
            try:
                cmd = ['ngrok', 'start', '--none', '--log=stdout']
                self.ngrok_process = subprocess.Popen(
                    cmd,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    text=True
                )
                print("🚀 ngrok daemon запущен")
                return True
                
            finally:
                if old_http_proxy:
                    os.environ['HTTP_PROXY'] = old_http_proxy
                if old_https_proxy:
                    os.environ['HTTPS_PROXY'] = old_https_proxy
                    
        except Exception as e:
            print(f"❌ Ошибка запуска ngrok daemon: {e}")
            return False
    
    def _create_tunnel_via_api(self, port: int) -> bool:
        try:
            import requests
            
            old_http_proxy = os.environ.get('HTTP_PROXY')
            old_https_proxy = os.environ.get('HTTPS_PROXY')
            old_all_proxy = os.environ.get('ALL_PROXY')
            
            if 'HTTP_PROXY' in os.environ:
                del os.environ['HTTP_PROXY']
            if 'HTTPS_PROXY' in os.environ:
                del os.environ['HTTPS_PROXY']
            if 'ALL_PROXY' in os.environ:
                del os.environ['ALL_PROXY']
            
            try:
                session = requests.Session()
                
                tunnel_data = {
                    "name": f"tcp-{port}",
                    "proto": "tcp",
                    "addr": str(port)
                }
                
                response = session.post(
                    "http://localhost:4040/api/tunnels",
                    json=tunnel_data,
                    timeout=10
                )
                
                if response.status_code == 201:
                    tunnel_info = response.json()
                    public_url = tunnel_info.get('public_url', '')
                    if public_url:
                        if public_url.startswith('tcp://'):
                            public_url = public_url[6:]
                        self.tunnels[port] = public_url
                        return True
                
                return False
                
            finally:
                if old_http_proxy:
                    os.environ['HTTP_PROXY'] = old_http_proxy
                if old_https_proxy:
                    os.environ['HTTPS_PROXY'] = old_https_proxy
                if old_all_proxy:
                    os.environ['ALL_PROXY'] = old_all_proxy
            
        except Exception as e:
            print(f"❌ Ошибка создания туннеля через API: {e}")
            return False

def load_proxy_from_file(proxy_file: str = 'socks5_proxy_list.txt') -> Optional[tuple]:
    try:
        with open(proxy_file, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        for line in lines:
            line = line.strip()
            if line and not line.startswith('#'):
                if ':' in line:
                    try:
                        host, port = line.split(':', 1)
                        return (host.strip(), int(port.strip()))
                    except ValueError:
                        continue
        return None
    except Exception as e:
        print(f"❌ Ошибка загрузки прокси: {e}")
        return None

def signal_handler(signum, frame):
    print("\n🛑 Получен сигнал завершения...")
    
    if ngrok_manager:
        ngrok_manager.stop_ngrok()
    
    print("✅ Завершение работы...")
    sys.exit(0)

if __name__ == "__main__":
    proxy = load_proxy_from_file()
    
    if proxy:
        proxy_host, proxy_port = proxy
        print(f"🌐 Используем прокси: {proxy_host}:{proxy_port}")
        
        ngrok_manager = NgrokManager(proxy_host, proxy_port)
        print(f"🌐 Настроен прокси: {proxy_host}:{proxy_port}")
    else:
        print("⚠️ Прокси не найден, запускаем без прокси")
        ngrok_manager = NgrokManager("", 0)
    
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    try:
        ports = [1212, 1213, 1214, 1215, 1216, 1217, 1218, 1219, 1220, 1221, 1222, 1223, 1224]
        if ngrok_manager.create_tunnels_for_ports(ports):
            ngrok_manager.update_config()
            
            print("✅ Туннели созданы успешно")
            print(f"✅ ngrok менеджер запущен (PID: {ngrok_manager.ngrok_process.pid})")
            
            try:
                while True:
                    time.sleep(1)
            except KeyboardInterrupt:
                print("\n🛑 Получен сигнал завершения...")
                ngrok_manager.stop_ngrok()
                print("✅ Завершение работы...")
        else:
            print("❌ Не удалось создать туннели")
            sys.exit(1)
            
    except Exception as e:
        print(f"❌ Ошибка: {e}")
        sys.exit(1)