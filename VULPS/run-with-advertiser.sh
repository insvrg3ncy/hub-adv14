#!/bin/bash

# Скрипт для запуска мультисерверного SS14 сервера с рекламой

echo "🎮 Запуск мультисерверного SS14 сервера с рекламой..."
echo "=================================================="

# Проверяем наличие Python
if ! command -v python3 &> /dev/null; then
    echo "❌ Python3 не найден. Установите Python3 для продолжения."
    exit 1
fi

# Проверяем наличие .NET
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET не найден. Установите .NET для продолжения."
    exit 1
fi

# Проверяем наличие файлов
if [ ! -f "multi-perfect-ss14-server.py" ]; then
    echo "❌ Файл multi-perfect-ss14-server.py не найден!"
    exit 1
fi

if [ ! -f "multisettings.json" ]; then
    echo "❌ Файл multisettings.json не найден!"
    exit 1
fi

# Делаем файлы исполняемыми
chmod +x multi-perfect-ss14-server.py

echo "🚀 Запускаем мультисерверный SS14 сервер..."
echo ""

# Запускаем мультисерверный сервер в фоне
python3 multi-perfect-ss14-server.py &
SERVER_PID=$!

# Ждем запуска сервера
echo "⏳ Ждем запуска сервера..."
sleep 5

# Проверяем, что сервер запустился
if ! kill -0 $SERVER_PID 2>/dev/null; then
    echo "❌ Ошибка запуска мультисерверного сервера"
    exit 1
fi

echo "✅ Мультисерверный сервер запущен (PID: $SERVER_PID)"
echo ""

# Запускаем рекламу
echo "📢 Запускаем рекламу серверов..."
echo ""

# Копируем конфигурацию в папку MultiServerAdvertiser
cp multisettings.json ../MultiServerAdvertiser/

# Переходим в папку MultiServerAdvertiser и запускаем рекламу
cd ../MultiServerAdvertiser
dotnet run --configuration Release &
ADVERTISER_PID=$!

echo "✅ Реклама запущена (PID: $ADVERTISER_PID)"
echo ""
echo "🎯 Серверы:"
echo "   • MakeSS14GreatAgain88 | Це пранк друзья"
echo "   • PINK VULP SUPREMACY"
echo "   • SAY MEWO IN OUR DISCORD PWWEASE~~"
echo ""
echo "📡 Адрес сервера: http://localhost:1218"
echo "⏹️  Нажмите Ctrl+C для остановки всех процессов"
echo ""

# Функция для остановки процессов
cleanup() {
    echo ""
    echo "🛑 Останавливаем процессы..."
    kill $SERVER_PID 2>/dev/null
    kill $ADVERTISER_PID 2>/dev/null
    echo "✅ Все процессы остановлены"
    exit 0
}

# Обработка сигнала прерывания
trap cleanup SIGINT SIGTERM

# Ждем завершения процессов
wait

