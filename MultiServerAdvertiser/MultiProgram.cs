using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SS14ServerAdvertiser
{
    class MultiProgram
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== SS14 Multi-Server Advertiser ===");
            Console.WriteLine("Автоматическая реклама множественных серверов в SS14 Hub");
            Console.WriteLine();

            try
            {
                // Загружаем конфигурацию
                var config = LoadConfiguration();
                var multiConfig = config.GetSection("MultiServerConfig").Get<MultiServerConfig>();
                
                if (multiConfig == null)
                {
                    Console.WriteLine("ОШИБКА: Не найдена секция MultiServerConfig в конфигурации!");
                    return;
                }

                // Создаем логгер
                var logger = new ConsoleLogger();
                
                // Выводим информацию о конфигурации
                logger.LogInfo($"Хаб: {multiConfig.HubUrl}");
                logger.LogInfo($"Режим: {(multiConfig.AutoGenerateServers ? "Автогенерация" : "Ручная конфигурация")}");
                
                if (multiConfig.AutoGenerateServers)
                {
                    logger.LogInfo($"Количество серверов: {multiConfig.ServerCount}");
                }
                else
                {
                    logger.LogInfo($"Количество серверов: {multiConfig.Servers?.Count ?? 0}");
                }
                
                logger.LogInfo($"Интервал рекламы: {multiConfig.AdvertisementIntervalMinutes} мин");

                // Создаем и запускаем адвертайзер
                using var advertiser = new MultiServerAdvertiser(multiConfig, logger);
                
                // Инициализируем серверы
                advertiser.InitializeServers();
                
                // Запускаем таймер рекламы
                advertiser.Start();

                // Небольшая задержка перед первым тестом подключения
                await Task.Delay(2000);
                
                // Тестируем подключение
                var connectionOk = await advertiser.TestConnectionAsync();
                if (!connectionOk)
                {
                    logger.LogError("✗ ПОДКЛЮЧЕНИЕ НЕ РАБОТАЕТ - ПРОГРАММА ОСТАНАВЛИВАЕТСЯ!");
                    return;
                }
                
                logger.LogInfo("Адвертайзер запущен. Нажмите Ctrl+C для остановки...");
                logger.LogInfo("Нажмите 's' + Enter для показа статистики");
                Console.WriteLine();

                // Запускаем мониторинг клавиш
                var cancellationTokenSource = new CancellationTokenSource();
                var keyMonitorTask = MonitorKeys(advertiser, cancellationTokenSource.Token);
                
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cancellationTokenSource.Cancel();
                    logger.LogInfo("Получен сигнал завершения...");
                };

                try
                {
                    await Task.WhenAny(
                        Task.Delay(Timeout.Infinite, cancellationTokenSource.Token),
                        keyMonitorTask
                    );
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемое исключение при завершении
                }

                logger.LogInfo("Останавливаем адвертайзер...");
                advertiser.LogStatistics();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                Console.WriteLine($"Детали: {ex}");
            }
        }

        private static async Task MonitorKeys(MultiServerAdvertiser advertiser, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.KeyChar == 's' || key.KeyChar == 'S')
                        {
                            Console.WriteLine();
                            advertiser.LogStatistics();
                            Console.WriteLine("Нажмите 's' + Enter для показа статистики");
                        }
                    }
                    
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка мониторинга клавиш: {ex.Message}");
                }
            }
        }

        private static IConfiguration LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("multisettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("multisettings.Development.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddCommandLine(Environment.GetCommandLineArgs());

            return builder.Build();
        }
    }
}