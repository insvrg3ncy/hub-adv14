#!/bin/bash

# Скрипт для запуска мультисерверного SS14 сервера

echo "🎮 Запуск мультисерверного SS14 сервера..."
echo "================================================"

# Проверяем наличие Python
if ! command -v python3 &> /dev/null; then
    echo "❌ Python3 не найден. Установите Python3 для продолжения."
    exit 1
fi

# Проверяем наличие файла сервера
if [ ! -f "multi-perfect-ss14-server.py" ]; then
    echo "❌ Файл multi-perfect-ss14-server.py не найден!"
    exit 1
fi

# Проверяем наличие конфигурации
if [ ! -f "multiservers.json" ]; then
    echo "⚠️  Конфигурационный файл multiservers.json не найден."
    echo "📝 Будет использована конфигурация по умолчанию."
fi

# Делаем файл исполняемым
chmod +x multi-perfect-ss14-server.py

# Запускаем сервер
echo "🚀 Запускаем мультисерверный SS14 сервер..."
echo ""

python3 multi-perfect-ss14-server.py
