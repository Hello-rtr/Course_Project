// NotificationHandler.cs (обновленный)
using Client_My_Messenger.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Client_My_Messenger.Pages
{
    public class NotificationHandler
    {
        private readonly ClientWebSocket _webSocket;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly NotificationState _notificationState;
        private readonly ConcurrentQueue<Action> _uiActions;
        private bool _isRunning;
        private Task _receiveTask;

        public event EventHandler<string>? RawMessageReceived;
        public event EventHandler<JsonElement>? JsonMessageReceived;

        public NotificationHandler(ClientWebSocket webSocket,
                                 CancellationTokenSource cts,
                                 NotificationState notificationState)
        {
            _webSocket = webSocket;
            _cancellationTokenSource = cts;
            _notificationState = notificationState;
            _uiActions = new ConcurrentQueue<Action>();
            _isRunning = false;
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _receiveTask = Task.Run(async () => await ReceiveMessagesAsync());
            Debug.WriteLine("[NotificationHandler] Запущен");
        }

        public void Stop()
        {
            _isRunning = false;
            _cancellationTokenSource.Cancel();

            try
            {
                _receiveTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            Debug.WriteLine("[NotificationHandler] Остановлен");
        }

        private async Task ReceiveMessagesAsync()
        {
            while (_isRunning && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var message = await GlobalMetods.ReceiveJsonAsync(_webSocket, _cancellationTokenSource);

                    if (string.IsNullOrEmpty(message))
                    {
                        if (message == null)
                        {
                            Debug.WriteLine("[NotificationHandler] Сервер закрыл соединение");
                            break;
                        }
                        continue;
                    }

                    Debug.WriteLine($"[NotificationHandler] Получено: {message.Substring(0, Math.Min(100, message.Length))}...");

                    // Передаем сырое сообщение
                    RawMessageReceived?.Invoke(this, message);

                    // Пытаемся разобрать как JSON
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(message);
                        var root = jsonDoc.RootElement;

                        // Передаем разобранный JSON
                        JsonMessageReceived?.Invoke(this, root);

                        // Обрабатываем тип сообщения
                        await ProcessJsonMessageAsync(root);
                    }
                    catch (JsonException ex)
                    {
                        Debug.WriteLine($"[NotificationHandler] Не JSON: {ex.Message}");
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[NotificationHandler] Операция отменена");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NotificationHandler] Ошибка приема: {ex.Message}");
                    await Task.Delay(1000);
                }
            }

            _isRunning = false;
            Debug.WriteLine("[NotificationHandler] Прием сообщений завершен");
        }

        private async Task ProcessJsonMessageAsync(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("type", out var typeElement))
                {
                    var messageType = typeElement.GetString();

                    switch (messageType)
                    {
                        case "NEW_CHAT_MESSAGE_NOTIFICATION":
                            await ProcessNewChatNotificationAsync(root);
                            break;

                        case "MESSAGE_READ_CONFIRMATION":
                            await ProcessMessageReadConfirmationAsync(root);
                            break;

                        case "USER_STATUS_CHANGE":
                            await ProcessUserStatusChangeAsync(root);
                            break;

                        case "PING":
                            await ProcessPingAsync(root);
                            break;

                        case "SYSTEM_MESSAGE":
                            await ProcessSystemMessageAsync(root);
                            break;

                        case "CHAT_MARKED_AS_READ":
                            await ProcessChatMarkedAsReadAsync(root);
                            break;

                        case "MESSAGE_READ":
                            await ProcessMessageReadAsync(root);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки JSON: {ex.Message}");
            }
        }

        private async Task ProcessNewChatNotificationAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();
                var unreadCount = root.GetProperty("unreadCount").GetInt32();

                Debug.WriteLine($"[NotificationHandler] Новое сообщение в чате {chatId}, непрочитанных: {unreadCount}");

                // Обновляем счетчик
                _notificationState.UpdateFromServer(chatId, unreadCount);

                // Если уведомление содержит предпросмотр сообщения
                if (root.TryGetProperty("messagePreview", out var preview))
                {
                    var message = preview.GetString();
                    Debug.WriteLine($"[NotificationHandler] Предпросмотр: {message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки уведомления: {ex.Message}");
            }
        }

        private async Task ProcessMessageReadConfirmationAsync(JsonElement root)
        {
            try
            {
                var messageId = root.GetProperty("messageId").GetInt32();
                var chatId = root.GetProperty("chatId").GetInt32();
                var readByUserId = root.GetProperty("readByUserId").GetInt32();
                var readByUserName = root.GetProperty("readByUserName").GetString();

                Debug.WriteLine($"[NotificationHandler] Сообщение {messageId} прочитано пользователем {readByUserName}");

                // Здесь можно обновить UI - показать вторую галочку
                ExecuteOnUI(() =>
                {
                    // Поиск и обновление сообщения в истории
                    // Это зависит от вашей реализации UI
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки подтверждения прочтения: {ex.Message}");
            }
        }

        private async Task ProcessUserStatusChangeAsync(JsonElement root)
        {
            try
            {
                var userId = root.GetProperty("userId").GetInt32();
                var isOnline = root.GetProperty("isOnline").GetBoolean();
                var lastActivity = root.TryGetProperty("lastActivity", out var lastAct)
                    ? lastAct.GetString()
                    : DateTime.Now.ToString();

                Debug.WriteLine($"[NotificationHandler] Статус пользователя {userId}: {(isOnline ? "Онлайн" : "Оффлайн")}");

                // Обновить статус в UI
                ExecuteOnUI(() =>
                {
                    // Здесь код для обновления статуса пользователя в интерфейсе
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки изменения статуса: {ex.Message}");
            }
        }

        private async Task ProcessPingAsync(JsonElement root)
        {
            try
            {
                // Можно отправить PONG обратно, но не обязательно
                Debug.WriteLine("[NotificationHandler] Получен PING от сервера");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки PING: {ex.Message}");
            }
        }

        private async Task ProcessSystemMessageAsync(JsonElement root)
        {
            try
            {
                var message = root.GetProperty("message").GetString();

                Debug.WriteLine($"[NotificationHandler] Системное сообщение: {message}");

                ExecuteOnUI(() =>
                {
                    // Показать системное сообщение пользователю
                    // MessageBox.Show(message, "Системное сообщение", 
                    //     MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки системного сообщения: {ex.Message}");
            }
        }

        private async Task ProcessChatMarkedAsReadAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();
                var markedCount = root.GetProperty("markedCount").GetInt32();

                Debug.WriteLine($"[NotificationHandler] Чат {chatId} отмечен как прочитанный: {markedCount} сообщений");

                // Сбрасываем счетчик непрочитанных
                _notificationState.ResetUnread(chatId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки отметки чата: {ex.Message}");
            }
        }

        private async Task ProcessMessageReadAsync(JsonElement root)
        {
            try
            {
                var messageId = root.GetProperty("messageId").GetInt32();
                var chatId = root.GetProperty("chatId").GetInt32();

                Debug.WriteLine($"[NotificationHandler] Сообщение {messageId} отмечено как прочитанное");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NotificationHandler] Ошибка обработки прочтения сообщения: {ex.Message}");
            }
        }

        private void ExecuteOnUI(Action action)
        {
            _uiActions.Enqueue(action);
            // Здесь должен быть механизм выполнения на UI потоке
            // Например, через Dispatcher
        }

        public void ProcessUIActions()
        {
            while (_uiActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NotificationHandler] Ошибка выполнения UI действия: {ex.Message}");
                }
            }
        }
    }
}