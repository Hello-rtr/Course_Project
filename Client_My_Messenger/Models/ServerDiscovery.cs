using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Client_My_Messenger
{
    public class DiscoveredServer
    {
        public string Service { get; set; } = "";
        public string Version { get; set; } = "";
        public string WsEndpoint { get; set; } = "";
        public string HttpEndpoint { get; set; } = "";
        public string ServerName { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string DiscoveredFrom { get; set; } = "";
        public int ClientCount { get; set; }
    }

    public static class ServerDiscovery
    {
        private const int DiscoveryPort = 8124;
        private static UdpClient _udpListener;
        private static CancellationTokenSource _cts;
        private static readonly List<DiscoveredServer> _discoveredServers = new();
        private static readonly object _lock = new object();

        public static event EventHandler<DiscoveredServer> ServerDiscovered;

        public static void StartDiscovery()
        {
            try
            {
                _cts = new CancellationTokenSource();
                _discoveredServers.Clear();

                _udpListener = new UdpClient(DiscoveryPort);
                _udpListener.EnableBroadcast = true;
                _udpListener.MulticastLoopback = true;

                Task.Run(() => ListenForBroadcasts());

                // Отправляем запрос для быстрого ответа
                SendDiscoveryRequest();

                Debug.WriteLine("[Discovery] Служба обнаружения запущена");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discovery] Ошибка запуска: {ex.Message}");
            }
        }

        public static void StopDiscovery()
        {
            try
            {
                _cts?.Cancel();
                _udpListener?.Close();
                _udpListener?.Dispose();
                Debug.WriteLine("[Discovery] Служба остановлена");
            }
            catch { }
        }

        private static async Task ListenForBroadcasts()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpListener.ReceiveAsync();
                    var json = Encoding.UTF8.GetString(result.Buffer);

                    var serverInfo = JsonSerializer.Deserialize<DiscoveredServer>(json);
                    if (serverInfo != null)
                    {
                        serverInfo.DiscoveredFrom = result.RemoteEndPoint.Address.ToString();
                        ProcessDiscoveredServer(serverInfo);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Discovery] Ошибка приема: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }

        private static void ProcessDiscoveredServer(DiscoveredServer server)
        {
            lock (_lock)
            {
                // Удаляем старые записи (> 10 секунд)
                _discoveredServers.RemoveAll(s =>
                    (DateTime.Now - s.Timestamp).TotalSeconds > 10);

                // Проверяем, есть ли уже такой сервер
                var existing = _discoveredServers.FirstOrDefault(s =>
                    s.WsEndpoint == server.WsEndpoint);

                if (existing == null)
                {
                    // Добавляем новый сервер
                    _discoveredServers.Add(server);

                    Debug.WriteLine($"[Discovery] Найден сервер: {server.ServerName} ({server.DiscoveredFrom})");

                    // Уведомляем подписчиков
                    ServerDiscovered?.Invoke(null, server);
                }
                else
                {
                    // Обновляем timestamp
                    existing.Timestamp = server.Timestamp;
                }
            }
        }

        private static void SendDiscoveryRequest()
        {
            try
            {
                using var client = new UdpClient();
                client.EnableBroadcast = true;

                var request = Encoding.UTF8.GetBytes("MYMESSENGER_DISCOVERY_REQUEST");
                var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                client.Send(request, request.Length, broadcastEndPoint);

                Debug.WriteLine("[Discovery] Запрос отправлен");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discovery] Ошибка запроса: {ex.Message}");
            }
        }

        public static async Task<DiscoveredServer> WaitForServerAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<DiscoveredServer>();
            var cancellationTokenSource = new CancellationTokenSource(timeout);

            // Подписываемся на событие обнаружения
            EventHandler<DiscoveredServer> handler = null;
            handler = (sender, server) =>
            {
                tcs.TrySetResult(server);
            };

            ServerDiscovered += handler;
            StartDiscovery();

            try
            {
                using (cancellationTokenSource.Token.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                ServerDiscovered -= handler;
                StopDiscovery();
            }
        }

        public static async Task<DiscoveredServer> WaitForServerAsync()
        {
            // Бесконечное ожидание
            var tcs = new TaskCompletionSource<DiscoveredServer>();

            EventHandler<DiscoveredServer> handler = null;
            handler = (sender, server) =>
            {
                tcs.TrySetResult(server);
            };

            ServerDiscovered += handler;
            StartDiscovery();

            try
            {
                return await tcs.Task;
            }
            finally
            {
                ServerDiscovered -= handler;
                StopDiscovery();
            }
        }
    }
}