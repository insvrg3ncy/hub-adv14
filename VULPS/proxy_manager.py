#!/usr/bin/env python3
"""
Менеджер прокси для настройки глобального прокси для всего Python приложения
Обеспечивает прохождение всего трафика через SOCKS5 прокси
"""

import os
import socket
import socks
import requests
import urllib3
from typing import Optional, List
import random
import time

# Отключаем предупреждения SSL
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

class ProxyManager:
    """Менеджер для настройки глобального прокси"""
    
    def __init__(self, proxy_list: List[str] = None, proxy_file: str = None, username: str = "", password: str = ""):
        self.proxy_list = proxy_list or []
        self.proxy_file = proxy_file
        self.username = username
        self.password = password
        self.current_proxy = None
        self.original_socket = None
        self.is_proxy_enabled = False
        
        # Загружаем прокси из файла если указан
        if proxy_file and not proxy_list:
            self.load_proxies_from_file()
        
        # User-Agent список
        self.user_agents = [
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:109.0) Gecko/20100101 Firefox/121.0"
        ]
    
    def load_proxies_from_file(self):
        """Загружает список прокси из файла"""
        if not self.proxy_file:
            return
        
        try:
            with open(self.proxy_file, 'r', encoding='utf-8') as f:
                lines = f.readlines()
            
            self.proxy_list = []
            for line in lines:
                line = line.strip()
                if line and not line.startswith('#'):
                    self.proxy_list.append(line)
            
            print(f"📁 Загружено {len(self.proxy_list)} прокси из файла {self.proxy_file}")
            
        except Exception as e:
            print(f"❌ Ошибка загрузки прокси из файла {self.proxy_file}: {e}")
            self.proxy_list = []
    
    def test_proxy(self, proxy_url: str) -> bool:
        """Тестирует прокси на работоспособность"""
        try:
            parts = proxy_url.split(':')
            if len(parts) != 2:
                return False
            
            proxy_host = parts[0]
            proxy_port = int(parts[1])
            
            # Создаем временный сокет с прокси
            test_socket = socks.socksocket()
            test_socket.set_proxy(socks.SOCKS5, proxy_host, proxy_port, 
                                username=self.username if self.username else None,
                                password=self.password if self.password else None)
            test_socket.settimeout(10)
            
            # Пытаемся подключиться к хабу SS14
            test_socket.connect(('hub.spacestation14.com', 443))
            test_socket.close()
            
            print(f"✓ Прокси работает: {proxy_url}")
            return True
            
        except Exception as e:
            print(f"✗ Прокси не работает: {proxy_url} - {e}")
            return False
    
    def find_working_proxy(self) -> Optional[str]:
        """Находит первый рабочий прокси"""
        print("🔍 Ищем рабочий прокси...")
        
        for proxy in self.proxy_list:
            if self.test_proxy(proxy):
                return proxy
        
        print("❌ Рабочие прокси не найдены")
        return None
    
    def enable_global_proxy(self, proxy_url: str) -> bool:
        """Включает глобальный прокси для всего приложения"""
        try:
            parts = proxy_url.split(':')
            if len(parts) != 2:
                raise ValueError(f"Неверный формат прокси: {proxy_url}")
            
            proxy_host = parts[0]
            proxy_port = int(parts[1])
            
            # Сохраняем оригинальный сокет
            self.original_socket = socket.socket
            
            # Устанавливаем глобальный прокси
            socks.set_default_proxy(socks.SOCKS5, proxy_host, proxy_port,
                                  username=self.username if self.username else None,
                                  password=self.password if self.password else None)
            socket.socket = socks.socksocket
            
            # Настраиваем переменные окружения для requests
            os.environ['HTTP_PROXY'] = f'socks5://{proxy_host}:{proxy_port}'
            os.environ['HTTPS_PROXY'] = f'socks5://{proxy_host}:{proxy_port}'
            
            if self.username and self.password:
                os.environ['HTTP_PROXY'] = f'socks5://{self.username}:{self.password}@{proxy_host}:{proxy_port}'
                os.environ['HTTPS_PROXY'] = f'socks5://{self.username}:{self.password}@{proxy_host}:{proxy_port}'
            
            self.current_proxy = proxy_url
            self.is_proxy_enabled = True
            
            print(f"✅ Глобальный прокси включен: {proxy_url}")
            return True
            
        except Exception as e:
            print(f"❌ Ошибка включения прокси: {e}")
            return False
    
    def disable_global_proxy(self):
        """Отключает глобальный прокси"""
        if self.is_proxy_enabled and self.original_socket:
            socket.socket = self.original_socket
            self.original_socket = None
        
        # Очищаем переменные окружения
        if 'HTTP_PROXY' in os.environ:
            del os.environ['HTTP_PROXY']
        if 'HTTPS_PROXY' in os.environ:
            del os.environ['HTTPS_PROXY']
        
        self.current_proxy = None
        self.is_proxy_enabled = False
        print("🔄 Глобальный прокси отключен")
    
    def switch_proxy(self) -> bool:
        """Переключается на другой прокси"""
        if not self.proxy_list:
            return False
        
        # Находим следующий прокси
        current_index = 0
        if self.current_proxy and self.current_proxy in self.proxy_list:
            current_index = self.proxy_list.index(self.current_proxy)
        
        # Пробуем следующие прокси по кругу
        for i in range(len(self.proxy_list)):
            next_index = (current_index + i + 1) % len(self.proxy_list)
            next_proxy = self.proxy_list[next_index]
            
            if self.test_proxy(next_proxy):
                self.disable_global_proxy()
                return self.enable_global_proxy(next_proxy)
        
        print("❌ Не удалось найти рабочий прокси для переключения")
        return False
    
    def create_proxied_session(self) -> requests.Session:
        """Создает requests.Session с настройками прокси"""
        session = requests.Session()
        
        # Настраиваем прокси для сессии
        if self.current_proxy:
            parts = self.current_proxy.split(':')
            proxy_host = parts[0]
            proxy_port = int(parts[1])
            
            proxy_url = f'socks5://{proxy_host}:{proxy_port}'
            if self.username and self.password:
                proxy_url = f'socks5://{self.username}:{self.password}@{proxy_host}:{proxy_port}'
            
            session.proxies = {
                'http': proxy_url,
                'https': proxy_url
            }
        
        # Случайный User-Agent
        session.headers.update({
            'User-Agent': random.choice(self.user_agents),
            'Accept': 'application/json, text/plain, */*',
            'Accept-Language': 'en-US,en;q=0.9,ru;q=0.8',
            'Accept-Encoding': 'gzip, deflate, br',
            'Connection': 'keep-alive',
            'Sec-Fetch-Dest': 'empty',
            'Sec-Fetch-Mode': 'cors',
            'Sec-Fetch-Site': 'cross-site'
        })
        
        return session
    
    def test_connection(self) -> bool:
        """Тестирует подключение через текущий прокси"""
        try:
            session = self.create_proxied_session()
            response = session.get('https://hub.spacestation14.com/api/servers/', 
                                 timeout=10, verify=False)
            session.close()
            
            if response.status_code == 200:
                print("✅ Подключение через прокси работает")
                return True
            else:
                print(f"❌ Ошибка подключения: {response.status_code}")
                return False
                
        except Exception as e:
            print(f"❌ Ошибка тестирования подключения: {e}")
            return False
    
    def get_status(self) -> dict:
        """Возвращает статус прокси"""
        return {
            'is_enabled': self.is_proxy_enabled,
            'current_proxy': self.current_proxy,
            'total_proxies': len(self.proxy_list),
            'connection_works': self.test_connection() if self.is_proxy_enabled else False
        }
    
    def start_with_proxy(self) -> bool:
        """Запускает прокси с автоматическим поиском рабочего"""
        working_proxy = self.find_working_proxy()
        if working_proxy:
            return self.enable_global_proxy(working_proxy)
        return False

# Глобальный экземпляр менеджера прокси
proxy_manager = None

def init_proxy_manager(proxy_list: List[str] = None, proxy_file: str = None, username: str = "", password: str = ""):
    """Инициализирует глобальный менеджер прокси"""
    global proxy_manager
    proxy_manager = ProxyManager(proxy_list, proxy_file, username, password)
    return proxy_manager

def get_proxy_manager() -> Optional[ProxyManager]:
    """Возвращает глобальный менеджер прокси"""
    return proxy_manager
