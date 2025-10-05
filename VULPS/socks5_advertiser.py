#!/usr/bin/env python3
"""
Модуль для рекламы серверов в хабе SS14 через SOCKS5 прокси
Интегрирован с VULPS для обхода блокировок IP
"""

import json
import time
import random
import threading
import requests
import socks
import socket
from typing import List, Dict, Optional
from datetime import datetime
import urllib3
from urllib3.exceptions import InsecureRequestWarning
from proxy_manager import get_proxy_manager

# Отключаем предупреждения SSL
urllib3.disable_warnings(InsecureRequestWarning)

class Socks5Advertiser:
    """Класс для рекламы серверов через SOCKS5 прокси"""
    
    def __init__(self, config_file: str = 'advertiser_config.json'):
        self.config = self.load_config(config_file)
        self.working_proxies = []
        self.current_proxy_index = 0
        self.proxy_error_count = {}
        self.advertisement_timer = None
        self.is_running = False
        self.lock = threading.Lock()
        
        # User-Agent список для обхода блокировки
        self.user_agents = [
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:109.0) Gecko/20100101 Firefox/121.0"
        ]
        
        print(f"🎯 Socks5Advertiser инициализирован")
        print(f"📡 Хаб: {self.config['hub_url']}")
        print(f"⏰ Интервал рекламы: {self.config['advertisement_interval_minutes']} мин")
        print(f"🔗 Серверов для рекламы: {len(self.config['servers'])}")
        print(f"🌐 Прокси в списке: {len(self.config['socks5_proxy_list'])}")
    
    def load_config(self, config_file: str) -> dict:
        """Загружает конфигурацию из файла"""
        try:
            with open(config_file, 'r', encoding='utf-8') as f:
                return json.load(f)
        except Exception as e:
            print(f"❌ Ошибка загрузки конфигурации: {e}")
            return self.get_default_config()
    
    def get_default_config(self) -> dict:
        """Возвращает конфигурацию по умолчанию"""
        return {
            "hub_url": "https://hub.spacestation14.com",
            "advertisement_interval_minutes": 2,
            "request_timeout_seconds": 30,
            "max_retries": 3,
            "retry_delay_ms": 3000,
            "auto_test_proxies": True,
            "proxy_test_timeout_seconds": 10,
            "socks5_proxy_list": [],
            "proxy_username": "",
            "proxy_password": "",
            "servers": []
        }
    
    def create_session_with_proxy(self, proxy_url: str = None) -> requests.Session:
        """Создает requests.Session с SOCKS5 прокси"""
        # Используем глобальный прокси менеджер
        proxy_manager = get_proxy_manager()
        if proxy_manager:
            return proxy_manager.create_proxied_session()
        
        # Fallback к старому методу если глобальный прокси не настроен
        session = requests.Session()
        
        if proxy_url:
            # Парсим адрес прокси
            parts = proxy_url.split(':')
            if len(parts) != 2:
                raise ValueError(f"Неверный формат прокси: {proxy_url}")
            
            proxy_host = parts[0]
            proxy_port = int(parts[1])
            
            # Настраиваем SOCKS5 прокси
            session.proxies = {
                'http': f'socks5://{proxy_host}:{proxy_port}',
                'https': f'socks5://{proxy_host}:{proxy_port}'
            }
            
            # Настройка аутентификации если есть
            if self.config.get('proxy_username') and self.config.get('proxy_password'):
                session.proxies = {
                    'http': f'socks5://{self.config["proxy_username"]}:{self.config["proxy_password"]}@{proxy_host}:{proxy_port}',
                    'https': f'socks5://{self.config["proxy_username"]}:{self.config["proxy_password"]}@{proxy_host}:{proxy_port}'
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
        
        # Таймаут
        session.timeout = self.config['request_timeout_seconds']
        
        return session
    
    def test_proxy(self, proxy_url: str) -> bool:
        """Тестирует прокси на возможность подключения к хабу"""
        try:
            session = self.create_session_with_proxy(proxy_url)
            
            # Простой GET запрос для проверки
            response = session.get(
                f"{self.config['hub_url']}/api/servers/",
                timeout=self.config['proxy_test_timeout_seconds'],
                verify=False
            )
            
            if response.status_code == 200:
                print(f"✓ Прокси работает: {proxy_url}")
                return True
            else:
                print(f"✗ Прокси не работает: {proxy_url} - {response.status_code}")
                return False
                
        except Exception as e:
            print(f"✗ Ошибка тестирования прокси {proxy_url}: {e}")
            return False
        finally:
            session.close()
    
    def test_all_proxies(self) -> List[str]:
        """Тестирует все прокси и возвращает список рабочих"""
        print("🔍 Тестируем прокси...")
        
        working_proxies = []
        proxy_list = self.config['socks5_proxy_list']
        
        for proxy in proxy_list:
            if self.test_proxy(proxy):
                working_proxies.append(proxy)
        
        print(f"✅ Найдено {len(working_proxies)} рабочих прокси из {len(proxy_list)}")
        return working_proxies
    
    def get_current_proxy(self) -> Optional[str]:
        """Возвращает текущий прокси"""
        with self.lock:
            if not self.working_proxies:
                return None
            return self.working_proxies[self.current_proxy_index]
    
    def switch_to_next_proxy(self):
        """Переключается на следующий прокси"""
        with self.lock:
            if len(self.working_proxies) > 1:
                self.current_proxy_index = (self.current_proxy_index + 1) % len(self.working_proxies)
                current_proxy = self.working_proxies[self.current_proxy_index]
                print(f"🔄 Переключились на прокси: {current_proxy}")
    
    def advertise_server(self, server: dict) -> bool:
        """Рекламирует один сервер"""
        current_proxy = self.get_current_proxy()
        if not current_proxy:
            print("❌ Нет доступных прокси для рекламы")
            return False
        
        try:
            session = self.create_session_with_proxy(current_proxy)
            
            # Данные для рекламы
            advertise_data = {
                "Address": server["address"]
            }
            
            # Отправляем POST запрос
            response = session.post(
                f"{self.config['hub_url']}/api/servers/advertise",
                json=advertise_data,
                timeout=self.config['request_timeout_seconds'],
                verify=False
            )
            
            if response.status_code == 200:
                print(f"✅ Сервер зарегистрирован: {server['display_name']}")
                
                # Сбрасываем счетчик ошибок для текущего прокси
                if current_proxy in self.proxy_error_count:
                    del self.proxy_error_count[current_proxy]
                    print(f"🔄 Сброшен счетчик ошибок для прокси: {current_proxy}")
                
                return True
            else:
                error_content = response.text
                print(f"❌ Ошибка регистрации {server['display_name']}: {response.status_code} - {error_content}")
                
                # Увеличиваем счетчик ошибок для прокси
                self.proxy_error_count[current_proxy] = self.proxy_error_count.get(current_proxy, 0) + 1
                
                # Если прокси заблокирован, обновляем список
                if "blocked" in error_content.lower() or "заблокирован" in error_content.lower():
                    print("🚫 Прокси заблокирован, обновляем список...")
                    self.refresh_working_proxies()
                
                return False
                
        except Exception as e:
            print(f"❌ Ошибка рекламы сервера {server['display_name']}: {e}")
            
            # Увеличиваем счетчик ошибок для прокси
            if current_proxy:
                self.proxy_error_count[current_proxy] = self.proxy_error_count.get(current_proxy, 0) + 1
            
            return False
        finally:
            session.close()
    
    def refresh_working_proxies(self):
        """Обновляет список рабочих прокси"""
        print("🔄 Обновляем список рабочих прокси...")
        self.working_proxies = self.test_all_proxies()
        self.current_proxy_index = 0
    
    def advertise_all_servers(self):
        """Рекламирует все серверы"""
        if not self.is_running:
            return
        
        print(f"\n📢 Начинаем рекламу серверов ({datetime.now().strftime('%H:%M:%S')})")
        
        # Периодически очищаем нерабочие прокси
        if datetime.now().minute % 10 == 0:
            self.cleanup_failed_proxies()
        
        # Переключаемся на следующий прокси
        if len(self.working_proxies) > 1:
            self.switch_to_next_proxy()
        
        # Рекламируем все серверы
        success_count = 0
        for server in self.config['servers']:
            # Случайная задержка для обхода блокировки
            delay = random.randint(1, 5)
            time.sleep(delay)
            
            if self.advertise_server(server):
                success_count += 1
        
        print(f"📊 Реклама завершена: {success_count}/{len(self.config['servers'])} серверов зарегистрировано")
    
    def cleanup_failed_proxies(self):
        """Удаляет прокси с большим количеством ошибок"""
        failed_proxies = []
        for proxy, error_count in self.proxy_error_count.items():
            if error_count >= 3:
                failed_proxies.append(proxy)
        
        if failed_proxies:
            print(f"🗑️ Удаляем {len(failed_proxies)} нерабочих прокси")
            for proxy in failed_proxies:
                if proxy in self.working_proxies:
                    self.working_proxies.remove(proxy)
                del self.proxy_error_count[proxy]
    
    def start(self):
        """Запускает рекламу"""
        if self.is_running:
            print("⚠️ Реклама уже запущена")
            return
        
        print("🚀 Запускаем рекламу серверов...")
        
        # Тестируем прокси при запуске
        if self.config.get('auto_test_proxies', True):
            self.working_proxies = self.test_all_proxies()
            
            if not self.working_proxies:
                print("❌ Нет рабочих прокси, реклама не запущена")
                return
        
        self.is_running = True
        
        # Запускаем таймер рекламы
        interval_seconds = self.config['advertisement_interval_minutes'] * 60
        self.advertisement_timer = threading.Timer(interval_seconds, self._advertisement_cycle)
        self.advertisement_timer.start()
        
        # Первая реклама сразу
        self.advertise_all_servers()
        
        print(f"✅ Реклама запущена (интервал: {self.config['advertisement_interval_minutes']} мин)")
    
    def _advertisement_cycle(self):
        """Цикл рекламы (вызывается таймером)"""
        if self.is_running:
            self.advertise_all_servers()
            
            # Планируем следующий цикл
            interval_seconds = self.config['advertisement_interval_minutes'] * 60
            self.advertisement_timer = threading.Timer(interval_seconds, self._advertisement_cycle)
            self.advertisement_timer.start()
    
    def stop(self):
        """Останавливает рекламу"""
        if not self.is_running:
            print("⚠️ Реклама не запущена")
            return
        
        print("🛑 Останавливаем рекламу...")
        self.is_running = False
        
        if self.advertisement_timer:
            self.advertisement_timer.cancel()
            self.advertisement_timer = None
        
        print("✅ Реклама остановлена")
    
    def get_status(self) -> dict:
        """Возвращает статус рекламы"""
        return {
            "is_running": self.is_running,
            "working_proxies_count": len(self.working_proxies),
            "total_proxies_count": len(self.config['socks5_proxy_list']),
            "current_proxy": self.get_current_proxy(),
            "servers_count": len(self.config['servers']),
            "proxy_errors": dict(self.proxy_error_count)
        }

if __name__ == "__main__":
    # Тестирование модуля
    advertiser = Socks5Advertiser()
    
    try:
        advertiser.start()
        
        # Ждем пользовательского ввода для остановки
        input("Нажмите Enter для остановки рекламы...")
        
    except KeyboardInterrupt:
        pass
    finally:
        advertiser.stop()
