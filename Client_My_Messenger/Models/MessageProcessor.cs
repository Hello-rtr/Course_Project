// MessageProcessor.cs
using Client_My_Messenger.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Client_My_Messenger.Pages
{
    public class MessageProcessor
    {
        private readonly ClientWebSocket _webSocket;
        private readonly CancellationTokenSource _cts;
        private readonly NotificationState _notificationState;
        private readonly ConcurrentQueue<Action> _uiActions;
        private bool _isRunning;
        private Task _processingTask;

        public event EventHandler<JsonElement>? MessageReceived;
        public event EventHandler<string>? RawMessageReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler? ConnectionClosed;

        public MessageProcessor(ClientWebSocket webSocket,
                               CancellationTokenSource cts,
                               NotificationState notificationState)
        {
            _webSocket = webSocket;
            _cts = cts;
            _notificationState = notificationState;
            _uiActions = new ConcurrentQueue<Action>();
            _isRunning = false;
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _processingTask = Task.Run(async () => await ProcessMessagesAsync());
            Debug.WriteLine("[MessageProcessor] Запущен");
        }

        public void Stop()
        {
            _isRunning = false;

            try
            {
                _processingTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            Debug.WriteLine("[MessageProcessor] Остановлен");
        }

        private async Task ProcessMessagesAsync()
        {
            Debug.WriteLine("[MessageProcessor] Начало обработки сообщений...");

            while (_isRunning && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var message = await GlobalMetods.ReceiveJsonAsync(_webSocket, _cts);

                    if (string.IsNullOrEmpty(message))
                    {
                        if (message == null)
                        {
                            Debug.WriteLine("[MessageProcessor] Сервер закрыл соединение");
                            ExecuteOnUI(() => ConnectionClosed?.Invoke(this, EventArgs.Empty));
                            break;
                        }
                        continue;
                    }

                    Debug.WriteLine($"[MessageProcessor] Получено: {message.Substring(0, Math.Min(200, message.Length))}...");

                    // Передаем сырое сообщение
                    RawMessageReceived?.Invoke(this, message);

                    // Пытаемся разобрать как JSON
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(message);
                        var root = jsonDoc.RootElement;

                        // Определяем тип сообщения
                        if (root.TryGetProperty("type", out var typeElement))
                        {
                            var messageType = typeElement.GetString();
                            Debug.WriteLine($"[MessageProcessor] Тип сообщения: {messageType}");

                            // Если это уведомление - обрабатываем здесь
                            if (IsNotification(messageType))
                            {
                                await ProcessNotificationAsync(root, messageType);
                            }
                            else
                            {
                                Debug.WriteLine($"[MessageProcessor] Обычное сообщение типа: {messageType}");
                            }
                        }

                        // ВСЕ сообщения передаем дальше (для основной логики)
                        MessageReceived?.Invoke(this, root);
                    }
                    catch (JsonException ex)
                    {
                        // Не JSON сообщение
                        Debug.WriteLine($"[MessageProcessor] Не JSON: {ex.Message}");

                        // Но все равно передаем дальше как текст
                        ExecuteOnUI(() =>
                        {
                            // Можно обработать текстовые сообщения
                            if (message.Contains("Успешная") || message.Contains("Ошибка"))
                            {
                                Debug.WriteLine($"[MessageProcessor] Текстовое сообщение: {message}");
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[MessageProcessor] Операция отменена");
                    break;
                }
                catch (WebSocketException ex)
                {
                    Debug.WriteLine($"[MessageProcessor] WebSocket ошибка: {ex.Message}");
                    ExecuteOnUI(() => ErrorOccurred?.Invoke(this, $"WebSocket ошибка: {ex.Message}"));
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MessageProcessor] Общая ошибка: {ex.Message}");
                    ExecuteOnUI(() => ErrorOccurred?.Invoke(this, $"Ошибка обработки: {ex.Message}"));

                    if (_isRunning)
                    {
                        await Task.Delay(1000);
                    }
                }
            }

            _isRunning = false;
            Debug.WriteLine("[MessageProcessor] Обработка сообщений завершена");
        }

        private bool IsNotification(string? messageType)
        {
            return messageType switch
            {
                "NEW_CHAT_MESSAGE_NOTIFICATION" => true,
                "MESSAGE_READ_CONFIRMATION" => true,
                "USER_STATUS_CHANGE" => true,
                "PING" => true,
                "SYSTEM_MESSAGE" => true,
                "CHAT_MARKED_AS_READ" => true,
                "MESSAGE_READ" => true,
                "USER_PROFILE_UPDATE" => true,
                "YOUR_ROLE_UPDATED" => true,
                "NEW_MESSAGE" => true,
                _ => false
            };
        }

        private async Task ProcessNotificationAsync(JsonElement root, string messageType)
        {
            try
            {
                switch (messageType)
                {
                    case "NEW_CHAT_MESSAGE_NOTIFICATION":
                        await ProcessNewChatNotificationAsync(root);
                        break;

                    case "NEW_MESSAGE":
                        await ProcessNewMessageNotificationAsync(root);
                        break;

                    case "MESSAGE_READ_CONFIRMATION":
                        await ProcessMessageReadConfirmationAsync(root);
                        break;

                    case "USER_STATUS_CHANGE":
                        await ProcessUserStatusChangeAsync(root);
                        break;

                    case "CHAT_MARKED_AS_READ":
                        await ProcessChatMarkedAsReadAsync(root);
                        break;

                    case "MESSAGE_READ":
                        await ProcessMessageReadAsync(root);
                        break;

                    case "PING":
                        await ProcessPingAsync(root);
                        break;

                    case "SYSTEM_MESSAGE":
                        await ProcessSystemMessageAsync(root);
                        break;

                    case "USER_PROFILE_UPDATE":
                        await ProcessUserProfileUpdateAsync(root);
                        break;

                    default:
                        Debug.WriteLine($"[MessageProcessor] Необработанное уведомление: {messageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка обработки уведомления {messageType}: {ex.Message}");
            }
        }

        private async Task ProcessNewChatNotificationAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();
                var unreadCount = root.GetProperty("unreadCount").GetInt32();

                Debug.WriteLine($"[MessageProcessor] Уведомление: чат {chatId}, непрочитанных: {unreadCount}");

                ExecuteOnUI(() =>
                {
                    _notificationState.UpdateFromServer(chatId, unreadCount);

                    // Показываем уведомление, если есть предпросмотр
                    if (root.TryGetProperty("messagePreview", out var preview) &&
                        root.TryGetProperty("senderName", out var sender))
                    {
                        var messageText = preview.GetString();
                        var senderName = sender.GetString();

                        Debug.WriteLine($"[Уведомление] {senderName}: {messageText}");

                        // Можно показать всплывающее уведомление
                        // ShowToastNotification(senderName, messageText, chatId);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка NEW_CHAT_MESSAGE_NOTIFICATION: {ex.Message}");
            }
        }

        private async Task ProcessNewMessageNotificationAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine($"[MessageProcessor] Новое сообщение в активном чате");

                // Если это новое сообщение в активном чате, оно будет обработано в основном потоке
                // Здесь можно обновить счетчик непрочитанных
                if (root.TryGetProperty("Message", out var messageElement) &&
                    messageElement.TryGetProperty("IdChat", out var chatIdElement))
                {
                    var chatId = chatIdElement.GetInt32();

                    ExecuteOnUI(() =>
                    {
                        // Увеличиваем счетчик, если чат не активен
                        var currentCount = _notificationState.GetUnreadCount(chatId);
                        _notificationState.UpdateFromServer(chatId, currentCount + 1);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка NEW_MESSAGE: {ex.Message}");
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

                Debug.WriteLine($"[MessageProcessor] Подтверждение прочтения: сообщение {messageId} прочитано {readByUserName}");

                ExecuteOnUI(() =>
                {
                    // Обновить UI - показать вторую галочку на сообщении
                    // Это можно сделать через событие
                    // OnMessageReadConfirmed?.Invoke(messageId, readByUserId);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка MESSAGE_READ_CONFIRMATION: {ex.Message}");
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

                Debug.WriteLine($"[MessageProcessor] Статус пользователя {userId}: {(isOnline ? "Онлайн" : "Оффлайн")}");

                ExecuteOnUI(() =>
                {
                    // Обновить статус пользователя в UI
                    // OnUserStatusChanged?.Invoke(userId, isOnline, lastActivity);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка USER_STATUS_CHANGE: {ex.Message}");
            }
        }

        private async Task ProcessChatMarkedAsReadAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();
                var markedCount = root.GetProperty("markedCount").GetInt32();

                Debug.WriteLine($"[MessageProcessor] Чат {chatId} отмечен как прочитанный: {markedCount} сообщений");

                ExecuteOnUI(() =>
                {
                    _notificationState.ResetUnread(chatId);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка CHAT_MARKED_AS_READ: {ex.Message}");
            }
        }

        private async Task ProcessMessageReadAsync(JsonElement root)
        {
            try
            {
                var messageId = root.GetProperty("messageId").GetInt32();
                var chatId = root.GetProperty("chatId").GetInt32();

                Debug.WriteLine($"[MessageProcessor] Сообщение {messageId} отмечено как прочитанное в чате {chatId}");

                // Можно уменьшить счетчик непрочитанных
                ExecuteOnUI(() =>
                {
                    var currentCount = _notificationState.GetUnreadCount(chatId);
                    if (currentCount > 0)
                    {
                        _notificationState.UpdateFromServer(chatId, currentCount - 1);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка MESSAGE_READ: {ex.Message}");
            }
        }

        private async Task ProcessPingAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine("[MessageProcessor] Получен PING от сервера");

                // Можно отправить PONG обратно
                // Но сервер не требует ответа, согласно памятке
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка PING: {ex.Message}");
            }
        }

        private async Task ProcessSystemMessageAsync(JsonElement root)
        {
            try
            {
                var message = root.GetProperty("message").GetString();

                Debug.WriteLine($"[MessageProcessor] Системное сообщение: {message}");

                ExecuteOnUI(() =>
                {
                    // Показать системное сообщение
                    // MessageBox.Show(message, "Системное сообщение", 
                    //     MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка SYSTEM_MESSAGE: {ex.Message}");
            }
        }

        private async Task ProcessUserProfileUpdateAsync(JsonElement root)
        {
            try
            {
                var userId = root.GetProperty("userId").GetInt32();

                Debug.WriteLine($"[MessageProcessor] Обновление профиля пользователя {userId}");

                ExecuteOnUI(() =>
                {
                    // Обновить информацию о пользователе в UI
                    // OnUserProfileUpdated?.Invoke(userId);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка USER_PROFILE_UPDATE: {ex.Message}");
            }
        }

        private void ExecuteOnUI(Action action)
        {
            try
            {
                _uiActions.Enqueue(action);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка добавления UI действия: {ex.Message}");
            }
        }

        public void ProcessUIActions()
        {
            try
            {
                while (_uiActions.TryDequeue(out var action))
                {
                    try
                    {
                        // Используем Dispatcher для выполнения на UI потоке
                        if (Application.Current?.Dispatcher != null)
                        {
                            if (Application.Current.Dispatcher.CheckAccess())
                            {
                                action();
                            }
                            else
                            {
                                Application.Current.Dispatcher.Invoke(action);
                            }
                        }
                        else
                        {
                            // Если Dispatcher недоступен, выполняем напрямую
                            action();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MessageProcessor] Ошибка выполнения UI действия: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageProcessor] Ошибка обработки UI действий: {ex.Message}");
            }
        }

        public bool IsRunning => _isRunning;
    }
}