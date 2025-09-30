#!/usr/bin/env python3
"""
Мультисерверный фейковый SS14 сервер
Поддерживает множественные серверы с переключением между ними
Полностью имитирует поведение настоящего SS14 сервера
"""

import http.server
import socketserver
import json
import os
import time
import urllib.parse
import threading
import random
from typing import Dict, List, Optional

class ServerInstance:
    """Информация о сервере"""
    def __init__(self, name: str, port: int, status_file: str = None, info_file: str = None):
        self.name = name
        self.port = port
        self.status_file = status_file
        self.info_file = info_file
        self.is_active = True
        self.last_accessed = time.time()
        self.access_count = 0

# Глобальные переменные для серверов
servers = {}
current_server = None
server_lock = threading.Lock()

class MultiSS14Handler(http.server.BaseHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)

    def do_POST(self):
        """Обработка POST запросов (для хаба)"""
        # Логируем запрос
        print(f"[{time.strftime('%H:%M:%S')}] POST {self.client_address[0]} -> {self.path}")
        
        # Парсим путь
        parsed_path = urllib.parse.urlparse(self.path)
        path = parsed_path.path
        
        # Обрабатываем POST запросы так же, как GET
        if path == '/status':
            self.handle_status()
        elif path == '/info':
            self.handle_info()
        else:
            self.send_error(404, "Not Found")

    def do_GET(self):
        # Логируем запрос
        print(f"[{time.strftime('%H:%M:%S')}] GET {self.client_address[0]} -> {self.path}")
        
        # Парсим путь и query параметры
        parsed_path = urllib.parse.urlparse(self.path)
        path = parsed_path.path
        query = parsed_path.query
        
        # Обработка специальных эндпоинтов для управления серверами
        if path == '/servers':
            self.handle_servers_list()
        elif path == '/switch':
            self.handle_server_switch(query)
        elif path == '/add':
            self.handle_add_server(query)
        elif path == '/remove':
            self.handle_remove_server(query)
        elif path == '/status':
            self.handle_status()
        elif path == '/info':
            self.handle_info()
        else:
            self.send_response(404)
            self.send_header('Content-Type', 'text/plain')
            self.end_headers()
            self.wfile.write(b'Not Found')

    def handle_servers_list(self):
        """Показать список всех серверов"""
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Connection', 'close')
        self.send_header('Server', 'SS14-MultiServer/1.0')
        self.send_header('Cache-Control', 'no-cache')
        self.end_headers()
        
        with server_lock:
            servers_info = []
            for server_id, server in servers.items():
                servers_info.append({
                    'id': server_id,
                    'name': server.name,
                    'port': server.port,
                    'is_active': server.is_active,
                    'is_current': server_id == current_server,
                    'last_accessed': server.last_accessed,
                    'access_count': server.access_count
                })
        
        response = {
            'current_server': current_server,
            'total_servers': len(servers),
            'servers': servers_info
        }
        
        self.wfile.write(json.dumps(response, ensure_ascii=False, indent=2).encode('utf-8'))

    def handle_server_switch(self, query):
        """Переключиться на другой сервер"""
        global current_server
        params = urllib.parse.parse_qs(query)
        server_id = params.get('id', [None])[0]
        
        if not server_id:
            self.send_response(400)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(json.dumps({'error': 'Server ID required'}).encode('utf-8'))
            return
        
        with server_lock:
            if server_id not in servers:
                self.send_response(404)
                self.send_header('Content-Type', 'application/json')
                self.end_headers()
                self.wfile.write(json.dumps({'error': 'Server not found'}).encode('utf-8'))
                return
            
            if not servers[server_id].is_active:
                self.send_response(400)
                self.send_header('Content-Type', 'application/json')
                self.end_headers()
                self.wfile.write(json.dumps({'error': 'Server is inactive'}).encode('utf-8'))
                return
            
            current_server = server_id
            servers[server_id].last_accessed = time.time()
            servers[server_id].access_count += 1
        
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps({
            'success': True,
            'current_server': current_server,
            'server_name': servers[server_id].name
        }).encode('utf-8'))

    def handle_add_server(self, query):
        """Добавить новый сервер"""
        global current_server
        params = urllib.parse.parse_qs(query)
        name = params.get('name', [None])[0]
        port = params.get('port', [None])[0]
        
        if not name or not port:
            self.send_response(400)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(json.dumps({'error': 'Name and port required'}).encode('utf-8'))
            return
        
        try:
            port = int(port)
        except ValueError:
            self.send_response(400)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(json.dumps({'error': 'Invalid port number'}).encode('utf-8'))
            return
        
        server_id = f"server_{len(servers) + 1}"
        
        with server_lock:
            servers[server_id] = ServerInstance(name, port)
            if not current_server:
                current_server = server_id
        
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps({
            'success': True,
            'server_id': server_id,
            'name': name,
            'port': port
        }).encode('utf-8'))

    def handle_remove_server(self, query):
        """Удалить сервер"""
        global current_server
        params = urllib.parse.parse_qs(query)
        server_id = params.get('id', [None])[0]
        
        if not server_id:
            self.send_response(400)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(json.dumps({'error': 'Server ID required'}).encode('utf-8'))
            return
        
        with server_lock:
            if server_id not in servers:
                self.send_response(404)
                self.send_header('Content-Type', 'application/json')
                self.end_headers()
                self.wfile.write(json.dumps({'error': 'Server not found'}).encode('utf-8'))
                return
            
            del servers[server_id]
            
            # Если удалили текущий сервер, переключаемся на первый доступный
            if current_server == server_id:
                if servers:
                    current_server = next(iter(servers.keys()))
                else:
                    current_server = None
        
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps({'success': True}).encode('utf-8'))

    def handle_status(self):
        """Обработка запроса статуса"""
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Connection', 'close')
        self.send_header('Server', 'SS14-Server/1.0')
        self.send_header('Cache-Control', 'no-cache')
        self.end_headers()
        
        # Определяем сервер по порту
        server_port = self.server.server_address[1]
        server_name = f"server_{server_port}"
        
        with server_lock:
            if server_name not in servers:
                # Создаем новый сервер для этого порта
                servers[server_name] = ServerInstance(
                    name=f"Server {server_port}",
                    port=server_port
                )
            
            server = servers[server_name]
            server.last_accessed = time.time()
            server.access_count += 1
            
            # Возвращаем разные данные для разных портов
            status = self.get_server_status_by_port(server_port)
        
        self.wfile.write(json.dumps(status, ensure_ascii=False).encode('utf-8'))

    def handle_info(self):
        """Обработка запроса информации"""
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Connection', 'close')
        self.send_header('Server', 'SS14-Server/1.0')
        self.send_header('Cache-Control', 'no-cache')
        self.end_headers()
        
        # Определяем сервер по порту
        server_port = self.server.server_address[1]
        server_name = f"server_{server_port}"
        
        with server_lock:
            if server_name not in servers:
                # Создаем новый сервер для этого порта
                servers[server_name] = ServerInstance(
                    name=f"Server {server_port}",
                    port=server_port
                )
            
            server = servers[server_name]
            server.last_accessed = time.time()
            server.access_count += 1
            
            # Возвращаем разные данные для разных портов
            info = self.get_server_info_by_port(server_port)
        
        self.wfile.write(json.dumps(info, ensure_ascii=False).encode('utf-8'))

    def load_server_status(self, server: ServerInstance) -> dict:
        """Загружает статус сервера из файла или возвращает fallback"""
        if server.status_file and os.path.exists(server.status_file):
            try:
                with open(server.status_file, 'r', encoding='utf-8') as f:
                    return json.loads(f.read().strip())
            except:
                pass
        
        # Fallback с именем сервера
        return {
            "name": server.name,
            "players": random.randint(50, 2000),
            "tags": [],
            "map": None,
            "round_id": random.randint(1, 100),
            "soft_max_players": 5000,
            "panic_bunker": False,
            "baby_jail": False,
            "run_level": 1,
            "preset": "Секрет"
        }

    def load_server_info(self, server: ServerInstance) -> dict:
        """Загружает информацию о сервере из файла или возвращает fallback"""
        if server.info_file and os.path.exists(server.info_file):
            try:
                with open(server.info_file, 'r', encoding='utf-8') as f:
                    return json.loads(f.read().strip())
            except:
                pass
        
        # Fallback с именем сервера
        return {
            "connect_address": f"ss14://localhost:{server.port}/",
            "auth": {
                "mode": "Required",
                "public_key": "h9/LkNgIKvPihU7/DFM22F+uerH+VIVWyKPXaxZNICc="
            },
            "build": {
                "engine_version": "238.0.0",
                "fork_id": "syndicate-public",
                "version": "e301f52e655c57d6df4a0475e75eb4b24f64e0e4",
                "download_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/file/SS14.Client.zip",
                "hash": "7670B58E1A0A39C33D5AB882194BB29D60951C3F88DB93EFB1E92F6648CCBDE2",
                "acz": False,
                "manifest_download_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/download",
                "manifest_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/manifest",
                "manifest_hash": "9AF4E8A392C87A4BDB60CDA83A59AFCE4DEF439FF44E6094DF077295A6964C7E"
            },
            "desc": f"Сервер {server.name} - Гойда!"
        }

    def get_server_status_by_port(self, port: int) -> dict:
        """Возвращает статус сервера в зависимости от порта"""
        # Список названий серверов
        server_names = [
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать", 
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
            "dsc.gg/nwTYTsqh | хаб будет плакать",
        ]
        
        # Список карт
        maps = ["BoxStation", "DeltaStation", "MetaStation", "PackedStation", "Saltern", "OasisStation", "KiloStation", "BirdshotStation"]
        
        # Получаем индекс сервера (порт - 1212)
        server_index = port - 1212
        if server_index < 0 or server_index >= len(server_names):
            return self.get_fallback_status()
        
        # Генерируем случайные данные
        name = server_names[server_index]
        players = random.randint(180, 250)
        map_name = random.choice(maps)
        round_id = random.randint(1, 100)
        
        return {
            "name": name,
            "players": players,
            "tags": [],
            "map": map_name,
            "round_id": round_id,
            "soft_max_players": 350,
            "panic_bunker": False,
            "baby_jail": False,
            "run_level": 1,
            "preset": "Medium"
        }

    def get_server_info_by_port(self, port: int) -> dict:
        """Возвращает информацию о сервере в зависимости от порта"""
        # Проверяем, что порт в допустимом диапазоне
        if port < 1212 or port > 1244:
            return self.get_fallback_info()
        
        return {
            "connect_address": f"ss14://194.102.104.184:{port}",
            "auth": {
                "mode": "Required",
                "public_key": "h9/LkNgIKvPihU7/DFM22F+uerH+VIVWyKPXaxZNICc="
            },
            "build": {
                "engine_version": "238.0.0",
                "fork_id": "syndicate-public",
                "version": "e301f52e655c57d6df4a0475e75eb4b24f64e0e4",
                "download_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/file/SS14.Client.zip",
                "hash": "7670B58E1A0A39C33D5AB882194BB29D60951C3F88DB93EFB1E92F6648CCBDE2",
                "acz": False,
                "manifest_download_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/download",
                "manifest_hash": "9AF4E8A392C87A4BDB60CDA83A59AFCE4DEF439FF44E6094DF077295A6964C7E"
            },
            "desc": f"discord.gg/HSC6Frb6ma | Server {port-1211} | SS14 Multi-Server",
            "links": [
                {
                    "name": "Discord",
                    "url": "https://discord.gg/HSC6Frb6ma"
                }
            ]
        }

    def get_fallback_status(self) -> dict:
        """Возвращает fallback статус"""
        return {
            "name": "TeZt ZerVer (Fallback)",
            "players": 1723,
            "tags": [],
            "map": None,
            "round_id": 8,
            "soft_max_players": 5000,
            "panic_bunker": False,
            "baby_jail": False,
            "run_level": 1,
            "preset": "Секрет"
        }

    def get_fallback_info(self) -> dict:
        """Возвращает fallback информацию"""
        return {
            "connect_address": "",
            "auth": {
                "mode": "Required",
                "public_key": "h9/LkNgIKvPihU7/DFM22F+uerH+VIVWyKPXaxZNICc="
            },
            "build": {
                "engine_version": "238.0.0",
                "fork_id": "syndicate-public",
                "version": "e301f52e655c57d6df4a0475e75eb4b24f64e0e4",
                "download_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/file/SS14.Client.zip",
                "hash": "7670B58E1A0A39C33D5AB882194BB29D60951C3F88DB93EFB1E92F6648CCBDE2",
                "acz": False,
                "manifest_download_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/download",
                "manifest_url": "https://cdn.station14.ru/fork/syndicate-public/version/e301f52e655c57d6df4a0475e75eb4b24f64e0e4/manifest",
                "manifest_hash": "9AF4E8A392C87A4BDB60CDA83A59AFCE4DEF439FF44E6094DF077295A6964C7E"
            },
            "desc": "Гойда!",
            "links": [
                {
                    "name": "Discord",
                    "url": "https://discord.gg/HSC6Frb6ma"
                }
            ]
        }

    def log_message(self, format, *args):
        # Отключаем стандартное логирование
        pass

    def version_string(self):
        return 'SS14-MultiServer/1.0'

def load_config(config_file: str = 'multiservers.json') -> dict:
    """Загружает конфигурацию из файла"""
    default_config = {
        "servers": [
            {
                "id": "server_1",
                "name": "dsc.gg/nwTYTsqh | хаб будет плакать",
                "port": 1212,
                "status_file": "status.json",
                "info_file": "info.json"
            },
            {
                "id": "server_2", 
                "name": "dsc.gg/nwTYTsqh | хаб будет плакать",
                "port": 1213,
                "status_file": None,
                "info_file": None
            },
            {
                "id": "server_3",
                "name": "dsc.gg/nwTYTsqh | хаб будет плакать", 
                "port": 1214,
                "status_file": None,
                "info_file": None
            }
        ],
        "default_server": "server_1",
        "port": 1212
    }
    
    if os.path.exists(config_file):
        try:
            with open(config_file, 'r', encoding='utf-8') as f:
                return json.load(f)
        except Exception as e:
            print(f"⚠️  Ошибка загрузки конфигурации: {e}")
            print("📝 Используем конфигурацию по умолчанию")
    
    return default_config

def save_config(config: dict, config_file: str = 'multiservers.json'):
    """Сохраняет конфигурацию в файл"""
    try:
        with open(config_file, 'w', encoding='utf-8') as f:
            json.dump(config, f, ensure_ascii=False, indent=2)
        print(f"✅ Конфигурация сохранена в {config_file}")
    except Exception as e:
        print(f"❌ Ошибка сохранения конфигурации: {e}")

def create_handler_class():
    """Создает класс обработчика"""
    return MultiSS14Handler

if __name__ == "__main__":
    # Загружаем конфигурацию
    config = load_config()
    
    # Создаем серверы из конфигурации
    
    for server_config in config.get('servers', []):
        server = ServerInstance(
            name=server_config['name'],
            port=server_config['port'],
            status_file=server_config.get('status_file'),
            info_file=server_config.get('info_file')
        )
        servers[server_config['id']] = server
    
    current_server = config.get('default_server')
    if current_server not in servers:
        current_server = next(iter(servers.keys())) if servers else None
    
    # Порты для каждого сервера
    server_ports = list(range(1212, 1227))  # 1212-1225 (13 портов)
    
    print("=" * 70)
    print("🎮 МУЛЬТИСЕРВЕРНЫЙ ФЕЙКОВЫЙ SS14 СЕРВЕР")
    print("=" * 70)
    print(f"📍 Порты: {', '.join(map(str, server_ports))}")
    print(f"🌐 Адреса: {', '.join(f'http://0.0.0.0:{port}' for port in server_ports)}")
    print("📡 Endpoints:")
    print("   • /status, /info - стандартные SS14 эндпоинты")
    print("   • /servers - список всех серверов")
    print("   • /switch?id=server_id - переключиться на сервер")
    print("   • /add?name=Name&port=1234 - добавить сервер")
    print("   • /remove?id=server_id - удалить сервер")
    print(f"🔄 Текущий сервер: {current_server}")
    print(f"📊 Всего серверов: {len(servers)}")
    print("⏹️  Нажмите Ctrl+C для остановки")
    print("=" * 70)
    
    # Показываем список серверов
    for server_id, server in servers.items():
        status = "🟢 Активен" if server.is_active else "🔴 Неактивен"
        current = " ← ТЕКУЩИЙ" if server_id == current_server else ""
        print(f"   {server_id}: {server.name} (порт {server.port}) {status}{current}")
    
    print()
    print("🚀 Запускаем серверы...")
    print()
    
    # Создаем обработчик
    HandlerClass = create_handler_class()
    
    # Создаем серверы для каждого порта
    httpd_servers = []
    threads = []
    
    try:
        for port in server_ports:
            try:
                httpd = socketserver.TCPServer(("0.0.0.0", port), HandlerClass)
                httpd_servers.append(httpd)
                
                # Запускаем сервер в отдельном потоке
                def run_server(server):
                    try:
                        server.serve_forever()
                    except Exception as e:
                        print(f"❌ Ошибка сервера на порту {server.server_address[1]}: {e}")
                
                thread = threading.Thread(target=run_server, args=(httpd,))
                thread.daemon = True
                thread.start()
                threads.append(thread)
                
                print(f"✅ Сервер запущен на порту {port}")
                
            except OSError as e:
                if e.errno == 98:  # Address already in use
                    print(f"❌ Ошибка: Порт {port} уже используется")
                else:
                    print(f"❌ Ошибка порта {port}: {e}")
            except Exception as e:
                print(f"❌ Неожиданная ошибка порта {port}: {e}")
        
        if not httpd_servers:
            print("❌ Не удалось запустить ни одного сервера")
            exit(1)
        
        print(f"🎉 Запущено {len(httpd_servers)} серверов")
        
        # Ждем завершения всех потоков
        for thread in threads:
            thread.join()
            
    except KeyboardInterrupt:
        print("\n🛑 Останавливаем серверы...")
        for httpd in httpd_servers:
            try:
                httpd.shutdown()
            except:
                pass
        print("✅ Серверы остановлены")
        # Сохраняем конфигурацию при выходе
        save_config(config)
    except Exception as e:
        print(f"❌ Неожиданная ошибка: {e}")
