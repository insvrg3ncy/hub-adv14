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
        private Timer _advertisementTimer;
        private readonly MultiServerConfig _config;
        private readonly ILogger _logger;
        private readonly List<ServerInstance> _servers;
        private int _currentProxyIndex = 0;
        private readonly Dictionary<string, int> _proxyErrorCount = new Dictionary<string, int>();

        public MultiServerAdvertiser(MultiServerConfig config, ILogger logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? new ConsoleLogger();
            _hubUrl = config.HubUrl.TrimEnd('/');
            _servers = new List<ServerInstance>();
            
            // Таймер не запускается сразу - будет запущен после тестирования прокси
        }

        private HttpClient CreateHttpClient(string proxyUrl = null)
        {
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                try
                {
                    // Парсим адрес прокси
                    var parts = proxyUrl.Split(':');
                    if (parts.Length != 2)
                    {
                        throw new ArgumentException($"Неверный формат прокси: {proxyUrl}");
                    }

                    var proxyHost = parts[0];
                    var proxyPort = int.Parse(parts[1]);

                    // Создаем WebProxy для SOCKS5
                    var proxy = new WebProxy($"socks5://{proxyHost}:{proxyPort}");
                    
                    // Настройка аутентификации если есть
                    if (!string.IsNullOrEmpty(_config.ProxyUsername))
                    {
                        proxy.Credentials = new NetworkCredential(_config.ProxyUsername, _config.ProxyPassword);
                        _logger.LogInfo($"Используется SOCKS5 прокси: {proxyUrl} (с аутентификацией)");
                    }
                    else
                    {
                        _logger.LogInfo($"Используется SOCKS5 прокси: {proxyUrl} (без аутентификации)");
                    }

                    // Создаем HttpClientHandler с прокси
                    var handler = new HttpClientHandler()
                    {
                        Proxy = proxy,
                        UseProxy = true
                    };

                    var client = new HttpClient(handler);
                    client.Timeout = TimeSpan.FromSeconds(_config.RequestTimeoutSeconds);
                    
                    // Случайный User-Agent для обхода блокировки
                    var userAgents = new[]
                    {
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
                        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0",
                        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:109.0) Gecko/20100101 Firefox/121.0"
                    };
                    var random = new Random();
                    var randomUserAgent = userAgents[random.Next(userAgents.Length)];
                    client.DefaultRequestHeaders.Add("User-Agent", randomUserAgent);
                    
                    // Дополнительные заголовки для обхода блокировки
                    client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
                    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,ru;q=0.8");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                    client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
                    
                    _logger.LogInfo($"HttpClient timeout установлен: {_config.RequestTimeoutSeconds} секунд");
                    return client;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Ошибка настройки SOCKS5 прокси {proxyUrl}: {ex.Message}");
                    _logger.LogError("✗ НЕ ПЕРЕКЛЮЧАЕМСЯ НА ПРЯМОЕ ПОДКЛЮЧЕНИЕ - ТОЛЬКО ПРОКСИ!");
                    throw new InvalidOperationException($"SOCKS5 прокси {proxyUrl} не работает: {ex.Message}");
                }
            }
            else
            {
                _logger.LogError("✗ ПРОКСИ НЕ НАСТРОЕН - ПРОГРАММА НЕ РАБОТАЕТ БЕЗ ПРОКСИ!");
                throw new InvalidOperationException("Прокси не настроен - программа работает только через прокси!");
            }
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
            else if (_config.Socks5ProxyList != null && _config.Socks5ProxyList.Count > 0)
            {
                proxyUrl = _config.Socks5ProxyList[_currentProxyIndex % _config.Socks5ProxyList.Count];
                _currentProxyIndex++;
                _logger.LogInfo($"Сервер {serverAddress} использует SOCKS5 прокси: {proxyUrl}");
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
                var testClient = CreateHttpClient(_config.ProxyUrl);
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
                if (ex.InnerException != null)
                {
                    _logger.LogError($"  └─ Внутренняя ошибка: {ex.InnerException.Message}");
                }
                _logger.LogError($"  └─ Тип ошибки: {ex.GetType().Name}");
                _logger.LogError($"  └─ Используемый прокси: {_config.ProxyUrl}");
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
            // Удаляем старые прокси
            
            _logger.LogInfo("✓ Прокси отключен");
        }

        /// <summary>
        /// Тестирует прокси и возвращает рабочий
        /// </summary>
        public async Task<string> FindWorkingProxyAsync()
        {
            if (_config.Socks5ProxyList == null || !_config.Socks5ProxyList.Any())
            {
                _logger.LogWarning("Список SOCKS5 прокси пуст, нечего тестировать");
                return null;
            }

            _logger.LogInfo($"Тестируем {_config.Socks5ProxyList.Count} SOCKS5 прокси на доступность и подключение к API...");

            var tasks = _config.Socks5ProxyList.Select(async proxyUrl =>
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

            // Удаляем нерабочие прокси из основного списка
            var workingProxySet = new HashSet<string>(_workingProxies);
            var originalCount = _config.Socks5ProxyList.Count;
            _config.Socks5ProxyList = _config.Socks5ProxyList.Where(p => workingProxySet.Contains(p)).ToList();
            var removedCount = originalCount - _config.Socks5ProxyList.Count;

            if (removedCount > 0)
            {
                _logger.LogInfo($"✓ Удалено {removedCount} нерабочих прокси из списка");
                _logger.LogInfo($"✓ Осталось {_config.Socks5ProxyList.Count} рабочих прокси в списке");
                
                // Сохраняем обновленный список прокси в файл
                await SaveProxiesToFileAsync();
            }

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
        /// Тестирует подключение к API через прокси
        /// </summary>
        private async Task<bool> TestProxyAdvertisementAsync(HttpClient testClient, string proxyUrl)
        {
            try
            {
                // Просто проверяем подключение к API серверов
                var response = await testClient.GetAsync($"{_hubUrl}/api/servers/");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInfo($"  └─ ✓ Прокси работает: {proxyUrl}");
                    return true;
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"  └─ ✗ Прокси не работает: {proxyUrl} - {response.StatusCode} - {responseContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"  └─ ✗ Ошибка подключения через прокси: {proxyUrl} - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Создает HttpClient для тестирования прокси
        /// </summary>
        private HttpClient CreateTestHttpClient(string proxyUrl)
        {
            // Парсим адрес прокси
            var parts = proxyUrl.Split(':');
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Неверный формат прокси: {proxyUrl}");
            }

            var proxyHost = parts[0];
            var proxyPort = int.Parse(parts[1]);

            // Создаем WebProxy для SOCKS5
            var proxy = new WebProxy($"socks5://{proxyHost}:{proxyPort}");
            
            // Настройка аутентификации если есть
            if (!string.IsNullOrEmpty(_config.ProxyUsername))
            {
                proxy.Credentials = new NetworkCredential(_config.ProxyUsername, _config.ProxyPassword);
            }

            // Создаем HttpClientHandler с прокси
            var handler = new HttpClientHandler()
            {
                Proxy = proxy,
                UseProxy = true
            };

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
            if (_config.Socks5ProxyList == null || !_config.Socks5ProxyList.Any())
            {
                _logger.LogWarning("Список SOCKS5 прокси пуст, нечего обновлять");
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
        /// Находит ВСЕ рабочие прокси
        /// </summary>
        public async Task<List<string>> FindAllWorkingProxiesAsync()
        {
            if (_config.Socks5ProxyList == null || !_config.Socks5ProxyList.Any())
            {
                _logger.LogWarning("Список SOCKS5 прокси пуст, нечего тестировать");
                return new List<string>();
            }

            _logger.LogInfo($"Тестируем {_config.Socks5ProxyList.Count} SOCKS5 прокси на доступность и подключение к API...");

            var tasks = _config.Socks5ProxyList.Select(async proxyUrl =>
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
                    _logger.LogWarning($"✗ Прокси не работает: {proxyUrl} - {ex.Message}");
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);
            var workingProxies = results.Where(r => r != null).ToList();

            // Удаляем нерабочие прокси из основного списка
            var workingProxySet = new HashSet<string>(workingProxies);
            var originalCount = _config.Socks5ProxyList.Count;
            _config.Socks5ProxyList = _config.Socks5ProxyList.Where(p => workingProxySet.Contains(p)).ToList();
            var removedCount = originalCount - _config.Socks5ProxyList.Count;

            if (removedCount > 0)
            {
                _logger.LogInfo($"✓ Удалено {removedCount} нерабочих прокси из списка");
                _logger.LogInfo($"✓ Осталось {_config.Socks5ProxyList.Count} рабочих прокси в списке");
            }

            if (workingProxies.Count > 0)
            {
                _logger.LogInfo($"✓ Найдено {workingProxies.Count} рабочих прокси для рекламы:");
                foreach (var proxy in workingProxies)
                {
                    _logger.LogInfo($"  - {proxy}");
                }
            }
            else
            {
                _logger.LogWarning("✗ Рабочие прокси не найдены");
            }

            // Сохраняем обновленный список прокси в файл
            await SaveProxiesToFileAsync();

            return workingProxies;
        }

        /// <summary>
        /// Сохраняет текущий список прокси в файл
        /// </summary>
        public async Task SaveProxiesToFileAsync()
        {
            try
            {
                if (_config.Socks5ProxyList != null && _config.Socks5ProxyList.Any())
                {
                    await File.WriteAllLinesAsync("socks5_proxy_list.txt", _config.Socks5ProxyList);
                    _logger.LogInfo($"✓ Сохранено {_config.Socks5ProxyList.Count} рабочих прокси в файл socks5_proxy_list.txt");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка сохранения прокси в файл: {ex.Message}");
            }
        }

        /// <summary>
        /// Устанавливает список рабочих прокси
        /// </summary>
        public void SetWorkingProxies(List<string> proxies)
        {
            _workingProxies = proxies ?? new List<string>();
            _currentProxyIndex = 0;
        }

        /// <summary>
        /// Переключается на следующий прокси из списка
        /// </summary>
        public void SwitchToNextProxy()
        {
            if (_workingProxies.Count == 0)
            {
                _logger.LogError("✗ Нет рабочих прокси для переключения");
                return;
            }

            _currentProxyIndex = (_currentProxyIndex + 1) % _workingProxies.Count;
            var nextProxy = _workingProxies[_currentProxyIndex];
            
            _logger.LogInfo($"Переключаемся на следующий прокси: {nextProxy} ({_currentProxyIndex + 1}/{_workingProxies.Count})");
            SwitchToProxy(nextProxy);
        }

        /// <summary>
        /// Получает текущий активный прокси
        /// </summary>
        public string GetCurrentProxy()
        {
            if (_workingProxies.Count > 0)
            {
                return _workingProxies[_currentProxyIndex % _workingProxies.Count];
            }
            return null;
        }

        /// <summary>
        /// Удаляет нерабочий прокси из списка (только после нескольких ошибок)
        /// </summary>
        public async void RemoveFailedProxy(string proxyUrl)
        {
            // Увеличиваем счетчик ошибок для прокси
            if (!_proxyErrorCount.ContainsKey(proxyUrl))
            {
                _proxyErrorCount[proxyUrl] = 0;
            }
            _proxyErrorCount[proxyUrl]++;
            
            _logger.LogWarning($"✗ Ошибка прокси {proxyUrl} (ошибка #{_proxyErrorCount[proxyUrl]})");
            
            // Удаляем прокси только после 3 ошибок
            if (_proxyErrorCount[proxyUrl] >= 3)
            {
                bool removedFromWorking = _workingProxies.Remove(proxyUrl);
                bool removedFromMain = _config.Socks5ProxyList.Remove(proxyUrl);
                
                if (removedFromWorking || removedFromMain)
                {
                    _logger.LogWarning($"✗ Удаляем прокси после {_proxyErrorCount[proxyUrl]} ошибок: {proxyUrl}");
                    _logger.LogInfo($"Осталось рабочих прокси: {_workingProxies.Count}");
                    _logger.LogInfo($"Осталось прокси в списке: {_config.Socks5ProxyList.Count}");
                    
                    // Удаляем из счетчика ошибок
                    _proxyErrorCount.Remove(proxyUrl);
                    
                    // Сохраняем обновленный список прокси в файл
                    await SaveProxiesToFileAsync();
                    
                    // Сбрасываем индекс если он выходит за границы
                    if (_currentProxyIndex >= _workingProxies.Count)
                    {
                        _currentProxyIndex = 0;
                    }
                    
                    // Если прокси закончились, останавливаем программу
                    if (_workingProxies.Count == 0)
                    {
                        _logger.LogError("✗ ВСЕ ПРОКСИ НЕРАБОЧИЕ - ПРОГРАММА ОСТАНАВЛИВАЕТСЯ!");
                        Environment.Exit(1);
                    }
                }
            }
            else
            {
                _logger.LogInfo($"Прокси {proxyUrl} остается в списке (ошибок: {_proxyErrorCount[proxyUrl]}/3)");
            }
        }

        /// <summary>
        /// Проверяет и удаляет нерабочие прокси из основного списка
        /// </summary>
        public async Task CleanupFailedProxiesAsync()
        {
            if (_config.Socks5ProxyList == null || !_config.Socks5ProxyList.Any())
                return;

            _logger.LogInfo("Проверяем и удаляем нерабочие прокси из основного списка...");
            
            var tasks = _config.Socks5ProxyList.Select(async proxyUrl =>
            {
                try
                {
                    using var testClient = CreateTestHttpClient(proxyUrl);
                    var response = await testClient.GetAsync($"{_hubUrl}/api/servers");
                    return response.IsSuccessStatusCode ? proxyUrl : null;
                }
                catch
                {
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);
            var workingProxies = results.Where(r => r != null).ToList();
            
            if (workingProxies.Count != _config.Socks5ProxyList.Count)
            {
                var removedCount = _config.Socks5ProxyList.Count - workingProxies.Count;
                _logger.LogInfo($"✓ Удалено {removedCount} нерабочих прокси из основного списка");
                _config.Socks5ProxyList = workingProxies;
                
                // Сохраняем обновленный список прокси в файл
                await SaveProxiesToFileAsync();
            }
        }

        /// <summary>
        /// Загружает список прокси из файла
        /// </summary>
        public void LoadProxiesFromFile()
        {
            try
            {
                if (File.Exists("socks5_proxy_list.txt"))
                {
                    var proxyLines = File.ReadAllLines("socks5_proxy_list.txt")
                        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                        .Select(line => line.Trim())
                        .ToList();

                    _config.Socks5ProxyList = proxyLines;
                    _logger.LogInfo($"Загружено {proxyLines.Count} SOCKS5 прокси из файла socks5_proxy_list.txt");
                }
                else
                {
                    _logger.LogWarning($"Файл SOCKS5 прокси socks5_proxy_list.txt не найден");
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
                var switchClient = CreateHttpClient(_config.ProxyUrl);
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
                var ipClient = CreateHttpClient(_config.ProxyUrl);
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
            // Периодически очищаем нерабочие прокси (каждые 10 циклов)
            if (DateTime.UtcNow.Minute % 10 == 0)
            {
                await CleanupFailedProxiesAsync();
            }
            
            // Переключаемся на следующий прокси перед каждой рекламой
            if (_workingProxies.Count > 1)
            {
                SwitchToNextProxy();
            }
            
            // Рекламируем все серверы одновременно параллельно
            var tasks = _servers.Where(s => s.IsActive).Select(AdvertiseServerAsync);
            await Task.WhenAll(tasks);
        }

        private async Task AdvertiseServerAsync(ServerInstance server)
        {
            // Случайная задержка для обхода блокировки (1-5 секунд)
            var random = new Random();
            var delay = random.Next(1000, 5000);
            await Task.Delay(delay);
            
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
                            
                            // Сбрасываем счетчик ошибок для текущего прокси при успехе
                            var currentProxy = GetCurrentProxy();
                            if (!string.IsNullOrEmpty(currentProxy) && _proxyErrorCount.ContainsKey(currentProxy))
                            {
                                _proxyErrorCount.Remove(currentProxy);
                                _logger.LogInfo($"✓ Сброшен счетчик ошибок для прокси: {currentProxy}");
                            }
                            
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
                        
                        // Если это последняя попытка, удаляем прокси с таймаутом
                        if (attempt == maxRetries)
                        {
                            await Task.Run(() => RemoveFailedProxy(_config.ProxyUrl));
                        }
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
                                
                                // Удаляем нерабочий прокси
                                await Task.Run(() => RemoveFailedProxy(_config.ProxyUrl));
                            }
                        }
                        
                        // Если это последняя попытка и ошибка связана с прокси, удаляем его
                        if (attempt == maxRetries)
                        {
                            await Task.Run(() => RemoveFailedProxy(_config.ProxyUrl));
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
                var testClient = CreateHttpClient(_config.ProxyUrl);
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
        /// Список SOCKS5 прокси для автоматического тестирования
        /// </summary>
        public List<string> Socks5ProxyList { get; set; } = new List<string>();

        /// <summary>
        /// Автоматически тестировать прокси при запуске
        /// </summary>
        public bool AutoTestProxies { get; set; } = true;

        /// <summary>
        /// Таймаут для тестирования прокси в секундах
        /// </summary>
        public int ProxyTestTimeoutSeconds { get; set; } = 10;

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
