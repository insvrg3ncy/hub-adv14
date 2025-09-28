#!/bin/bash

# SS14 Multi-Server Advertiser - Скрипт запуска

echo "=== SS14 Multi-Server Advertiser ==="
echo "Проверяем .NET 8.0..."

# Проверяем наличие .NET 9.0
if ! command -v dotnet &> /dev/null; then
    echo "ОШИБКА: .NET не установлен!"
    echo "Установите .NET 8.0 с https://dotnet.microsoft.com/download"
    exit 1
fi

# Проверяем версию .NET
DOTNET_VERSION=$(dotnet --version)
echo "Найден .NET версии: $DOTNET_VERSION"

if [[ ! "$DOTNET_VERSION" =~ ^8\. ]]; then
    echo "ПРЕДУПРЕЖДЕНИЕ: Рекомендуется .NET 8.0, но найдена версия $DOTNET_VERSION"
fi

echo ""
echo "Проверяем конфигурацию..."

# Проверяем наличие конфигурационного файла
if [ ! -f "multisettings.json" ]; then
    echo "ОШИБКА: Файл multisettings.json не найден!"
    echo "Создайте файл конфигурации на основе примера"
    exit 1
fi

echo "Конфигурация найдена ✓"
echo ""

# Собираем проект
echo "Собираем проект..."
dotnet build --configuration Release

if [ $? -ne 0 ]; then
    echo "ОШИБКА: Не удалось собрать проект!"
    exit 1
fi

echo "Сборка завершена ✓"
echo ""

# Запускаем
echo "Запускаем SS14 Multi-Server Advertiser..."
echo "Нажмите Ctrl+C для остановки"
echo "Нажмите 's' + Enter для показа статистики"
echo ""

dotnet run --configuration Release
