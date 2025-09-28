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
        private readonly Timer _advertisementTimer;
        private readonly MultiServerConfig _config;
        private readonly ILogger _logger;
        private readonly List<ServerInstance> _servers;
        private int _currentProxyIndex = 0;

        public MultiServerAdvertiser(MultiServerConfig config, ILogger logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? new ConsoleLogger();
            _hubUrl = config.HubUrl.TrimEnd('/');
            _servers = new List<ServerInstance>();
            
            _advertisementTimer = new Timer(AdvertiseAllServers, null, TimeSpan.Zero, TimeSpan.FromMinutes(config.AdvertisementIntervalMinutes));
        }

        private HttpClient CreateHttpClient(string proxyUrl = null)
        {
            var handler = new HttpClientHandler();
            
            // Игнорируем SSL ошибки для работы с проблемными прокси
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            
            // Настройка прокси если указан
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                try
                {
                    var proxy = new WebProxy(proxyUrl);
                    if (!string.IsNullOrEmpty(_config.ProxyUsername))
                    {
                        proxy.Credentials = new NetworkCredential(_config.ProxyUsername, _config.ProxyPassword);
                        _logger.LogInfo($"Используется прокси: {proxyUrl} (с аутентификацией)");
                    }
                    else
                    {
                        _logger.LogInfo($"Используется прокси: {proxyUrl} (без аутентификации)");
                    }
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Ошибка настройки прокси {proxyUrl}: {ex.Message}");
                    _logger.LogInfo("Переключаемся на прямое подключение...");
                }
            }
            else
            {
                _logger.LogInfo("Прокси не настроен, используется прямое подключение");
            }

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
            
            // Логируем установленный таймаут для отладки
            _logger.LogInfo($"HttpClient timeout установлен: {_config.RequestTimeoutSeconds} секунд");
            
            // User-Agent для идентификации
            client.DefaultRequestHeaders.Add("User-Agent", "SS14MultiServerAdvertiser/1.0");
            
            return client;
        }

        /// <summary>
        /// Список рабочих прокси (не заблокированных)
        /// </summary>
        private List<string> _workingProxies = new List<string>();

        /// <summary>
        /// Получает HttpClient для конкретного сервера с уникальным прокси
        /// </summary>
        private HttpClient GetHttpClientForServer(string serverAddress)
        {
            // Всегда создаем новый HttpClient для каждого запроса
            // Это предотвращает проблемы с disposed объектами
            string proxyUrl = null;
            
            // Используем рабочие прокси, если они есть
            if (_workingProxies.Count > 0)
            {
                proxyUrl = _workingProxies[_currentProxyIndex % _workingProxies.Count];
                _currentProxyIndex++;
                _logger.LogInfo($"Сервер {serverAddress} использует рабочий прокси: {proxyUrl}");
            }
            else if (_config.ProxyList != null && _config.ProxyList.Count > 0)
            {
                proxyUrl = _config.ProxyList[_currentProxyIndex % _config.ProxyList.Count];
                _currentProxyIndex++;
                _logger.LogInfo($"Сервер {serverAddress} использует прокси: {proxyUrl}");
            }

            return CreateHttpClient(proxyUrl);
        }

        /// <summary>
        /// Тестирует подключение к хабу через прокси
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInfo("Тестируем подключение к хабу...");
                
                // Пробуем простой GET запрос к хабу
                var testClient = CreateHttpClient();
                var response = await testClient.GetAsync($"{_hubUrl}/api/servers");
                testClient.Dispose();
                
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
                return false;
            }
        }

        /// <summary>
        /// Пересоздает HttpClient без прокси
        /// </summary>
        public void DisableProxy()
        {
            _logger.LogWarning("Отключаем прокси...");
            
            _config.ProxyUrl = null;
            _config.ProxyList = null;
            
            _logger.LogInfo("✓ Прокси отключен");
        }

        /// <summary>
        /// Тестирует прокси и возвращает рабочий
        /// </summary>
        public async Task<string> FindWorkingProxyAsync()
        {
            if (_config.ProxyList == null || !_config.ProxyList.Any())
            {
                _logger.LogWarning("Список прокси пуст");
                return null;
            }

            _logger.LogInfo($"Тестируем {_config.ProxyList.Count} прокси на доступность и возможность рекламы...");

            var tasks = _config.ProxyList.Select(async proxyUrl =>
            {
                try
                {
                    _logger.LogInfo($"Тестируем прокси: {proxyUrl}");
                    
                    using var testClient = CreateTestHttpClient(proxyUrl);
                    
                    // Сначала проверяем доступность хаба
                    var serversResponse = await testClient.GetAsync($"{_hubUrl}/api/servers");
                    if (!serversResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"✗ Прокси не может получить список серверов: {proxyUrl} (статус: {serversResponse.StatusCode})");
                        return null;
                    }
                    
                    // Теперь проверяем возможность рекламы
                    var canAdvertise = await TestProxyAdvertisementAsync(testClient, proxyUrl);
                    if (canAdvertise)
                    {
                        _logger.LogInfo($"✓ Прокси работает и может рекламировать: {proxyUrl}");
                        return proxyUrl;
                    }
                    else
                    {
                        _logger.LogWarning($"✗ Прокси заблокирован для рекламы: {proxyUrl}");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"✗ Прокси не работает: {proxyUrl} ({ex.Message})");
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);
            _workingProxies = results.Where(r => r != null).ToList();

            if (_workingProxies.Count > 0)
            {
                _logger.LogInfo($"✓ Найдено {_workingProxies.Count} рабочих прокси для рекламы:");
                foreach (var proxy in _workingProxies)
                {
                    _logger.LogInfo($"  - {proxy}");
                }
                return _workingProxies.First();
            }
            else
            {
                _logger.LogError("✗ Ни один прокси не может рекламировать в хабе");
                return null;
            }
        }

        /// <summary>
        /// Тестирует возможность рекламы через прокси
        /// </summary>
        private async Task<bool> TestProxyAdvertisementAsync(HttpClient testClient, string proxyUrl)
        {
            try
            {
                // Используем тестовый адрес для проверки рекламы
                var testAddress = "ss14://test.example.com:1212";
                var advertiseRequest = new { Address = testAddress };
                var json = JsonSerializer.Serialize(advertiseRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await testClient.PostAsync($"{_hubUrl}/api/servers/advertise", content);
                
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    if (responseContent.Contains("blocked") || responseContent.Contains("заблокирован"))
                    {
                        _logger.LogWarning($"  └─ Прокси заблокирован для рекламы: {responseContent}");
                        return false;
                    }
                    else
                    {
                        _logger.LogWarning($"  └─ Ошибка рекламы через прокси: {response.StatusCode} - {responseContent}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  └─ Ошибка тестирования рекламы: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Создает HttpClient для тестирования прокси
        /// </summary>
        private HttpClient CreateTestHttpClient(string proxyUrl)
        {
            var handler = new HttpClientHandler();
            
            // Игнорируем SSL ошибки
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            
            // Настраиваем прокси
            var proxy = new WebProxy(proxyUrl);
            handler.Proxy = proxy;
            handler.UseProxy = true;
            
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(_config.ProxyTestTimeoutSeconds);
            client.DefaultRequestHeaders.Add("User-Agent", "SS14MultiServerAdvertiser/1.0");
            
            return client;
        }

        /// <summary>
        /// Переключается на указанный прокси
        /// </summary>
        public void SwitchToProxy(string proxyUrl)
        {
            _logger.LogInfo($"Переключаемся на прокси: {proxyUrl}");
            
            _config.ProxyUrl = proxyUrl;
            
            _logger.LogInfo("✓ Прокси обновлен");
        }

        /// <summary>
        /// Проверяет и обновляет список рабочих прокси
        /// </summary>
        public async Task RefreshWorkingProxiesAsync()
        {
            if (_config.ProxyList == null || !_config.ProxyList.Any())
            {
                _logger.LogWarning("Список прокси пуст, нечего обновлять");
                return;
            }

            _logger.LogInfo("Обновляем список рабочих прокси...");
            await FindWorkingProxyAsync();
        }

        /// <summary>
        /// Получает количество рабочих прокси
        /// </summary>
        public int GetWorkingProxyCount()
        {
            return _workingProxies.Count;
        }

        /// <summary>
        /// Загружает список прокси из файла
        /// </summary>
        public void LoadProxiesFromFile()
        {
            try
            {
                if (File.Exists(_config.ProxyListFile))
                {
                    var proxyLines = File.ReadAllLines(_config.ProxyListFile)
                        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                        .Select(line => line.Trim())
                        .ToList();

                    _config.ProxyList = proxyLines;
                    _logger.LogInfo($"Загружено {proxyLines.Count} прокси из файла {_config.ProxyListFile}");
                }
                else
                {
                    _logger.LogWarning($"Файл прокси {_config.ProxyListFile} не найден");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка загрузки прокси из файла: {ex.Message}");
            }
        }

        /// <summary>
        /// Переключает сервер на указанный ID (для мультисерверной конфигурации)
        /// </summary>
        public async Task<bool> SwitchServerAsync(string serverId)
        {
            try
            {
                var switchClient = CreateHttpClient();
                var response = await switchClient.GetAsync($"{_hubUrl.Replace("hub.spacestation14.com", "localhost:1218")}/switch?id={serverId}");
                switchClient.Dispose();
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
        public void AddServer(string serverAddress, string displayName = null)
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

                var random = new Random();
                var usedPorts = new HashSet<int>();

                for (int i = 0; i < _config.ServerCount; i++)
                {
                    int port;
                    do
                    {
                        port = random.Next(1024, 65535);
                    } while (usedPorts.Contains(port));
                    
                    usedPorts.Add(port);
                    
                    var serverAddress = $"ss14://{externalIp}:{port}/";
                    var displayName = $"Auto Server {i + 1}";
                    
                    AddServer(serverAddress, displayName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка при генерации серверов: {ex.Message}");
            }
        }

        private async Task<string> GetExternalIp()
        {
            try
            {
                var ipClient = CreateHttpClient();
                var response = await ipClient.GetStringAsync("https://api.ipify.org?format=json");
                ipClient.Dispose();
                var json = JsonSerializer.Deserialize<JsonElement>(response);
                return json.GetProperty("ip").GetString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка получения внешнего IP: {ex.Message}");
                return null;
            }
        }

        private async void AdvertiseAllServers(object state)
        {
            // Рекламируем все серверы одновременно параллельно
            var tasks = _servers.Where(s => s.IsActive).Select(AdvertiseServerAsync);
            await Task.WhenAll(tasks);
        }

        private async Task AdvertiseServerAsync(ServerInstance server)
        {
            using (var httpClient = GetHttpClientForServer(server.Address))
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

                        var response = await httpClient.PostAsync($"{_hubUrl}/api/servers/advertise", content);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            server.LastAdvertised = DateTime.UtcNow;
                            server.SuccessCount++;
                            _logger.LogInfo($"✓ Сервер зарегистрирован: {server.DisplayName}");
                            return; // Успех, выходим из цикла
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            _logger.LogError($"✗ Ошибка регистрации {server.DisplayName}: {response.StatusCode} - {errorContent}");
                            
                            // Проверяем, заблокирован ли прокси
                            if (errorContent.Contains("blocked") || errorContent.Contains("заблокирован"))
                            {
                                _logger.LogWarning($"Прокси заблокирован, обновляем список рабочих прокси...");
                                await RefreshWorkingProxiesAsync();
                                
                                // Если есть рабочие прокси, попробуем снова
                                if (_workingProxies.Count > 0)
                                {
                                    _logger.LogInfo($"Переключаемся на рабочий прокси для сервера {server.DisplayName}");
                                    continue; // Повторяем попытку с новым прокси
                                }
                            }
                            
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
                            
                            // Проверяем, является ли это ошибкой прокси
                            if (ex.InnerException.Message.Contains("502") || 
                                ex.InnerException.Message.Contains("503") || 
                                ex.InnerException.Message.Contains("404"))
                            {
                                _logger.LogWarning($"  └─ Прокси возвращает ошибку {ex.InnerException.Message.Split(' ').FirstOrDefault(s => s.Contains("502") || s.Contains("503") || s.Contains("404"))}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"✗ Исключение при рекламе {server.DisplayName} (попытка {attempt}/{maxRetries}): {ex.Message}");
                    }
                    
                    // Если это не последняя попытка, ждем перед повтором
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                    }
                }
                
                // Если все попытки исчерпаны
                server.ErrorCount++;
                _logger.LogError($"✗ Все попытки исчерпаны для сервера: {server.DisplayName}");
            }
        }

        private async Task<bool> IsServerAccessible(string serverAddress)
        {
            try
            {
                var statusUrl = GetServerStatusUrl(serverAddress);
                var testClient = CreateHttpClient();
                var response = await testClient.GetAsync(statusUrl);
                testClient.Dispose();
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
            _logger.LogInfo($"Рабочих прокси: {_workingProxies.Count}");
            _logger.LogInfo($"Последняя реклама: {stats.LastAdvertised:HH:mm:ss}");
        }

        public void Stop()
        {
            _advertisementTimer?.Dispose();
        }

        public void Dispose()
        {
            Stop();
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
        /// URL прокси
        /// </summary>
        public string ProxyUrl { get; set; }

        /// <summary>
        /// Список резервных прокси
        /// </summary>
        public List<string> BackupProxies { get; set; } = new List<string>();

        /// <summary>
        /// Имя пользователя для прокси
        /// </summary>
        public string ProxyUsername { get; set; }

        /// <summary>
        /// Пароль для прокси
        /// </summary>
        public string ProxyPassword { get; set; }

        /// <summary>
        /// Количество повторных попыток при ошибках
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Задержка между повторными попытками в миллисекундах
        /// </summary>
        public int RetryDelayMs { get; set; } = 2000;

        /// <summary>
        /// Автоматически отключать прокси при ошибках подключения
        /// </summary>
        public bool AutoDisableProxyOnError { get; set; } = true;

        /// <summary>
        /// Список прокси для автоматического тестирования
        /// </summary>
        public List<string> ProxyList { get; set; } = new List<string>();

        /// <summary>
        /// Автоматически тестировать прокси при запуске
        /// </summary>
        public bool AutoTestProxies { get; set; } = true;

        /// <summary>
        /// Таймаут для тестирования прокси в секундах
        /// </summary>
        public int ProxyTestTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// Путь к файлу со списком прокси
        /// </summary>
        public string ProxyListFile { get; set; } = "proxy_list.txt";
    }

    /// <summary>
    /// Конфигурация отдельного сервера
    /// </summary>
    public class ServerConfigItem
    {
        public string Address { get; set; }
        public string DisplayName { get; set; }
    }

    /// <summary>
    /// Информация о сервере
    /// </summary>
    public class ServerInstance
    {
        public string Address { get; set; }
        public string DisplayName { get; set; }
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
