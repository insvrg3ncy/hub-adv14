#!/bin/bash

echo "📦 Установка зависимостей для VULPS..."

# Цвета
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

print_status() { echo -e "${GREEN}✅ $1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠️  $1${NC}"; }
print_error() { echo -e "${RED}❌ $1${NC}"; }
print_info() { echo -e "${BLUE}ℹ️  $1${NC}"; }

print_info "Устанавливаем Python зависимости..."

# Устанавливаем зависимости
pip3 install -r requirements.txt --upgrade --user --break-system-packages

if [ $? -eq 0 ]; then
    print_status "Зависимости установлены успешно"
else
    print_error "Ошибка установки зависимостей"
    exit 1
fi

print_info "Проверяем установку..."

# Проверяем, что requests с SOCKS поддержкой работает
python3 -c "
import requests
import socks
import socket

# Тестируем SOCKS поддержку
try:
    # Создаем тестовый сокет с SOCKS5
    test_socket = socks.socksocket()
    test_socket.set_proxy(socks.SOCKS5, '127.0.0.1', 1080)
    print('✅ SOCKS5 поддержка работает')
except Exception as e:
    print(f'❌ Ошибка SOCKS5: {e}')

# Тестируем requests
try:
    response = requests.get('https://httpbin.org/ip', timeout=5)
    if response.status_code == 200:
        print('✅ requests работает')
    else:
        print('❌ requests не работает')
except Exception as e:
    print(f'❌ Ошибка requests: {e}')
"

print_status "Проверка завершена"
