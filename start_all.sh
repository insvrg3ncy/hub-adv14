#!/bin/bash

echo "🚀 Запуск SS14 Multi-Server Advertiser и Fake Server"
echo "=================================================="

# Проверяем, что мы в правильной директории
if [ ! -f "MultiServerAdvertiser/MultiServerAdvertiser.csproj" ]; then
    echo "❌ Ошибка: Запустите скрипт из корневой директории проекта"
    exit 1
fi

# Функция для остановки всех процессов при Ctrl+C
cleanup() {
    echo ""
    echo "🛑 Останавливаем все процессы..."
    kill $ADVERTISER_PID 2>/dev/null
    kill $SERVER_PID 2>/dev/null
    wait $ADVERTISER_PID 2>/dev/null
    wait $SERVER_PID 2>/dev/null
    echo "✅ Все процессы остановлены"
    exit 0
}

# Устанавливаем обработчик сигнала
trap cleanup SIGINT SIGTERM

echo "🌐 Запускаем фейковый SS14 сервер..."
cd VULPS
python3 multi-perfect-ss14-server.py &
SERVER_PID=$!
cd ..

echo "⏳ Ждем 3 секунды для запуска сервера..."
sleep 3

echo "📢 Запускаем адвертайзер..."
cd MultiServerAdvertiser
dotnet run &
ADVERTISER_PID=$!
cd ..

echo ""
echo "✅ Оба сервиса запущены!"
echo "📊 Адвертайзер PID: $ADVERTISER_PID"
echo "🎮 Сервер PID: $SERVER_PID"
echo ""
echo "⏹️  Нажмите Ctrl+C для остановки всех сервисов"
echo ""

# Ждем завершения процессов
wait $ADVERTISER_PID
wait $SERVER_PID
