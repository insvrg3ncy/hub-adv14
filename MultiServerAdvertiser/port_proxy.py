#!/usr/bin/env python3
"""
Прокси для перенаправления запросов с портов 1212, 1213, 1214 на 1218
"""

import socket
import threading
import time

def proxy_connection(client_socket, target_port):
    """Проксирует соединение на целевой порт"""
    target_socket = None
    try:
        # Устанавливаем таймауты
        client_socket.settimeout(30.0)
        
        # Подключаемся к основному серверу
        target_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        target_socket.settimeout(30.0)
        target_socket.connect(('localhost', target_port))
        
        # Пересылаем данные в обе стороны
        def forward_data(source, destination, direction):
            try:
                while True:
                    data = source.recv(4096)
                    if not data:
                        break
                    destination.send(data)
            except Exception as e:
                print(f"Ошибка пересылки {direction}: {e}")
            finally:
                try:
                    source.close()
                except:
                    pass
                try:
                    destination.close()
                except:
                    pass
        
        # Запускаем пересылку в обе стороны
        client_to_target = threading.Thread(target=forward_data, args=(client_socket, target_socket, "client->target"))
        target_to_client = threading.Thread(target=forward_data, args=(target_socket, client_socket, "target->client"))
        
        client_to_target.daemon = True
        target_to_client.daemon = True
        
        client_to_target.start()
        target_to_client.start()
        
        # Ждем завершения одного из потоков
        client_to_target.join(timeout=60)
        target_to_client.join(timeout=60)
        
    except Exception as e:
        print(f"Ошибка проксирования: {e}")
    finally:
        try:
            client_socket.close()
        except:
            pass
        try:
            if target_socket:
                target_socket.close()
        except:
            pass

def start_proxy_server(proxy_port, target_port):
    """Запускает прокси сервер"""
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.settimeout(1.0)  # Таймаут для accept
    
    try:
        server.bind(('0.0.0.0', proxy_port))
        server.listen(5)
        print(f"✅ Прокси порт {proxy_port} -> {target_port} запущен")
        
        while True:
            try:
                client_socket, addr = server.accept()
                print(f"📡 Подключение с {addr[0]}:{addr[1]} -> порт {proxy_port}")
                
                # Обрабатываем соединение в отдельном потоке
                proxy_thread = threading.Thread(
                    target=proxy_connection, 
                    args=(client_socket, target_port)
                )
                proxy_thread.daemon = True
                proxy_thread.start()
                
            except socket.timeout:
                continue  # Просто продолжаем ждать
            except Exception as e:
                print(f"Ошибка принятия соединения на порту {proxy_port}: {e}")
                continue
            
    except Exception as e:
        print(f"❌ Ошибка прокси порта {proxy_port}: {e}")

if __name__ == "__main__":
    print("🔄 Запуск прокси серверов...")
    print("=" * 50)
    
    # Запускаем прокси для каждого порта
    ports = [
        (1212, 1218),  # 1212 -> 1218
        (1213, 1218),  # 1213 -> 1218  
        (1214, 1218),  # 1214 -> 1218
    ]
    
    threads = []
    
    for proxy_port, target_port in ports:
        thread = threading.Thread(
            target=start_proxy_server,
            args=(proxy_port, target_port)
        )
        thread.daemon = True
        thread.start()
        threads.append(thread)
        time.sleep(0.1)  # Небольшая задержка между запусками
    
    print("🚀 Все прокси серверы запущены!")
    print("⏹️  Нажмите Ctrl+C для остановки")
    
    try:
        # Ждем завершения всех потоков
        for thread in threads:
            thread.join()
    except KeyboardInterrupt:
        print("\n🛑 Прокси серверы остановлены")
