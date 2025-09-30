#!/bin/bash

echo "🚀 Запуск системы SS14 Multi-Server Advertiser"
echo "=============================================="

# Проверяем, что Node.js установлен
if ! command -v node &> /dev/null; then
    echo "❌ Node.js не найден. Устанавливаем..."
    curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -
    sudo apt-get install -y nodejs
fi

# Устанавливаем localtunnel если не установлен
if ! command -v npx &> /dev/null; then
    echo "❌ npm не найден. Устанавливаем..."
    sudo apt-get install -y npm
fi

# Устанавливаем localtunnel
echo "📦 Устанавливаем localtunnel..."
npm install localtunnel

# Запускаем VULPS сервер
echo "🎮 Запускаем VULPS сервер..."
cd VULPS
python3 multi-perfect-ss14-server.py &
VULPS_PID=$!
cd ..

# Ждем запуска VULPS
sleep 3

# Запускаем localtunnel туннели
echo "🌐 Запускаем localtunnel туннели..."
for port in {1212..1224}; do
    echo "  - Порт $port"
    npx localtunnel --port $port --subdomain "ss14-$port" > /dev/null 2>&1 &
    sleep 1
done

# Ждем запуска туннелей
echo "⏳ Ждем запуска туннелей..."
sleep 10

echo "✅ Система запущена!"
echo "📊 PID процессов:"
echo "   - VULPS: $VULPS_PID"
echo ""
echo "🔗 Туннели доступны по адресам:"
for port in {1212..1224}; do
    echo "   - https://ss14-$port.loca.lt"
done
echo ""
echo "⏹️  Для остановки нажмите Ctrl+C"

# Обработка сигнала остановки
trap 'echo "🛑 Останавливаем систему..."; kill $VULPS_PID $ADVERTISER_PID 2>/dev/null; pkill -f localtunnel; exit 0' INT

# Ждем завершения
wait
