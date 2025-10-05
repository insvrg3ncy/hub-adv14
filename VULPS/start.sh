#!/bin/bash

echo "🚀 VULPS - Автоматический запуск с ngrok через прокси"

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

# Проверки
check_command() {
    if ! command -v $1 &> /dev/null; then
        print_error "$1 не найден!"
        return 1
    fi
    return 0
}

# Проверяем зависимости
print_info "Проверяем зависимости..."
if ! check_command "python3"; then exit 1; fi
if ! check_command "pip3"; then exit 1; fi
if ! check_command "ngrok"; then 
    print_error "ngrok не найден!"
    echo "📦 Установите ngrok:"
    echo "   wget https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-linux-amd64.tgz"
    echo "   tar xvzf ngrok-v3-stable-linux-amd64.tgz"
    echo "   sudo mv ngrok /usr/local/bin/"
    echo "   ngrok config add-authtoken YOUR_TOKEN"
    exit 1
fi

# Устанавливаем зависимости Python
print_info "Устанавливаем зависимости Python..."
pip3 install -r requirements.txt

# Проверяем файлы
if [ ! -f "socks5_proxy_list.txt" ]; then
    print_error "Файл socks5_proxy_list.txt не найден!"
    exit 1
fi

if [ ! -s "socks5_proxy_list.txt" ]; then
    print_error "Файл socks5_proxy_list.txt пуст!"
    exit 1
fi

# Читаем первый прокси
PROXY=$(head -n 1 socks5_proxy_list.txt | tr -d '\r\n')
if [ -z "$PROXY" ]; then
    print_error "Не удалось прочитать прокси из файла"
    exit 1
fi

PROXY_HOST=$(echo $PROXY | cut -d: -f1)
PROXY_PORT=$(echo $PROXY | cut -d: -f2)

print_info "Используем прокси: $PROXY_HOST:$PROXY_PORT"

# Настраиваем переменные окружения для прокси
export HTTP_PROXY="socks5://$PROXY_HOST:$PROXY_PORT"
export HTTPS_PROXY="socks5://$PROXY_HOST:$PROXY_PORT"
export ALL_PROXY="socks5://$PROXY_HOST:$PROXY_PORT"

print_status "Переменные окружения настроены"

# Функция очистки
cleanup() {
    print_info "Останавливаем процессы..."
    pkill -f ngrok 2>/dev/null
    pkill -f python3 2>/dev/null
    print_status "Процессы остановлены"
}

# Обработчик сигналов
trap cleanup EXIT INT TERM

# Запускаем ngrok менеджер в фоне
print_info "Запускаем ngrok менеджер..."
python3 ngrok_manager.py &
NGROK_PID=$!

# Ждем создания туннелей
print_info "Ждем создания туннелей..."
sleep 15

# Проверяем, что ngrok менеджер работает
if ! kill -0 $NGROK_PID 2>/dev/null; then
    print_error "ngrok менеджер не запустился"
    exit 1
fi

print_status "ngrok менеджер запущен (PID: $NGROK_PID)"

# Запускаем VULPS сервер
print_info "Запускаем VULPS сервер..."
echo ""
echo "🌐 ngrok туннели созданы через прокси $PROXY_HOST:$PROXY_PORT"
echo "📡 Серверы доступны через публичные URL'ы"
echo "⏹️  Нажмите Ctrl+C для остановки"
echo ""

python3 multi-perfect-ss14-server.py
