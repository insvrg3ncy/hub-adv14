using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text;
using System.Linq;

namespace SS14ServerAdvertiser
{
    /// <summary>
    /// Адвертайзер для множественных серверов SS14
    /// </summary>
    public class MultiServerAdvertiser : IDisposable
    {
        private readonly string _hubUrl;
        private Timer? _advertisementTimer;
        private readonly MultiServerConfig _config;
        private readonly ILogger _logger;
        private readonly List<ServerInstance> _servers;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _requestSemaphore;

        public MultiServerAdvertiser(MultiServerConfig config, ILogger? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? new ConsoleLogger();
            _hubUrl = config.HubUrl.TrimEnd('/');
            _servers = new List<ServerInstance>();
            
            // Создаем один переиспользуемый HttpClient для всех запросов
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SS14MultiServerAdvertiser/1.0");
            
            // Семафор для ограничения количества одновременных запросов
            _requestSemaphore = new SemaphoreSlim(1, 1); // Только один запрос одновременно
            
            _logger.LogInfo($"HttpClient timeout установлен: {_config.RequestTimeoutSeconds} секунд");
        }

        /// <summary>
        /// Тестирует подключение к хабу
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInfo("Тестируем подключение к хабу...");
                
                // Пробуем простой GET запрос к хабу
                var response = await _httpClient.GetAsync($"{_hubUrl}/api/servers");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInfo("✓ Подключение к хабу успешно");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"⚠ Подключение к хабу неуспешно: {response.StatusCode}");
                    return false;
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError($"✗ Таймаут при тестировании подключения: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"✗ Ошибка при тестировании подключения: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger.LogError($"  └─ Внутренняя ошибка: {ex.InnerException.Message}");
                }
                _logger.LogError($"  └─ Тип ошибки: {ex.GetType().Name}");
                return false;
            }
        }






        /// <summary>
        /// Сохраняет конфигурацию с обновленным IP в файл
        /// </summary>
        public async Task SaveConfigWithIpAsync()
        {
            try
            {
                var configJson = JsonSerializer.Serialize(new { MultiServerConfig = _config }, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                await File.WriteAllTextAsync("multisettings.json", configJson);
                _logger.LogInfo($"✓ Конфигурация с IP {_config.CurrentExternalIp} сохранена в multisettings.json");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка сохранения конфигурации: {ex.Message}");
            }
        }


        /// <summary>
        /// Переключает сервер на указанный ID (для мультисерверной конфигурации)
        /// </summary>
        public async Task<bool> SwitchServerAsync(string serverId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_hubUrl.Replace("hub.spacestation14.com", "localhost:1218")}/switch?id={serverId}");
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInfo($"✓ Переключились на сервер: {serverId}");
                    return true;
                }
                else
                {
                    _logger.LogError($"✗ Ошибка переключения на сервер {serverId}: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"✗ Ошибка переключения сервера: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Добавляет сервер в список для рекламы
        /// </summary>
        public void AddServer(string serverAddress, string? displayName = null)
        {
            var server = new ServerInstance
            {
                Address = serverAddress,
                DisplayName = displayName ?? serverAddress,
                LastAdvertised = DateTime.MinValue,
                IsActive = true
            };
            
            _servers.Add(server);
            _logger.LogInfo($"Добавлен сервер: {server.DisplayName} ({server.Address})");
        }

        /// <summary>
        /// Добавляет серверы на основе конфигурации
        /// </summary>
        public void InitializeServers()
        {
            if (_config.Servers != null && _config.Servers.Any())
            {
                foreach (var serverConfig in _config.Servers)
                {
                    AddServer(serverConfig.Address, serverConfig.DisplayName);
                }
            }
            else if (_config.AutoGenerateServers)
            {
                GenerateServers();
            }
        }

        /// <summary>
        /// Автоматически генерирует серверы на основе внешнего IP
        /// </summary>
        private async void GenerateServers()
        {
            try
            {
                var externalIp = await GetExternalIp();
                if (string.IsNullOrEmpty(externalIp))
                {
                    _logger.LogError("Не удалось получить внешний IP адрес");
                    return;
                }

                _logger.LogInfo($"Внешний IP: {externalIp}");
                _logger.LogInfo($"Генерируем {_config.ServerCount} серверов...");

                // Сохраняем IP в конфигурации
                _config.CurrentExternalIp = externalIp;

                // Используем последовательные порты начиная с 1212
                var startPort = 1212;
                var usedPorts = new HashSet<int>();

                for (int i = 0; i < _config.ServerCount; i++)
                {
                    int port = startPort + i;
                    usedPorts.Add(port);
                    
                    var serverAddress = $"ss14://{externalIp}:{port}";
                    var displayName = $"t.me/VT_SS14 | PJB, suck my dick";
                    
                    AddServer(serverAddress, displayName);
                    _logger.LogInfo($"Добавлен сервер: {displayName} ({serverAddress})");
                }

                // Сохраняем конфигурацию с обновленным IP
                await SaveConfigWithIpAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка при генерации серверов: {ex.Message}");
            }
        }

        private async Task<string?> GetExternalIp()
        {
            // Список сервисов для получения IP
            var ipServices = new[]
            {
                "https://api.ipify.org?format=json",
                "https://ipapi.co/json/",
                "https://ipinfo.io/json",
                "https://api.myip.com"
            };

            foreach (var service in ipServices)
            {
                try
                {
                    _logger.LogInfo($"Пытаемся получить IP через {service}");
                    
                    var response = await _httpClient.GetStringAsync(service);
                    
                    var json = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    // Пробуем разные поля в зависимости от сервиса
                    string? ip = null;
                    if (json.TryGetProperty("ip", out var ipElement))
                        ip = ipElement.GetString();
                    else if (json.TryGetProperty("query", out var queryElement))
                        ip = queryElement.GetString();
                    else if (json.TryGetProperty("origin", out var originElement))
                        ip = originElement.GetString();
                    
                    if (!string.IsNullOrEmpty(ip))
                    {
                        _logger.LogInfo($"✓ Получен внешний IP: {ip} через {service}");
                        return ip;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"✗ Ошибка получения IP через {service}: {ex.Message}");
                }
            }
            
            _logger.LogError("✗ Не удалось получить внешний IP ни через один сервис");
            return null;
        }

        private async void AdvertiseAllServers(object? state)
        {
            // Рекламируем серверы последовательно с задержкой для предотвращения rate limiting
            var activeServers = _servers.Where(s => s.IsActive).ToList();
            
            foreach (var server in activeServers)
            {
                await AdvertiseServerAsync(server);
                
                // Добавляем задержку между запросами для предотвращения rate limiting
                // Увеличиваем задержку, если были ошибки в предыдущих запросах
                var delay = _config.RequestCooldownMs;
                if (server.ErrorCount > 0)
                {
                    // Увеличиваем задержку при наличии ошибок
                    delay = Math.Max(delay, _config.RequestCooldownMs * 2);
                }
                
                if (server != activeServers.Last())
                {
                    await Task.Delay(delay);
                }
            }
        }

        private async Task AdvertiseServerAsync(ServerInstance server)
        {
            // Используем семафор для ограничения количества одновременных запросов
            await _requestSemaphore.WaitAsync();
            try
            {
                var maxRetries = _config.MaxRetries;
                var retryDelayMs = _config.RetryDelayMs;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (attempt == 1)
                        {
                            _logger.LogInfo($"Рекламируем сервер: {server.DisplayName}");
                        }
                        else
                        {
                            _logger.LogInfo($"Повторная попытка {attempt}/{maxRetries} для сервера: {server.DisplayName}");
                        }

                        // Проверяем доступность сервера (опционально)
                        if (_config.CheckServerAvailability && !await IsServerAccessible(server.Address))
                        {
                            _logger.LogWarning($"Сервер недоступен: {server.DisplayName}");
                            return;
                        }

                        // Отправляем рекламу в хаб
                        var advertiseRequest = new { Address = server.Address };
                        var json = JsonSerializer.Serialize(advertiseRequest);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync($"{_hubUrl}/api/servers/advertise", content);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            server.LastAdvertised = DateTime.UtcNow;
                            server.SuccessCount++;
                            server.ErrorCount = 0; // Сбрасываем счетчик ошибок при успехе
                            
                            _logger.LogInfo($"✓ Сервер зарегистрирован: {server.DisplayName}");
                            return; // Успех, выходим из цикла
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            
                            // Проверяем, является ли это ошибкой "Unable to contact status address"
                            // Это не критичная ошибка - хаб просто не может проверить статус, но сервер может быть доступен
                            bool isStatusCheckError = errorContent.Contains("Unable to contact status address", StringComparison.OrdinalIgnoreCase) ||
                                                      errorContent.Contains("status address", StringComparison.OrdinalIgnoreCase);
                            
                            if (isStatusCheckError)
                            {
                                // Логируем как предупреждение, но не считаем критичной ошибкой
                                _logger.LogWarning($"⚠ Предупреждение регистрации {server.DisplayName}: хаб не может проверить статус сервера. Это может быть нормально, если порты не проброшены или firewall блокирует.");
                                server.SuccessCount++; // Считаем как успех, т.к. регистрация прошла
                                server.LastAdvertised = DateTime.UtcNow;
                                server.ErrorCount = 0; // Сбрасываем счетчик ошибок
                                return;
                            }
                            
                            // InternalServerError (500) - временная ошибка сервера, продолжаем попытки
                            if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                            {
                                _logger.LogWarning($"⚠ Временная ошибка сервера (500) для {server.DisplayName}. Продолжаем попытки...");
                                
                                // Увеличиваем задержку экспоненциально при 500 ошибках (возможный rate limiting)
                                var exponentialDelay = retryDelayMs * (int)Math.Pow(2, attempt - 1);
                                exponentialDelay = Math.Min(exponentialDelay, 30000); // Максимум 30 секунд
                                
                                if (attempt < maxRetries)
                                {
                                    _logger.LogInfo($"Ожидание {exponentialDelay} мс перед следующей попыткой...");
                                    await Task.Delay(exponentialDelay);
                                }
                                // Продолжаем цикл попыток
                            }
                            else
                            {
                                _logger.LogError($"✗ Ошибка регистрации {server.DisplayName}: {response.StatusCode} - {errorContent}");
                                
                                // Если это не временная ошибка, не повторяем
                                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                                {
                                    server.ErrorCount++;
                                    return;
                                }
                            }
                        }
                    }
                    catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                    {
                        _logger.LogError($"✗ Таймаут при рекламе {server.DisplayName} (попытка {attempt}/{maxRetries}): {ex.Message}");
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogError($"✗ Ошибка сети при рекламе {server.DisplayName} (попытка {attempt}/{maxRetries}): {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            _logger.LogError($"  └─ Внутренняя ошибка: {ex.InnerException.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"✗ Исключение при рекламе {server.DisplayName} (попытка {attempt}/{maxRetries}): {ex.Message}");
                    }
                    
                    // Если это не последняя попытка, ждем перед повтором
                    if (attempt < maxRetries)
                    {
                        // Используем экспоненциальную задержку для всех типов ошибок
                        var delay = retryDelayMs * attempt;
                        await Task.Delay(delay);
                    }
                }
                
                // Если все попытки исчерпаны
                server.ErrorCount++;
                _logger.LogError($"✗ Все попытки исчерпаны для сервера: {server.DisplayName}");
            }
            finally
            {
                _requestSemaphore.Release();
            }
        }

        private async Task<bool> IsServerAccessible(string serverAddress)
        {
            try
            {
                var statusUrl = GetServerStatusUrl(serverAddress);
                var response = await _httpClient.GetAsync(statusUrl);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private string GetServerStatusUrl(string serverAddress)
        {
            // Преобразуем ss14:// в http:// для проверки статуса
            if (serverAddress.StartsWith("ss14://"))
            {
                var address = serverAddress.Substring(7).TrimEnd('/'); // убираем "ss14://" и "/"
                if (!address.Contains(":"))
                    address += ":1212"; // добавляем порт по умолчанию
                return $"http://{address}/status";
            }
            else if (serverAddress.StartsWith("ss14s://"))
            {
                var address = serverAddress.Substring(8).TrimEnd('/'); // убираем "ss14s://" и "/"
                if (!address.Contains(":"))
                    address += ":443"; // добавляем порт по умолчанию
                return $"https://{address}/status";
            }
            
            throw new ArgumentException($"Неподдерживаемый формат адреса: {serverAddress}");
        }

        /// <summary>
        /// Получает статистику по серверам
        /// </summary>
        public ServerStatistics GetStatistics()
        {
            var activeServers = _servers.Where(s => s.IsActive).ToList();
            
            return new ServerStatistics
            {
                TotalServers = _servers.Count,
                ActiveServers = activeServers.Count,
                TotalSuccesses = activeServers.Sum(s => s.SuccessCount),
                TotalErrors = activeServers.Sum(s => s.ErrorCount),
                LastAdvertised = activeServers.Max(s => s.LastAdvertised),
                Servers = activeServers.ToList()
            };
        }

        /// <summary>
        /// Выводит статистику в лог
        /// </summary>
        public void LogStatistics()
        {
            var stats = GetStatistics();
            _logger.LogInfo("=== СТАТИСТИКА ===");
            _logger.LogInfo($"Всего серверов: {stats.TotalServers}");
            _logger.LogInfo($"Активных серверов: {stats.ActiveServers}");
            _logger.LogInfo($"Успешных реклам: {stats.TotalSuccesses}");
            _logger.LogInfo($"Ошибок: {stats.TotalErrors}");
            _logger.LogInfo($"Последняя реклама: {stats.LastAdvertised:HH:mm:ss}");
        }

        /// <summary>
        /// Запускает таймер рекламы
        /// </summary>
        public void Start()
        {
            if (_advertisementTimer == null)
            {
                _advertisementTimer = new Timer(AdvertiseAllServers, null, TimeSpan.Zero, TimeSpan.FromMinutes(_config.AdvertisementIntervalMinutes));
                _logger.LogInfo($"✓ Таймер рекламы запущен (интервал: {_config.AdvertisementIntervalMinutes} мин)");
            }
        }

        public void Stop()
        {
            _advertisementTimer?.Dispose();
            _advertisementTimer = null;
        }

        public void Dispose()
        {
            Stop();
            _httpClient?.Dispose();
            _requestSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// Конфигурация для множественных серверов
    /// </summary>
    public class MultiServerConfig
    {
        /// <summary>
        /// URL ServerHub
        /// </summary>
        public string HubUrl { get; set; } = "https://hub.spacestation14.com";

        /// <summary>
        /// Список серверов для рекламы
        /// </summary>
        public List<ServerConfigItem> Servers { get; set; } = new List<ServerConfigItem>();

        /// <summary>
        /// Автоматически генерировать серверы на основе внешнего IP
        /// </summary>
        public bool AutoGenerateServers { get; set; } = false;

        /// <summary>
        /// Количество серверов для автогенерации
        /// </summary>
        public int ServerCount { get; set; } = 5;

        /// <summary>
        /// Интервал рекламы в минутах
        /// </summary>
        public int AdvertisementIntervalMinutes { get; set; } = 2;

        /// <summary>
        /// Таймаут запросов в секундах
        /// </summary>
        public int RequestTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// Проверять доступность серверов перед рекламой
        /// </summary>
        public bool CheckServerAvailability { get; set; } = false;

        /// <summary>
        /// Количество повторных попыток при ошибках
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Задержка между повторными попытками в миллисекундах
        /// </summary>
        public int RetryDelayMs { get; set; } = 2000;

        /// <summary>
        /// Задержка между запросами к API (cooldown) в миллисекундах для предотвращения rate limiting
        /// </summary>
        public int RequestCooldownMs { get; set; } = 1000;

        /// <summary>
        /// Текущий внешний IP адрес
        /// </summary>
        public string? CurrentExternalIp { get; set; }

        /// <summary>
        /// Автоматически обновлять IP адрес при запуске
        /// </summary>
        public bool AutoUpdateIp { get; set; } = true;

    }

    /// <summary>
    /// Конфигурация отдельного сервера
    /// </summary>
    public class ServerConfigItem
    {
        public string Address { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Информация о сервере
    /// </summary>
    public class ServerInstance
    {
        public string Address { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTime LastAdvertised { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Статистика по серверам
    /// </summary>
    public class ServerStatistics
    {
        public int TotalServers { get; set; }
        public int ActiveServers { get; set; }
        public int TotalSuccesses { get; set; }
        public int TotalErrors { get; set; }
        public DateTime LastAdvertised { get; set; }
        public List<ServerInstance> Servers { get; set; } = new List<ServerInstance>();
    }

    /// <summary>
    /// Интерфейс для логирования
    /// </summary>
    public interface ILogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
    }

    /// <summary>
    /// Простой консольный логгер
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        public void LogInfo(string message)
        {
            Console.WriteLine($"[INFO] {DateTime.Now:HH:mm:ss} - {message}");
        }

        public void LogWarning(string message)
        {
            Console.WriteLine($"[WARN] {DateTime.Now:HH:mm:ss} - {message}");
        }

        public void LogError(string message)
        {
            Console.WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} - {message}");
        }
    }
}