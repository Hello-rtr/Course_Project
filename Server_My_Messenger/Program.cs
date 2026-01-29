// Подключаем необходимые пространства имен
using Client_My_Messenger.Models;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server_My_Messenger;
using Server_My_Messenger.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;

// Основной код приложения
var builder = WebApplication.CreateBuilder(args);

// Конфигурация подключения к базе данных
builder.Services.AddDbContext<LocalMessangerDbContext>(options =>
    options.UseSqlServer("Server=sql.ects;Database=LocalMessangerDB;User ID=student_00;Password=student_00;TrustServerCertificate=True;"));

// Регистрация сервисов
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<MessageService>();

// Регистрируем службу WebSockets с настройками
builder.Services.AddWebSockets(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(120);
});

var app = builder.Build();

app.UseDefaultFiles(); // Это для index.html
app.UseStaticFiles();
app.UseWebSockets();

// Маршрут для WebSocket соединений
app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var ws = await context.WebSockets.AcceptWebSocketAsync();

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var clientPort = context.Connection.RemotePort;
        var connectionId = $"{clientIp}:{clientPort}_{Guid.NewGuid():N}";

        var clientInfo = new ClientInfo
        {
            Socket = ws,
            ConnectionId = connectionId,
            IP = clientIp,
            Port = clientPort,
            ConnectionTime = DateTime.Now,
            LastActivityTime = DateTime.Now
        };

        // Используем Task.Run вместо Thread
        _ = Task.Run(async () =>
        {
            await WebSocketServer.HandleClientAsync(clientInfo, app.Services);
        });

        WebSocketServer.LogMessage($"Запущена задача для клиента {connectionId}", "SYSTEM");

        await Task.Delay(Timeout.Infinite, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Требуется WebSocket соединение");
    }
});

// Маршрут для статистики
app.MapGet("/stats", () =>
{
    var stats = WebSocketServer.GetServerStats();
    return JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
});

// Маршрут для API статистики
app.MapGet("/api/stats", () =>
{
    var stats = WebSocketServer.GetServerStats();
    return JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
});

// Маршрут для получения информации о пользователе
app.MapGet("/api/user/{id}", async (int id) =>
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

    var user = await context.Users
        .Include(u => u.Status)
        .FirstOrDefaultAsync(u => u.Id == id);

    if (user == null)
        return Results.NotFound(new { error = "Пользователь не найден" });

    // Автоматически обновляем статус на основе реального подключения
    var isOnline = WebSocketServer.IsUserOnline(user.Id);
    if (user.StatusId != (isOnline ? 1 : 2))
    {
        user.StatusId = isOnline ? 1 : 2;
        user.DateOfLastActivity = DateTime.Now;
        await context.SaveChangesAsync();
    }

    return Results.Json(new
    {
        user.Id,
        user.Login,
        user.Name,
        user.Surname,
        user.SecondSurname,
        user.CreateDate,
        user.DateOfLastActivity,
        user.UrlAvatar,
        Status = user.Status?.Name,
        IsOnline = isOnline,
        LastSeen = WebSocketServer.GetUserLastActivity(user.Id) ?? user.DateOfLastActivity
    });
});

// Маршрут для получения информации о чате
app.MapGet("/api/chat/{id}", async (int id) =>
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

    var chat = await context.Chats
        .Include(c => c.ChatType)
        .Include(c => c.UserInChats)
            .ThenInclude(uc => uc.User)
                .ThenInclude(u => u.Status)
        .Include(c => c.UserInChats)
            .ThenInclude(uc => uc.Role)
        .FirstOrDefaultAsync(c => c.Id == id);

    if (chat == null)
        return Results.NotFound(new { error = "Чат не найден" });

    return Results.Json(new
    {
        chat.Id,
        chat.Name,
        chat.CreateDate,
        ChatType = chat.ChatType != null ? new
        {
            chat.ChatType.Id,
            chat.ChatType.Name,
            chat.ChatType.Description
        } : null,
        Users = chat.UserInChats
            .Where(uc => uc.DateRelease == null)
            .Select(uc => new
            {
                UserId = uc.UserId,
                Login = uc.User?.Login,
                Name = uc.User?.Name,
                Surname = uc.User?.Surname,
                SecondSurname = uc.User?.SecondSurname,
                Role = uc.Role?.Role,
                DateOfEntry = uc.DateOfEntry,
                UrlAvatar = uc.User?.UrlAvatar,
                Status = uc.User?.Status?.Name,
                IsOnline = WebSocketServer.IsUserOnline(uc.UserId),
                LastSeen = uc.User?.DateOfLastActivity
            }).ToList(),
        MessageCount = await context.MessageUserInChats.CountAsync(m => m.IdChat == id)
    });
});

// Запускаем UDP broadcast сервис
var serverIp = GetLocalIpAddress();

// Запускаем сервис обнаружения
WebSocketServer.StartDiscoveryService(serverIp);

// При завершении приложения останавливаем discovery
AppDomain.CurrentDomain.ProcessExit += (s, e) =>
{
    WebSocketServer.StopDiscoveryService();
    Console.WriteLine("Сервер остановлен");
};

// Запускаем сервер
try
{
    app.Run("http://*:812");
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка запуска сервера: {ex.Message}");
}

// Метод для получения локального IP
static string GetLocalIpAddress()
{
    try
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(ip) &&
                !ip.ToString().StartsWith("169.254."))
            {
                return ip.ToString();
            }
        }
    }
    catch { }

    return "127.0.0.1";
}

public class SearchResult
{
    public string Type { get; set; }
    public object Data { get; set; }
    public double Similarity { get; set; }
}

public class ChatSearchData
{
    public Chat Chat { get; set; }
}

public class UserSearchData
{
    public User User { get; set; }
}

// Статический класс сервера
public static class WebSocketServer
{
    public static readonly ConcurrentDictionary<string, ClientInfo> ConnectedClients = new();
    public static readonly ConcurrentDictionary<int, string> UserConnectionMap = new();
    public static readonly ConcurrentDictionary<int, HashSet<int>> UserUnreadMessages = new();

    // Время старта сервера
    private static readonly DateTime ServerStartTime = DateTime.Now;

    // UDP discovery
    private static UdpClient _udpBroadcaster;
    private static System.Timers.Timer _discoveryTimer;
    private const int DiscoveryPort = 8124;
    private static string _serverIp = "";

    private static readonly object LogFileLock = new object();
    private static readonly string LogFilePath = "websocket_server_log.txt";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Метод для проверки онлайн-статуса пользователя
    public static bool IsUserOnline(int userId)
    {
        if (UserConnectionMap.ContainsKey(userId))
        {
            // Проверяем, действительно ли клиент подключен
            var client = ConnectedClients.Values.FirstOrDefault(c => c.User?.Id == userId);
            return client?.Socket.State == WebSocketState.Open;
        }
        return false;
    }

    // Метод для получения последнего времени активности пользователя
    public static DateTime? GetUserLastActivity(int userId)
    {
        var client = ConnectedClients.Values.FirstOrDefault(c => c.User?.Id == userId);
        return client?.LastActivityTime;
    }

    // Метод для запуска UDP broadcast
    public static void StartDiscoveryService(string serverIp)
    {
        _serverIp = serverIp;

        try
        {
            _udpBroadcaster = new UdpClient();
            _udpBroadcaster.EnableBroadcast = true;

            _discoveryTimer = new System.Timers.Timer(3000);
            _discoveryTimer.Elapsed += (sender, e) =>
            {
                SendDiscoveryBroadcast();
            };
            _discoveryTimer.AutoReset = true;
            _discoveryTimer.Enabled = true;

            ConsoleWrite("Служба обнаружения запущена", "DISCOVERY", ConsoleColor.DarkCyan);
            SendDiscoveryBroadcast();
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка запуска обнаружения: {ex.Message}", "ERROR", ConsoleColor.Red);
        }
    }

    private static void SendDiscoveryBroadcast()
    {
        try
        {
            if (_udpBroadcaster == null) return;

            var discoveryMessage = new
            {
                Service = "MyMessenger",
                Version = "1.0",
                WsEndpoint = $"ws://{_serverIp}:812/ws",
                HttpEndpoint = $"http://{_serverIp}:812",
                ServerName = Environment.MachineName,
                Timestamp = DateTime.Now,
                ClientCount = ConnectedClients.Count
            };

            var json = JsonSerializer.Serialize(discoveryMessage);
            var data = Encoding.UTF8.GetBytes(json);

            var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            _udpBroadcaster.Send(data, data.Length, broadcastEndPoint);
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка отправки broadcast: {ex.Message}", "ERROR", ConsoleColor.Red);
        }
    }

    // Метод для остановки discovery
    public static void StopDiscoveryService()
    {
        _discoveryTimer?.Stop();
        _discoveryTimer?.Dispose();
        _udpBroadcaster?.Close();
        _udpBroadcaster?.Dispose();

        ConsoleWrite("Служба обнаружения остановлена", "DISCOVERY", ConsoleColor.DarkCyan);
    }

    // Метод для улучшенного вывода в консоль
    private static void ConsoleWrite(string message, string type = "INFO", ConsoleColor color = ConsoleColor.White)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var prefix = type switch
        {
            "AUTH" => "[AUTH]",
            "CHAT" => "[CHAT]",
            "USER" => "[USER]",
            "ERROR" => "[ERROR]",
            "SYSTEM" => "[SYS]",
            "DB" => "[DB]",
            "CONNECT" => "[CONN]",
            "DISCONNECT" => "[DISC]",
            "MESSAGE" => "[MSG]",
            "DISCOVERY" => "[DISC]",
            _ => "[INFO]"
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"{timestamp} {prefix} {message}");
        Console.ForegroundColor = originalColor;
    }

    public static async Task HandleClientAsync(ClientInfo clientInfo, IServiceProvider rootServiceProvider)
    {
        var buffer = new byte[1024 * 4];
        var authenticated = false;
        User currentUser = null;

        try
        {
            // Создаем scope внутри метода
            using var scope = rootServiceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
            var messageService = scope.ServiceProvider.GetRequiredService<MessageService>();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

            ConsoleWrite($"Новое подключение: {clientInfo.IP}:{clientInfo.Port}", "CONNECT", ConsoleColor.Cyan);

            // Шаг 1: Аутентификация/регистрация
            while (!authenticated && clientInfo.Socket.State == WebSocketState.Open)
            {
                var result = await clientInfo.Socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var jsonData = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    try
                    {
                        var authData = JsonSerializer.Deserialize<AuthRequest>(jsonData, JsonOptions);

                        if (authData == null || string.IsNullOrWhiteSpace(authData.Login) ||
                            string.IsNullOrWhiteSpace(authData.Password))
                        {
                            ConsoleWrite($"Неверные данные от {clientInfo.IP}:{clientInfo.Port}", "ERROR", ConsoleColor.Red);
                            await SendErrorResponseAsync(clientInfo.Socket, "Неверные данные пользователя");
                            continue;
                        }

                        var existingUser = await userService.GetUserByLoginAsync(authData.Login);

                        if (existingUser != null)
                        {
                            if (existingUser.Password != authData.Password)
                            {
                                ConsoleWrite($"Неверный пароль для {authData.Login}", "AUTH", ConsoleColor.Red);
                                await SendErrorResponseAsync(clientInfo.Socket, "Неверный пароль");
                                continue;
                            }

                            currentUser = existingUser;

                            ConsoleWrite($"{currentUser.Login} вошел в систему", "AUTH", ConsoleColor.Green);
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(authData.Name))
                            {
                                await SendErrorResponseAsync(clientInfo.Socket, "Имя не может быть пустым");
                                continue;
                            }

                            try
                            {
                                currentUser = new User
                                {
                                    Login = authData.Login,
                                    Password = authData.Password,
                                    Name = authData.Name,
                                    Surname = authData.Surname ?? "",
                                    SecondSurname = authData.SecondSurname ?? "",
                                    CreateDate = DateOnly.FromDateTime(DateTime.Now),
                                    DateOfLastActivity = DateTime.Now,
                                    StatusId = 1
                                };

                                currentUser = await userService.CreateUserAsync(currentUser);
                                ConsoleWrite($"Новый пользователь: {currentUser.Login} ({currentUser.Name})", "USER", ConsoleColor.Green);
                            }
                            catch (Exception dbEx)
                            {
                                ConsoleWrite($"Ошибка регистрации {authData.Login}: {dbEx.Message}", "ERROR", ConsoleColor.Red);
                                await SendErrorResponseAsync(clientInfo.Socket, dbEx.Message);
                                continue;
                            }
                        }

                        bool userAlreadyConnected = ConnectedClients.Values
                            .Any(c => c.User?.Login?.Equals(currentUser.Login, StringComparison.OrdinalIgnoreCase) == true);

                        if (userAlreadyConnected)
                        {
                            ConsoleWrite($"{currentUser.Login} уже в сети", "ERROR", ConsoleColor.Yellow);
                            await SendErrorResponseAsync(clientInfo.Socket, $"Пользователь '{currentUser.Login}' уже в сети");
                            continue;
                        }

                        currentUser.StatusId = 1; // Online
                        currentUser.DateOfLastActivity = DateTime.Now;
                        await context.SaveChangesAsync();

                        clientInfo.Nickname = currentUser.Login;
                        clientInfo.User = currentUser;
                        authenticated = true;

                        ConnectedClients.TryAdd(clientInfo.ConnectionId, clientInfo);
                        UserConnectionMap.TryAdd(currentUser.Id, clientInfo.ConnectionId);

                        await SendUserInfoAsync(clientInfo.Socket, currentUser,
                            existingUser != null ? "Успешная авторизация" : "Успешная регистрация");

                        ConsoleWrite($"{currentUser.Name} {currentUser.Surname} подключился", "CONNECT", ConsoleColor.Green);
                        ConsoleWrite($"Всего пользователей: {ConnectedClients.Count}", "SYSTEM", ConsoleColor.Blue);

                        await BroadcastSystemMessageAsync($"{currentUser.Name} {currentUser.Surname} присоединился к чату!");
                        await BroadcastUserStatusChangeAsync(currentUser.Id, true);

                        // После LoadUserChatsAsync
                        await LoadUserChatsAsync(clientInfo, userService, chatService);

                        // Отправляем уведомления о непрочитанных сообщениях
                        await SendUnreadNotificationsAsync(clientInfo, rootServiceProvider);

                    }
                    catch (JsonException)
                    {
                        ConsoleWrite($"Неверный JSON от {clientInfo.IP}", "ERROR", ConsoleColor.Red);
                        await SendErrorResponseAsync(clientInfo.Socket, "Неверный JSON формат");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        ConsoleWrite($"Ошибка: {ex.Message}", "ERROR", ConsoleColor.Red);
                        await SendErrorResponseAsync(clientInfo.Socket, ex.Message);
                        continue;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    ConsoleWrite($"Закрыто до аутентификации: {clientInfo.IP}", "DISCONNECT", ConsoleColor.DarkGray);
                    break;
                }
            }

            // Шаг 2: Обработка сообщений после аутентификации
            if (authenticated && currentUser != null)
            {
                ConsoleWrite($"{currentUser.Login} готов к общению", "SYSTEM", ConsoleColor.DarkGreen);

                // Запускаем таймер для heartbeat
                var heartbeatTimer = new System.Timers.Timer(30000); // 30 секунд
                heartbeatTimer.Elapsed += async (sender, e) =>
                {
                    if (clientInfo.Socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            // Отправляем ping для поддержания соединения
                            var pingMessage = new
                            {
                                Type = "PING",
                                Timestamp = DateTime.Now
                            };
                            var pingJson = JsonSerializer.Serialize(pingMessage, JsonOptions);
                            await SendResponseAsync(clientInfo.Socket, pingJson);
                        }
                        catch { }
                    }
                    else
                    {
                        heartbeatTimer.Stop();
                        heartbeatTimer.Dispose();
                    }
                };
                heartbeatTimer.AutoReset = true;
                heartbeatTimer.Enabled = true;

                while (clientInfo.Socket.State == WebSocketState.Open)
                {
                    var result = await clientInfo.Socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var messageData = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        clientInfo.LastActivityTime = DateTime.Now;

                        try
                        {
                            await ProcessClientMessageAsync(clientInfo, messageData, userService, chatService, messageService, rootServiceProvider);
                        }
                        catch (Exception ex)
                        {
                            ConsoleWrite($"Ошибка у {currentUser.Login}: {ex.Message}", "ERROR", ConsoleColor.Red);
                            await SendErrorResponseAsync(clientInfo.Socket, ex.Message);
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
        }
        catch (WebSocketException ex)
        {
            ConsoleWrite($"WebSocket ошибка: {ex.Message}", "ERROR", ConsoleColor.Red);
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Общая ошибка: {ex.Message}", "ERROR", ConsoleColor.Red);
        }
        finally
        {
            ConnectedClients.TryRemove(clientInfo.ConnectionId, out _);

            if (clientInfo.User != null)
            {
                UserConnectionMap.TryRemove(clientInfo.User.Id, out _);
                ConsoleWrite($"{clientInfo.User.Name} отключился", "DISCONNECT", ConsoleColor.DarkGray);
                ConsoleWrite($"Осталось пользователей: {ConnectedClients.Count}", "SYSTEM", ConsoleColor.Blue);

                // Обновляем время последней активности пользователя
                using var scope = rootServiceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();
                var userInDb = await context.Users.FindAsync(clientInfo.User.Id);
                if (userInDb != null)
                {
                    userInDb.StatusId = 2;
                    userInDb.DateOfLastActivity = DateTime.Now;
                    await context.SaveChangesAsync();
                }

                await BroadcastSystemMessageAsync($"{clientInfo.User.Name} {clientInfo.User.Surname} покинул чат");
                await BroadcastUserStatusChangeAsync(clientInfo.User.Id, false);
            }
            else
            {
                ConsoleWrite($"Аноним отключился: {clientInfo.IP}", "DISCONNECT", ConsoleColor.DarkGray);
            }

            if (clientInfo.Socket.State == WebSocketState.Open)
            {
                await clientInfo.Socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Соединение закрыто",
                    CancellationToken.None);
            }

            clientInfo.Socket.Dispose();
        }
    }



    private static async Task LoadUserChatsAsync(ClientInfo clientInfo, UserService userService, ChatService chatService)
    {
        var chats = await chatService.GetUserChatsAsync(clientInfo.User.Id);
        clientInfo.JoinedChats = chats.Select(c => c.Id).ToList();

        await SendChatsListAsync(clientInfo, chats);
    }

    private static async Task ProcessClientMessageAsync(ClientInfo clientInfo, string messageData, UserService userService, ChatService chatService, MessageService messageService, IServiceProvider serviceProvider)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(messageData);
            if (jsonDoc.RootElement.TryGetProperty("type", out var typeElement))
            {
                var messageType = typeElement.GetString();

                switch (messageType)
                {
                    case WebSocketMessageTypes.TEXT_MESSAGE:
                        await HandleTextMessage(clientInfo, jsonDoc, messageService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.CREATE_CHAT_AND_INVITE:
                        await HandleCreateChatAndInvite(clientInfo, jsonDoc, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.SELECT_CHAT:
                        await HandleSelectChat(clientInfo, jsonDoc, serviceProvider);
                        break;

                    case WebSocketMessageTypes.GET_CHATS:
                        await HandleGetChats(clientInfo, chatService);
                        break;

                    case WebSocketMessageTypes.GET_HISTORY:
                        await HandleGetHistory(clientInfo, messageService);
                        break;

                    case WebSocketMessageTypes.GET_USERS:
                        await HandleGetUsersAsync(clientInfo, userService);
                        break;

                    case WebSocketMessageTypes.AUTH:
                        await HandleAuthCommand(clientInfo, jsonDoc, userService, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.CREATE_CHAT:
                        await HandleCreateChat(clientInfo, jsonDoc, chatService);
                        break;

                    case WebSocketMessageTypes.CREATE_CHAT_WITH_USER:
                        await HandleCreateChatWithUser(clientInfo, jsonDoc, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.CREATE_PRIVATE_CHAT:
                        await HandleCreatePrivateChat(clientInfo, jsonDoc, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.JOIN_CHAT:
                        await HandleJoinChat(clientInfo, jsonDoc, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.LEAVE_CHAT:
                        await HandleLeaveChat(clientInfo, jsonDoc, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.UPDATE_PROFILE:
                        await HandleUpdateProfile(clientInfo, jsonDoc, userService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.MARK_AS_READ:
                        await HandleMarkAsRead(clientInfo, jsonDoc, messageService);
                        break;

                    case WebSocketMessageTypes.GET_CHAT_USERS:
                        await HandleGetChatUsers(clientInfo, jsonDoc, chatService);
                        break;

                    case WebSocketMessageTypes.UPDATE_USER_ROLE:
                        await HandleUpdateUserRole(clientInfo, jsonDoc, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.UPLOAD_AVATAR:
                        await HandleUploadAvatar(clientInfo, jsonDoc, userService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.UPDATE_STATUS:
                        await HandleUpdateStatus(clientInfo, jsonDoc, userService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.MARK_CHAT_AS_READ:
                        await HandleMarkChatAsRead(clientInfo, jsonDoc, messageService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.MARK_MULTIPLE_READ:
                        await HandleMarkMultipleMessagesRead(clientInfo, jsonDoc, messageService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.SEARCH_CHATS:
                        await HandleSearchChats(clientInfo, jsonDoc, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.SEARCH_USERS:
                        await HandleSearchUsers(clientInfo, jsonDoc, userService);
                        break;

                    case WebSocketMessageTypes.GLOBAL_SEARCH:
                        await HandleGlobalSearch(clientInfo, jsonDoc, userService, chatService, serviceProvider);
                        break;

                    case WebSocketMessageTypes.GET_UNREAD_SUMMARY:
                        await HandleGetUnreadSummaryAsync(clientInfo, serviceProvider);
                        break;

                    default:
                        ConsoleWrite($"Неизвестный тип от {clientInfo.Nickname}: {messageType}", "ERROR", ConsoleColor.Yellow);
                        await SendErrorResponseAsync(clientInfo.Socket, $"Неизвестный тип сообщения: {messageType}");
                        break;
                }
            }
            else
            {
                await HandleSimpleTextMessage(clientInfo, messageData, messageService, serviceProvider);
            }
        }
        catch (JsonException)
        {
            await HandleSimpleTextMessage(clientInfo, messageData, messageService, serviceProvider);
        }
    }

    private static async Task HandleCreateChatAndInvite(ClientInfo clientInfo, JsonDocument jsonDoc,
        ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatName = root.GetProperty("chatName").GetString();
        var chatTypeId = root.GetProperty("chatTypeId").GetInt32();
        var invitedUserId = root.GetProperty("invitedUserId").GetInt32();

        ConsoleWrite($"{clientInfo.Nickname} создает чат '{chatName}' (тип: {chatTypeId}) с пользователем {invitedUserId}",
            "CHAT", ConsoleColor.Magenta);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            // Проверяем существование приглашенного пользователя
            var invitedUser = await context.Users.FindAsync(invitedUserId);
            if (invitedUser == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Приглашенный пользователь не найден");
                return;
            }

            // ВАЖНО: Проверяем, не существует ли уже приватный чат между этими пользователями
            if (chatTypeId == 2) // Приватный чат
            {
                var existingPrivateChat = await context.Chats
                    .Where(c => c.ChatTypeId == 2)
                    .Join(context.UserInChats, c => c.Id, uc => uc.ChatId, (c, uc) => new { Chat = c, UserInChat = uc })
                    .Where(x => x.UserInChat.UserId == clientInfo.User.Id || x.UserInChat.UserId == invitedUserId)
                    .GroupBy(x => x.Chat.Id)
                    .Where(g => g.Count() == 2)
                    .Select(g => g.First().Chat)
                    .FirstOrDefaultAsync();

                if (existingPrivateChat != null)
                {
                    ConsoleWrite($"Приватный чат уже существует: {existingPrivateChat.Name}", "CHAT", ConsoleColor.Yellow);

                    var response = new
                    {
                        Type = ResponseMessageTypes.CHAT_SELECTED,
                        ChatId = existingPrivateChat.Id,
                        ChatName = existingPrivateChat.Name,
                        Message = "Приватный чат уже существует"
                    };

                    await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
                    return;
                }
            }

            // 1. Создаем чат через сервис
            Chat chat;
            try
            {
                ConsoleWrite($"Создаю чат '{chatName}' типа {chatTypeId}", "DB", ConsoleColor.DarkGray);
                chat = await chatService.CreateChatAsync(chatName, chatTypeId, clientInfo.User.Id);
                ConsoleWrite($"Чат создан: ID={chat.Id}", "DB", ConsoleColor.DarkGray);
            }
            catch (Exception ex)
            {
                ConsoleWrite($"Ошибка при создании чата: {ex.Message}", "ERROR", ConsoleColor.Red);
                if (ex.InnerException != null)
                {
                    ConsoleWrite($"Внутренняя ошибка: {ex.InnerException.Message}", "ERROR", ConsoleColor.Red);
                }
                await SendErrorResponseAsync(clientInfo.Socket, "Не удалось создать чат. Проверьте название.");
                return;
            }

            // 2. Если это групповой чат, добавляем приглашенного пользователя
            if (chatTypeId == 1) // Групповой чат
            {
                try
                {
                    ConsoleWrite($"Добавляю пользователя {invitedUserId} в чат {chat.Id}", "DB", ConsoleColor.DarkGray);
                    await chatService.AddUserToChatAsync(invitedUserId, chat.Id);
                    ConsoleWrite($"Пользователь добавлен", "DB", ConsoleColor.DarkGray);
                }
                catch (Exception ex)
                {
                    ConsoleWrite($"Ошибка при добавлении пользователя в чат: {ex.Message}", "ERROR", ConsoleColor.Red);
                    // Не прерываем выполнение, просто логируем ошибку
                }
            }

            // 3. Обновляем список чатов для создателя
            if (!clientInfo.JoinedChats.Contains(chat.Id))
            {
                clientInfo.JoinedChats.Add(chat.Id);
            }

            ConsoleWrite($"{clientInfo.Nickname} создал чат '{chat.Name}' (ID: {chat.Id})",
                "CHAT", ConsoleColor.Green);

            // 4. Отправляем ответ создателю
            var successResponse = new
            {
                Type = ResponseMessageTypes.CHAT_CREATED_AND_INVITED,
                Chat = new
                {
                    chat.Id,
                    chat.Name,
                    chat.ChatTypeId,
                    chat.CreateDate
                },
                InvitedUser = chatTypeId == 1 ? new
                {
                    invitedUser.Id,
                    invitedUser.Login,
                    invitedUser.Name,
                    invitedUser.Surname
                } : null,
                Message = chatTypeId == 2
                    ? $"Приватный чат с {invitedUser.Name} создан"
                    : $"Групповой чат '{chatName}' создан с участием {invitedUser.Name}"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(successResponse, JsonOptions));

            // 5. Если это групповой чат, уведомляем приглашенного пользователя
            if (chatTypeId == 1)
            {
                var targetClient = ConnectedClients.Values.FirstOrDefault(c => c.User?.Id == invitedUserId);
                if (targetClient != null)
                {
                    if (!targetClient.JoinedChats.Contains(chat.Id))
                    {
                        targetClient.JoinedChats.Add(chat.Id);
                    }

                    // Отправляем обновленный список чатов
                    await SendChatsListAsync(targetClient, new List<Chat> { chat });

                    var notification = new
                    {
                        Type = ResponseMessageTypes.NEW_MESSAGE,
                        Chat = new
                        {
                            chat.Id,
                            chat.Name,
                            chat.ChatTypeId,
                            chat.CreateDate
                        },
                        Initiator = new
                        {
                            clientInfo.User.Id,
                            clientInfo.User.Login,
                            clientInfo.User.Name,
                            clientInfo.User.Surname
                        },
                        Message = $"{clientInfo.User.Name} пригласил вас в чат '{chatName}'"
                    };

                    await SendResponseAsync(targetClient.Socket,
                        JsonSerializer.Serialize(notification, JsonOptions));

                    ConsoleWrite($"Уведомление отправлено пользователю {invitedUser.Login}",
                        "NOTIFY", ConsoleColor.DarkCyan);
                }
                else
                {
                    ConsoleWrite($"Пользователь {invitedUser.Login} не в сети",
                        "NOTIFY", ConsoleColor.DarkGray);
                }
            }

            // 6. Также отправляем обновленный список чатов создателю
            var creatorChats = await chatService.GetUserChatsAsync(clientInfo.User.Id);
            await SendChatsListAsync(clientInfo, creatorChats);

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка создания чата с приглашением: {ex.Message}\nStackTrace: {ex.StackTrace}",
                "ERROR", ConsoleColor.Red);

            // Более информативное сообщение об ошибке
            string errorMessage = ex.InnerException != null
                ? $"Ошибка БД: {ex.InnerException.Message}"
                : "Не удалось создать чат";

            await SendErrorResponseAsync(clientInfo.Socket, errorMessage);
        }
    }

    private static async Task HandleCreateChatWithUser(ClientInfo clientInfo, JsonDocument jsonDoc,
    ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatName = root.GetProperty("name").GetString();
        var chatTypeId = root.GetProperty("chatTypeId").GetInt32();
        var invitedUserId = root.GetProperty("invitedUserId").GetInt32();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            // 1. Создаем чат
            var chat = await chatService.CreateChatAsync(chatName, chatTypeId, clientInfo.User.Id);

            // 2. Добавляем приглашенного пользователя
            var invitedUser = await context.Users.FindAsync(invitedUserId);
            if (invitedUser == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Приглашенный пользователь не найден");
                return;
            }

            await chatService.AddUserToChatAsync(invitedUserId, chat.Id);

            // 3. Обновляем списки чатов для обоих пользователей
            if (!clientInfo.JoinedChats.Contains(chat.Id))
            {
                clientInfo.JoinedChats.Add(chat.Id);
            }

            ConsoleWrite($"{clientInfo.Nickname} создал чат '{chatName}' с {invitedUser.Login}",
                "CHAT", ConsoleColor.Magenta);

            // 4. Отправляем ответ создателю
            var response = new
            {
                Type = ResponseMessageTypes.CHAT_CREATED_WITH_USER,
                Chat = new
                {
                    chat.Id,
                    chat.Name,
                    chat.ChatTypeId,
                    chat.CreateDate
                },
                InvitedUser = new
                {
                    invitedUser.Id,
                    invitedUser.Login,
                    invitedUser.Name,
                    invitedUser.Surname
                },
                Message = $"Чат '{chatName}' создан с участием {invitedUser.Name}"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

            // 5. Уведомляем приглашенного пользователя (если онлайн)
            var targetClient = ConnectedClients.Values.FirstOrDefault(c => c.User?.Id == invitedUserId);
            if (targetClient != null)
            {
                targetClient.JoinedChats.Add(chat.Id);
                await SendChatsListAsync(targetClient, new List<Chat> { chat });

                var notification = new
                {
                    Type = ResponseMessageTypes.NEW_MESSAGE,
                    Chat = new
                    {
                        chat.Id,
                        chat.Name,
                        chat.ChatTypeId,
                        chat.CreateDate
                    },
                    Initiator = new
                    {
                        clientInfo.User.Id,
                        clientInfo.User.Login,
                        clientInfo.User.Name,
                        clientInfo.User.Surname
                    },
                    Message = $"{clientInfo.User.Name} пригласил вас в чат '{chatName}'"
                };

                await SendResponseAsync(targetClient.Socket,
                    JsonSerializer.Serialize(notification, JsonOptions));
            }
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка создания чата с пользователем: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось создать чат с пользователем");
        }
    }

    private static async Task HandleAuthCommand(ClientInfo clientInfo, JsonDocument jsonDoc, UserService userService, ChatService chatService, IServiceProvider serviceProvider)
    {
        var root = jsonDoc.RootElement;
        var login = root.GetProperty("login").GetString();
        var password = root.GetProperty("password").GetString();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        var existingUser = await userService.GetUserByLoginAsync(login);
        if (existingUser == null)
        {
            ConsoleWrite($"Пользователь не найден: {login}", "AUTH", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Пользователь не найден");
            return;
        }

        if (existingUser.Password != password)
        {
            ConsoleWrite($"Неверный пароль для {login}", "AUTH", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Неверный пароль");
            return;
        }

        bool userAlreadyConnected = ConnectedClients.Values
            .Any(c => c.User?.Login?.Equals(login, StringComparison.OrdinalIgnoreCase) == true);

        if (userAlreadyConnected)
        {
            ConsoleWrite($"{login} уже в сети", "ERROR", ConsoleColor.Yellow);
            await SendErrorResponseAsync(clientInfo.Socket, $"Пользователь '{login}' уже в сети");
            return;
        }

        existingUser.DateOfLastActivity = DateTime.Now;
        await context.SaveChangesAsync();

        clientInfo.Nickname = existingUser.Login;
        clientInfo.User = existingUser;

        ConnectedClients.TryAdd(clientInfo.ConnectionId, clientInfo);
        UserConnectionMap.TryAdd(existingUser.Id, clientInfo.ConnectionId);

        await SendUserInfoAsync(clientInfo.Socket, existingUser, "Успешная авторизация");

        ConsoleWrite($"{existingUser.Login} авторизован", "AUTH", ConsoleColor.Green);
        await BroadcastSystemMessageAsync($"{existingUser.Name} {existingUser.Surname} присоединился к чату!");
        await BroadcastUserStatusChangeAsync(existingUser.Id, true);

        await LoadUserChatsAsync(clientInfo, userService, chatService);
    }

    private static async Task HandleCreateChat(ClientInfo clientInfo, JsonDocument jsonDoc, ChatService chatService)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatName = root.GetProperty("name").GetString();
        var chatTypeId = root.GetProperty("chatTypeId").GetInt32();

        try
        {
            var chat = await chatService.CreateChatAsync(chatName, chatTypeId, clientInfo.User.Id);

            clientInfo.JoinedChats.Add(chat.Id);

            ConsoleWrite($"{clientInfo.Nickname} создал чат: {chatName}", "CHAT", ConsoleColor.Magenta);

            var response = new
            {
                Type = "SEARCH_RESULTS", // или "GLOBAL_SEARCH_RESULTS"
                Chat = new
                {
                    chat.Id,
                    chat.Name,
                    chat.ChatTypeId,
                    chat.CreateDate,
                    IsCreator = true,
                    Role = "Administrator"
                },
                Message = $"Чат '{chatName}' успешно создан"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
            await SendChatsListAsync(clientInfo, new List<Chat> { chat });

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка создания чата: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось создать чат");
        }
    }



    private static async Task HandleCreatePrivateChat(ClientInfo clientInfo, JsonDocument jsonDoc, ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var targetUserId = root.GetProperty("targetUserId").GetInt32();

        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

            var targetUser = await context.Users.FindAsync(targetUserId);
            if (targetUser == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Пользователь не найден");
                return;
            }

            // Проверяем, существует ли уже приватный чат
            var existingChat = await context.Chats
                .Where(c => c.ChatTypeId == 2)
                .Join(context.UserInChats, c => c.Id, uc => uc.ChatId, (c, uc) => new { Chat = c, UserInChat = uc })
                .Where(x => x.UserInChat.UserId == clientInfo.User.Id || x.UserInChat.UserId == targetUserId)
                .GroupBy(x => x.Chat.Id)
                .Where(g => g.Count() == 2)
                .Select(g => g.First().Chat)
                .FirstOrDefaultAsync();

            if (existingChat != null)
            {
                clientInfo.CurrentChatId = existingChat.Id;
                var response = new
                {
                    Type = ResponseMessageTypes.CHAT_SELECTED,
                    ChatId = existingChat.Id,
                    ChatName = existingChat.Name,
                    Message = $"Приватный чат с {targetUser.Name} уже существует"
                };
                await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
                return;
            }

            var chat = await chatService.CreatePrivateChatAsync(clientInfo.User.Id, targetUserId);

            clientInfo.JoinedChats.Add(chat.Id);

            ConsoleWrite($"{clientInfo.Nickname} создал приватный чат с {targetUser.Login}", "CHAT", ConsoleColor.Magenta);

            var successResponse = new
            {
                Type = ResponseMessageTypes.PRIVATE_CHAT_CREATED,
                Chat = new
                {
                    chat.Id,
                    chat.Name,
                    chat.ChatTypeId,
                    chat.CreateDate
                },
                TargetUser = new
                {
                    targetUser.Id,
                    targetUser.Login,
                    targetUser.Name,
                    targetUser.Surname
                },
                Message = $"Приватный чат с {targetUser.Name} создан"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(successResponse, JsonOptions));

            var targetClient = ConnectedClients.Values.FirstOrDefault(c => c.User?.Id == targetUserId);
            if (targetClient != null)
            {
                targetClient.JoinedChats.Add(chat.Id);
                await SendChatsListAsync(targetClient, new List<Chat> { chat });

                var notification = new
                {
                    Type = ResponseMessageTypes.NEW_MESSAGE,
                    Chat = new
                    {
                        chat.Id,
                        chat.Name,
                        chat.ChatTypeId,
                        chat.CreateDate
                    },
                    Initiator = new
                    {
                        clientInfo.User.Id,
                        clientInfo.User.Login,
                        clientInfo.User.Name,
                        clientInfo.User.Surname
                    }
                };

                await SendResponseAsync(targetClient.Socket, JsonSerializer.Serialize(notification, JsonOptions));
            }

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка создания приватного чата: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось создать приватный чат");
        }
    }

    private static async Task HandleJoinChat(ClientInfo clientInfo, JsonDocument jsonDoc,
    ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatId = root.GetProperty("chatId").GetInt32();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            var chat = await context.Chats.FindAsync(chatId);
            if (chat == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Чат не найден");
                return;
            }

            // Проверяем, был ли пользователь ранее в этом чате
            var existingMembership = await context.UserInChats
                .FirstOrDefaultAsync(uc => uc.UserId == clientInfo.User.Id && uc.ChatId == chatId);

            if (existingMembership != null)
            {
                // Если пользователь уже в чате (не покидал его)
                if (existingMembership.DateRelease == null)
                {
                    await SendErrorResponseAsync(clientInfo.Socket, "Вы уже состоите в этом чате");
                    return;
                }

                // Если пользователь покидал чат, обновляем запись
                ConsoleWrite($"{clientInfo.Nickname} возвращается в чат {chat.Name}", "CHAT", ConsoleColor.Magenta);

                existingMembership.DateRelease = null; // Убираем дату выхода
                existingMembership.DateOfEntry = DateTime.Now; // Обновляем дату входа

                await context.SaveChangesAsync();
            }
            else
            {
                // Если пользователя никогда не было в этом чате, создаем новую запись
                await chatService.AddUserToChatAsync(clientInfo.User.Id, chatId);
            }

            // Обновляем список чатов клиента
            if (!clientInfo.JoinedChats.Contains(chatId))
            {
                clientInfo.JoinedChats.Add(chatId);
            }

            ConsoleWrite($"{clientInfo.Nickname} присоединился к чату {chat.Name}", "CHAT", ConsoleColor.Magenta);

            var response = new
            {
                Type = ResponseMessageTypes.JOINED_CHAT,
                ChatId = chatId,
                ChatName = chat.Name,
                Message = $"Вы присоединились к чату '{chat.Name}'",
                IsReturning = existingMembership != null
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
            await BroadcastSystemMessageAsync($"{clientInfo.User.Name} присоединился к чату", chatId);

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка присоединения к чату: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось присоединиться к чату");
        }
    }

    private static async Task HandleLeaveChat(ClientInfo clientInfo, JsonDocument jsonDoc, ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatId = root.GetProperty("chatId").GetInt32();

        try
        {
            var success = await chatService.RemoveUserFromChatAsync(clientInfo.User.Id, chatId);

            if (!success)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Вы не состоите в этом чате");
                return;
            }

            clientInfo.JoinedChats.Remove(chatId);

            if (clientInfo.CurrentChatId == chatId)
            {
                clientInfo.CurrentChatId = null;
            }

            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();
            var chat = await context.Chats.FindAsync(chatId);

            ConsoleWrite($"{clientInfo.Nickname} покинул чат {chat?.Name}", "CHAT", ConsoleColor.Magenta);

            var response = new
            {
                Type = ResponseMessageTypes.LEFT_CHAT,
                ChatId = chatId,
                Message = $"Вы покинули чат '{chat?.Name}'"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
            await BroadcastSystemMessageAsync($"{clientInfo.User.Name} покинул чат", chatId);

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка выхода из чата: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось покинуть чат");
        }
    }

    private static async Task HandleUpdateProfile(ClientInfo clientInfo, JsonDocument jsonDoc,
    UserService userService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            var user = await context.Users.FindAsync(clientInfo.User.Id);
            if (user == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Пользователь не найден");
                return;
            }

            if (root.TryGetProperty("name", out var nameElement))
                user.Name = nameElement.GetString();

            if (root.TryGetProperty("surname", out var surnameElement))
                user.Surname = surnameElement.GetString();

            if (root.TryGetProperty("secondSurname", out var secondSurnameElement))
                user.SecondSurname = secondSurnameElement.GetString();

            if (root.TryGetProperty("urlAvatar", out var avatarElement))
                user.UrlAvatar = avatarElement.GetString();

            if (root.TryGetProperty("statusId", out var statusElement))
                user.StatusId = statusElement.GetInt32();

            user.DateOfLastActivity = DateTime.Now;
            await context.SaveChangesAsync();

            clientInfo.User = user;

            ConsoleWrite($"{user.Login} обновил профиль", "USER", ConsoleColor.Cyan);

            var response = new
            {
                Type = ResponseMessageTypes.PROFILE_UPDATED,
                User = new
                {
                    user.Id,
                    user.Login,
                    user.Name,
                    user.Surname,
                    user.SecondSurname,
                    user.CreateDate,
                    user.UrlAvatar,
                    user.StatusId,
                    user.DateOfLastActivity
                },
                Message = "Профиль успешно обновлен"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка обновления профиля: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось обновить профиль");
        }
    }

    private static async Task HandleMarkAsRead(ClientInfo clientInfo, JsonDocument jsonDoc, MessageService messageService)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var messageId = root.GetProperty("messageId").GetInt32();

        try
        {
            var message = await messageService.MarkMessageAsReadAsync(messageId, clientInfo.User.Id);
            if (message == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Сообщение не найдено");
                return;
            }

            ConsoleWrite($"{clientInfo.Nickname} отметил сообщение {messageId} как прочитанное", "MESSAGE", ConsoleColor.DarkGray);

            // Подтверждение для того, кто отметил сообщение
            var response = new
            {
                Type = ResponseMessageTypes.MESSAGE_READ,
                MessageId = messageId,
                Message = "Сообщение отмечено как прочитанное",
                ChatId = message.IdChat,
                ReadAt = DateTime.Now
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

            // Уведомление для автора сообщения, если он онлайн и это не тот же самый пользователь
            if (message.IdUser != clientInfo.User.Id && IsUserOnline(message.IdUser))
            {
                var authorNotification = new
                {
                    Type = ResponseMessageTypes.MESSAGE_READ_CONFIRMATION,
                    MessageId = messageId,
                    ChatId = message.IdChat,
                    ReadByUserId = clientInfo.User.Id,
                    ReadByUserName = $"{clientInfo.User.Name} {clientInfo.User.Surname}".Trim(),
                    ReadByUserLogin = clientInfo.User.Login,
                    MessageText = message.IdMessageNavigation?.Data?.Length > 50
                        ? message.IdMessageNavigation.Data.Substring(0, 50) + "..."
                        : message.IdMessageNavigation?.Data,
                    OriginalMessageDate = message.IdMessageNavigation?.DateAndTime,
                    ReadAt = DateTime.Now,
                    Timestamp = DateTime.Now
                };

                // Отправляем уведомление автору сообщения
                if (UserConnectionMap.TryGetValue(message.IdUser, out var authorConnectionId))
                {
                    if (ConnectedClients.TryGetValue(authorConnectionId, out var authorClient) &&
                        authorClient.Socket.State == WebSocketState.Open)
                    {
                        await SendResponseAsync(authorClient.Socket,
                            JsonSerializer.Serialize(authorNotification, JsonOptions));

                        ConsoleWrite($"Автору {message.IdUserNavigation?.Login} отправлено уведомление о прочтении сообщения {messageId}",
                            "MESSAGE", ConsoleColor.DarkCyan);
                    }
                }
            }
            else if (message.IdUser != clientInfo.User.Id)
            {
                ConsoleWrite($"Автор сообщения {messageId} не в сети, уведомление не отправлено",
                    "MESSAGE", ConsoleColor.DarkGray);
            }

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка отметки сообщения: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось отметить сообщение как прочитанное");
        }
    }

    private static async Task HandleGetChatUsers(ClientInfo clientInfo, JsonDocument jsonDoc, ChatService chatService)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatId = root.GetProperty("chatId").GetInt32();

        try
        {
            var chatUsers = await chatService.GetChatUsersAsync(chatId);

            var response = new
            {
                Type = ResponseMessageTypes.CHAT_USERS,
                ChatId = chatId,
                Users = chatUsers.Select(uc => new
                {
                    UserId = uc.UserId,
                    Login = uc.User?.Login,
                    Name = uc.User?.Name,
                    Surname = uc.User?.Surname,
                    SecondSurname = uc.User?.SecondSurname,
                    UrlAvatar = uc.User?.UrlAvatar,
                    Role = uc.Role?.Role,
                    DateOfEntry = uc.DateOfEntry,
                    Status = uc.User?.Status?.Name,
                    IsOnline = IsUserOnline(uc.UserId),
                    LastSeen = uc.User?.DateOfLastActivity
                })
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
            ConsoleWrite($"{clientInfo.Nickname} запросил список пользователей чата {chatId}", "CHAT", ConsoleColor.DarkCyan);

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка получения пользователей чата: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось получить список пользователей чата");
        }
    }

    private static async Task HandleUpdateUserRole(ClientInfo clientInfo, JsonDocument jsonDoc,
        ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var targetUserId = root.GetProperty("targetUserId").GetInt32();
        var chatId = root.GetProperty("chatId").GetInt32();
        var roleName = root.GetProperty("role").GetString();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            // Проверяем, есть ли у текущего пользователя права администратора в этом чате
            var currentUserRole = await context.UserInChats
                .Include(uc => uc.Role)
                .FirstOrDefaultAsync(uc => uc.UserId == clientInfo.User.Id && uc.ChatId == chatId && uc.DateRelease == null);

            if (currentUserRole == null || currentUserRole.Role?.Role != "Administrator")
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Недостаточно прав");
                return;
            }

            var success = await chatService.UpdateUserRoleAsync(targetUserId, chatId, roleName);
            if (!success)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Не удалось изменить роль пользователя");
                return;
            }

            var targetUser = await context.Users.FindAsync(targetUserId);
            ConsoleWrite($"{clientInfo.Nickname} изменил роль {targetUser?.Login} на {roleName}", "CHAT", ConsoleColor.Magenta);

            var response = new
            {
                Type = ResponseMessageTypes.USER_ROLE_UPDATED,
                ChatId = chatId,
                TargetUserId = targetUserId,
                Role = roleName,
                Message = $"Роль пользователя изменена на '{roleName}'"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

            var targetClient = ConnectedClients.Values.FirstOrDefault(c => c.User?.Id == targetUserId);
            if (targetClient != null)
            {
                var notification = new
                {
                    Type = ResponseMessageTypes.YOUR_ROLE_UPDATED,
                    ChatId = chatId,
                    Role = roleName,
                    Message = $"Ваша роль в чате изменена на '{roleName}'"
                };

                await SendResponseAsync(targetClient.Socket, JsonSerializer.Serialize(notification, JsonOptions));
            }

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка изменения роли: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось изменить роль пользователя");
        }
    }

    private static async Task HandleUploadAvatar(ClientInfo clientInfo, JsonDocument jsonDoc,
    UserService userService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var avatarUrl = root.GetProperty("avatarUrl").GetString();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            var user = await context.Users.FindAsync(clientInfo.User.Id);
            if (user == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Пользователь не найден");
                return;
            }

            user.UrlAvatar = avatarUrl;
            user.DateOfLastActivity = DateTime.Now;
            await context.SaveChangesAsync();

            clientInfo.User = user;

            ConsoleWrite($"{user.Login} обновил аватар", "USER", ConsoleColor.Cyan);

            var response = new
            {
                Type = ResponseMessageTypes.AVATAR_UPLOADED,
                AvatarUrl = avatarUrl,
                Message = "Аватар успешно обновлен"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

            await BroadcastUserProfileUpdateAsync(user.Id, new { AvatarUrl = avatarUrl });

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка загрузки аватара: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось загрузить аватар");
        }
    }

    private static async Task HandleUpdateStatus(ClientInfo clientInfo, JsonDocument jsonDoc,
    UserService userService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var statusId = root.GetProperty("statusId").GetInt32();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            var user = await context.Users.FindAsync(clientInfo.User.Id);
            if (user == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Пользователь не найден");
                return;
            }

            var status = await context.Statuses.FindAsync(statusId);
            if (status == null)
            {
                await SendErrorResponseAsync(clientInfo.Socket, "Статус не найден");
                return;
            }

            user.StatusId = statusId;
            user.DateOfLastActivity = DateTime.Now;
            await context.SaveChangesAsync();

            clientInfo.User = user;

            ConsoleWrite($"{user.Login} изменил статус на {status.Name}", "USER", ConsoleColor.Cyan);

            var response = new
            {
                Type = ResponseMessageTypes.STATUS_UPDATED,
                StatusId = statusId,
                StatusName = status.Name,
                Message = $"Статус изменен на '{status.Name}'"
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

            await BroadcastUserProfileUpdateAsync(user.Id, new { StatusId = statusId, StatusName = status.Name });

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка изменения статуса: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось изменить статус");
        }
    }

    private static async Task HandleMarkChatAsRead(ClientInfo clientInfo, JsonDocument jsonDoc,
    MessageService messageService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatId = root.GetProperty("chatId").GetInt32();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            //  КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: отмечаем как прочитанные ТОЛЬКО чужие сообщения
            var messagesToMark = await context.MessageUserInChats
                .Include(muc => muc.IdMessageNavigation)
                .Include(muc => muc.IdUserNavigation)
                .Where(muc => muc.IdChat == chatId &&
                             muc.IdUser != clientInfo.User.Id && // ❗ НЕ свои сообщения
                             muc.IdMessageNavigation.IsRead == false) // ❗ Только непрочитанные
                .ToListAsync();

            if (!messagesToMark.Any())
            {
                // Отправляем ответ даже если нечего отмечать
                var response = new
                {
                    Type = ResponseMessageTypes.CHAT_MARKED_AS_READ,
                    ChatId = chatId,
                    Message = "Нет непрочитанных сообщений от других пользователей",
                    MarkedCount = 0,
                    ReadAt = DateTime.Now
                };

                await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
                return;
            }

            // Отмечаем сообщения как прочитанные
            foreach (var messageUserInChat in messagesToMark)
            {
                if (messageUserInChat.IdMessageNavigation != null)
                {
                    messageUserInChat.IdMessageNavigation.IsRead = true;
                }
            }

            await context.SaveChangesAsync();

            // Обновляем локальный счетчик непрочитанных для пользователя
            if (UserUnreadMessages.ContainsKey(clientInfo.User.Id))
            {
                foreach (var msg in messagesToMark)
                {
                    UserUnreadMessages[clientInfo.User.Id].Remove(msg.IdMessage);
                }
            }

            ConsoleWrite($"{clientInfo.Nickname} отметил {messagesToMark.Count} чужих сообщений в чате {chatId} как прочитанные",
                "MESSAGE", ConsoleColor.DarkGray);

            // Подтверждение для того, кто отметил сообщения
            var confirmation = new
            {
                Type = ResponseMessageTypes.CHAT_MARKED_AS_READ,
                ChatId = chatId,
                Message = "Сообщения от других пользователей отмечены как прочитанные",
                MarkedCount = messagesToMark.Count,
                ReadAt = DateTime.Now
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(confirmation, JsonOptions));

            //  Группируем сообщения по авторам для отправки уведомлений
            var messagesByAuthor = messagesToMark
                .Where(m => m.IdUser != clientInfo.User.Id)
                .GroupBy(m => m.IdUser)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Отправляем уведомления авторам
            foreach (var authorGroup in messagesByAuthor)
            {
                var authorId = authorGroup.Key;
                var authorMessages = authorGroup.Value;

                // Проверяем, онлайн ли автор
                if (IsUserOnline(authorId))
                {
                    var authorNotification = new
                    {
                        Type = ResponseMessageTypes.MESSAGES_BATCH_READ,
                        ChatId = chatId,
                        MessageIds = authorMessages.Select(m => m.IdMessage).ToList(),
                        ReadByUserId = clientInfo.User.Id,
                        ReadByUserName = $"{clientInfo.User.Name} {clientInfo.User.Surname}".Trim(),
                        ReadByUserLogin = clientInfo.User.Login,
                        ReadAt = DateTime.Now,
                        Timestamp = DateTime.Now,
                        MessageCount = authorMessages.Count
                    };

                    // Отправляем уведомление автору
                    if (UserConnectionMap.TryGetValue(authorId, out var authorConnectionId))
                    {
                        if (ConnectedClients.TryGetValue(authorConnectionId, out var authorClient) &&
                            authorClient.Socket.State == WebSocketState.Open)
                        {
                            await SendResponseAsync(authorClient.Socket,
                                JsonSerializer.Serialize(authorNotification, JsonOptions));

                            ConsoleWrite($"Автору {authorId} отправлено уведомление о прочтении {authorMessages.Count} сообщений",
                                "MESSAGE", ConsoleColor.DarkCyan);
                        }
                    }
                }
                else
                {
                    ConsoleWrite($"Автор {authorId} не в сети, уведомления о прочтении не отправлены",
                        "MESSAGE", ConsoleColor.DarkGray);
                }
            }

            // Обновляем время последнего просмотра чата
            clientInfo.ChatLastSeen[chatId] = DateTime.Now;

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка отметки чата как прочитанного: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось отметить чат как прочитанный");
        }
    }

    private static async Task BroadcastToChatParticipantsAsync(int chatId, object message,
        IServiceProvider serviceProvider, int[] excludeUserIds = null)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

            // Получаем всех участников чата
            var participants = await context.UserInChats
                .Where(uc => uc.ChatId == chatId && uc.DateRelease == null)
                .Select(uc => uc.UserId)
                .ToListAsync();

            var jsonMessage = JsonSerializer.Serialize(message, JsonOptions);
            var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

            foreach (var userId in participants)
            {
                // Пропускаем исключенных пользователей
                if (excludeUserIds != null && excludeUserIds.Contains(userId))
                    continue;

                // Если пользователь онлайн, отправляем уведомление
                if (UserConnectionMap.TryGetValue(userId, out var connectionId))
                {
                    if (ConnectedClients.TryGetValue(connectionId, out var client) &&
                        client.Socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await client.Socket.SendAsync(
                                new ArraySegment<byte>(messageBytes),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            ConsoleWrite($"Ошибка отправки уведомления пользователю {userId}: {ex.Message}",
                                "ERROR", ConsoleColor.Red);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка рассылки уведомлений чата {chatId}: {ex.Message}", "ERROR", ConsoleColor.Red);
        }
    }

    private static async Task HandleMarkMultipleMessagesRead(ClientInfo clientInfo, JsonDocument jsonDoc,
        MessageService messageService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var chatId = root.GetProperty("chatId").GetInt32();
        var messageIds = root.GetProperty("messageIds").EnumerateArray()
            .Select(x => x.GetInt32())
            .ToList();

        try
        {
            var messages = await messageService.GetMessagesWithDetailsAsync(messageIds);
            var messagesToMark = messages
                .Where(m => m.IdChat == chatId &&
                           m.IdMessageNavigation != null &&
                           !m.IdMessageNavigation.IsRead)
                .ToList();

            // Отмечаем все сообщения как прочитанные
            foreach (var message in messagesToMark)
            {
                if (message.IdMessageNavigation != null)
                {
                    message.IdMessageNavigation.IsRead = true;
                }
            }

            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();
            await context.SaveChangesAsync();

            ConsoleWrite($"{clientInfo.Nickname} отметил {messagesToMark.Count} сообщений как прочитанные в чате {chatId}",
                "MESSAGE", ConsoleColor.DarkGray);

            // Подтверждение для того, кто отметил сообщения
            var response = new
            {
                Type = ResponseMessageTypes.MULTIPLE_MESSAGES_READ,
                ChatId = chatId,
                MessageIds = messagesToMark.Select(m => m.IdMessage).ToList(),
                Count = messagesToMark.Count,
                ReadAt = DateTime.Now
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

            // Группируем сообщения по авторам
            var messagesByAuthor = messagesToMark
                .Where(m => m.IdUser != clientInfo.User.Id)
                .GroupBy(m => m.IdUser)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Отправляем уведомления авторам
            foreach (var authorGroup in messagesByAuthor)
            {
                var authorId = authorGroup.Key;
                var authorMessages = authorGroup.Value;

                if (IsUserOnline(authorId))
                {
                    var authorNotification = new
                    {
                        Type = ResponseMessageTypes.MESSAGES_BATCH_READ,
                        ChatId = chatId,
                        MessageIds = authorMessages.Select(m => m.IdMessage).ToList(),
                        ReadByUserId = clientInfo.User.Id,
                        ReadByUserName = $"{clientInfo.User.Name} {clientInfo.User.Surname}".Trim(),
                        ReadByUserLogin = clientInfo.User.Login,
                        ReadAt = DateTime.Now,
                        Timestamp = DateTime.Now,
                        MessageCount = authorMessages.Count
                    };

                    if (UserConnectionMap.TryGetValue(authorId, out var authorConnectionId))
                    {
                        if (ConnectedClients.TryGetValue(authorConnectionId, out var authorClient) &&
                            authorClient.Socket.State == WebSocketState.Open)
                        {
                            await SendResponseAsync(authorClient.Socket,
                                JsonSerializer.Serialize(authorNotification, JsonOptions));

                            ConsoleWrite($"Автору {authorId} отправлено уведомление о прочтении {authorMessages.Count} сообщений",
                                "MESSAGE", ConsoleColor.DarkCyan);
                        }
                    }
                }
                else
                {
                    ConsoleWrite($"Автор {authorId} не в сети, уведомления о прочтении не отправлены",
                        "MESSAGE", ConsoleColor.DarkGray);
                }
            }

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка отметки нескольких сообщений: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось отметить сообщения как прочитанные");
        }
    }

    private static async Task HandleSimpleTextMessage(ClientInfo clientInfo, string message,
        MessageService messageService, IServiceProvider serviceProvider)
    {
        if (!clientInfo.CurrentChatId.HasValue || clientInfo.User == null)
        {
            Console.WriteLine($"[Сервер] Клиент {clientInfo.Nickname} пытается отправить сообщение без выбранного чата");
            return;
        }

        Console.WriteLine($"[Сервер] {clientInfo.Nickname} отправляет сообщение в чат {clientInfo.CurrentChatId}: {message}");

        try
        {
            var savedMessage = await messageService.SendMessageAsync(
                clientInfo.CurrentChatId.Value,
                clientInfo.User.Id,
                message);

            if (savedMessage != null)
            {
                Console.WriteLine($"[Сервер] Сообщение сохранено в БД: ID={savedMessage.IdMessage}");

                // 🔴 ВАЖНО: загружаем связанные данные перед отправкой
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

                var fullMessage = await context.MessageUserInChats
                    .Include(m => m.IdMessageNavigation)
                    .Include(m => m.IdUserNavigation)
                    .FirstOrDefaultAsync(m => m.IdMessage == savedMessage.IdMessage && m.IdChat == savedMessage.IdChat);

                if (fullMessage != null)
                {
                    await BroadcastNewMessageToAllParticipantsAsync(fullMessage, serviceProvider);
                }
                else
                {
                    Console.WriteLine($"[Сервер] Не удалось загрузить полное сообщение ID={savedMessage.IdMessage}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Сервер] Ошибка обработки сообщения: {ex.Message}\nStackTrace: {ex.StackTrace}");
        }
    }
    private static async Task BroadcastNewMessageToAllParticipantsAsync(MessageUserInChat messageInChat, IServiceProvider serviceProvider)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

            // Получаем ВСЕХ участников чата
            var participants = await context.UserInChats
                .Where(uc => uc.ChatId == messageInChat.IdChat && uc.DateRelease == null)
                .Select(uc => uc.UserId)
                .ToListAsync();

            // 🔴 ПРОСТАЯ и понятная структура
            var messageObject = new
            {
                type = "NEW_MESSAGE",
                message = new
                {
                    idMessage = messageInChat.IdMessage,
                    idChat = messageInChat.IdChat,
                    idUser = messageInChat.IdUser,
                    message = messageInChat.IdMessageNavigation != null ? new
                    {
                        id = messageInChat.IdMessageNavigation.Id,
                        data = messageInChat.IdMessageNavigation.Data,
                        dateAndTime = messageInChat.IdMessageNavigation.DateAndTime,
                        isDeleted = messageInChat.IdMessageNavigation.IsDeleted,
                        isUpdate = messageInChat.IdMessageNavigation.IsUpdate,
                        isRead = messageInChat.IdMessageNavigation.IsRead
                    } : null,
                    user = messageInChat.IdUserNavigation != null ? new
                    {
                        id = messageInChat.IdUserNavigation.Id,
                        login = messageInChat.IdUserNavigation.Login,
                        name = messageInChat.IdUserNavigation.Name,
                        surname = messageInChat.IdUserNavigation.Surname,
                        secondSurname = messageInChat.IdUserNavigation.SecondSurname,
                        urlAvatar = messageInChat.IdUserNavigation.UrlAvatar
                    } : null
                }
            };

            var jsonMessage = JsonSerializer.Serialize(messageObject, JsonOptions);
            var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

            foreach (var participantId in participants)
            {
                // Отправляем ВСЕМ участникам, включая отправителя
                if (UserConnectionMap.TryGetValue(participantId, out var connectionId))
                {
                    if (ConnectedClients.TryGetValue(connectionId, out var client) &&
                        client.Socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await client.Socket.SendAsync(
                                new ArraySegment<byte>(messageBytes),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None);

                            Console.WriteLine($"[Сервер] Сообщение {messageInChat.IdMessage} отправлено пользователю {participantId}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Сервер] Ошибка отправки пользователю {participantId}: {ex.Message}");
                        }
                    }
                }
            }

            Console.WriteLine($"[Сервер] Сообщение {messageInChat.IdMessage} отправлено {participants.Count} участникам");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Сервер] Ошибка рассылки: {ex.Message}");
        }
    }

    private static async Task SendNewMessageNotificationAsync(ClientInfo client,
        MessageUserInChat messageInChat, IServiceProvider serviceProvider)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

            var chat = await context.Chats.FindAsync(messageInChat.IdChat);
            var sender = await context.Users.FindAsync(messageInChat.IdUser);

            var notification = new
            {
                Type = ResponseMessageTypes.NEW_CHAT_MESSAGE_NOTIFICATION,
                ChatId = messageInChat.IdChat,
                ChatName = chat?.Name ?? "Чат",
                MessageId = messageInChat.IdMessage,
                SenderId = messageInChat.IdUser,
                SenderName = sender?.Name ?? "Неизвестно",
                SenderSurname = sender?.Surname ?? "",
                MessagePreview = messageInChat.IdMessageNavigation?.Data?.Length > 30
                    ? messageInChat.IdMessageNavigation.Data.Substring(0, 30) + "..."
                    : messageInChat.IdMessageNavigation?.Data,
                Timestamp = messageInChat.IdMessageNavigation?.DateAndTime ?? DateTime.Now,
                UnreadCount = 1
            };

            await SendResponseAsync(client.Socket, JsonSerializer.Serialize(notification, JsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка отправки уведомления: {ex.Message}", "ERROR", ConsoleColor.Red);
        }
    }

    private static async Task HandleGlobalSearchAsync(ClientInfo clientInfo, JsonDocument jsonDoc,
        UserService userService, ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var searchTerm = root.GetProperty("searchTerm").GetString() ?? "";
        var threshold = root.TryGetProperty("threshold", out var thresholdElement)
            ? thresholdElement.GetDouble()
            : 0.2;

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            var results = new List<GlobalSearchResult>();

            // 1. Поиск чатов по названию (триграммы)
            var allChats = await context.Chats
                .Include(c => c.ChatType)
                .Include(c => c.UserInChats)
                .ToListAsync();

            foreach (var chat in allChats)
            {
                var similarity = CalculateTrigramSimilarity(searchTerm, chat.Name);
                if (similarity >= threshold)
                {
                    var isJoined = await context.UserInChats
                        .AnyAsync(uc => uc.ChatId == chat.Id &&
                                        uc.UserId == clientInfo.User.Id &&
                                        uc.DateRelease == null);

                    results.Add(new GlobalSearchResult
                    {
                        Type = "chat",
                        Id = chat.Id,
                        Name = chat.Name,
                        Description = chat.ChatType?.Description ?? "",
                        IsJoined = isJoined,
                        Similarity = similarity,
                        MemberCount = chat.UserInChats?.Count(uc => uc.DateRelease == null) ?? 0,
                        ChatType = chat.ChatType?.Name ?? "Групповой",
                        Chat = chat
                    });
                }
            }

            // 2. Поиск пользователей (опционально)
            var allUsers = await userService.GetAllUsersAsync();
            foreach (var user in allUsers)
            {
                var fullName = $"{user.Name} {user.Surname}".Trim();
                var similarity = CalculateTrigramSimilarity(searchTerm, fullName);

                if (similarity >= threshold && user.Id != clientInfo.User.Id)
                {
                    results.Add(new GlobalSearchResult
                    {
                        Type = "user",
                        Id = user.Id,
                        Name = fullName,
                        Description = user.Login,
                        IsJoined = false,
                        Similarity = similarity,
                        User = user
                    });
                }
            }

            // Сортировка по похожести
            results = results.OrderByDescending(r => r.Similarity).ToList();

            var response = new
            {
                Type = ResponseMessageTypes.GLOBAL_SEARCH_RESULTS,
                SearchTerm = searchTerm,
                Results = results.Select(r => new
                {
                    r.Type,
                    r.Id,
                    r.Name,
                    r.Description,
                    r.IsJoined,
                    r.Similarity,
                    r.MemberCount,
                    r.ChatType,
                    User = r.User != null ? new
                    {
                        r.User.Id,
                        r.User.Login,
                        r.User.Name,
                        r.User.Surname,
                        r.User.UrlAvatar
                    } : null,
                    Chat = r.Chat != null ? new
                    {
                        r.Chat.Id,
                        r.Chat.Name,
                        r.Chat.ChatTypeId,
                        r.Chat.CreateDate
                    } : null
                }),
                TotalResults = results.Count
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка глобального поиска: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось выполнить поиск");
        }
    }

    private static async Task HandleTextMessage(ClientInfo clientInfo, JsonDocument jsonDoc,
        MessageService messageService, IServiceProvider serviceProvider)
    {
        var root = jsonDoc.RootElement;
        var message = root.GetProperty("content").GetString();
        var chatId = root.TryGetProperty("chatId", out var chatIdElement)
            ? chatIdElement.GetInt32()
            : clientInfo.CurrentChatId;

        if (!chatId.HasValue)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Не выбран чат");
            return;
        }

        if (!clientInfo.CurrentChatId.HasValue)
        {
            clientInfo.CurrentChatId = chatId;
        }

        await HandleSimpleTextMessage(clientInfo, message, messageService, serviceProvider);
    }

    private static async Task HandleSelectChat(ClientInfo clientInfo, JsonDocument jsonDoc, IServiceProvider serviceProvider)
    {
        var chatId = jsonDoc.RootElement.GetProperty("chatId").GetInt32();

        if (!clientInfo.JoinedChats.Contains(chatId))
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Вы не состоите в этом чате");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();
        var chat = await context.Chats
            .Include(c => c.ChatType)
            .FirstOrDefaultAsync(c => c.Id == chatId);

        if (chat == null)
        {
            ConsoleWrite($"Чат {chatId} не найден", "CHAT", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, $"Чат с ID {chatId} не найден");
            return;
        }

        clientInfo.CurrentChatId = chatId;
        clientInfo.ChatLastSeen[chatId] = DateTime.Now;

        var response = new
        {
            Type = ResponseMessageTypes.CHAT_SELECTED,
            ChatId = chatId,
            ChatName = chat.Name,
            ChatType = chat.ChatType != null ? new
            {
                chat.ChatType.Id,
                chat.ChatType.Name,
                chat.ChatType.Description
            } : null,
            Message = $"Выбран чат: {chat.Name}"
        };

        await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
        ConsoleWrite($"{clientInfo.Nickname} выбрал чат: {chat.Name}", "CHAT", ConsoleColor.Magenta);
    }

    private static async Task HandleGetChats(ClientInfo clientInfo, ChatService chatService)
    {
        ConsoleWrite($"{clientInfo.Nickname} запросил список чатов", "CHAT", ConsoleColor.DarkCyan);

        var chats = await chatService.GetUserChatsAsync(clientInfo.User.Id);
        await SendChatsListAsync(clientInfo, chats);
    }

    private static async Task HandleGetHistory(ClientInfo clientInfo, MessageService messageService)
    {
        if (!clientInfo.CurrentChatId.HasValue)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Сначала выберите чат");
            return;
        }

        ConsoleWrite($"{clientInfo.Nickname} запросил историю чата {clientInfo.CurrentChatId}", "CHAT", ConsoleColor.DarkCyan);

        try
        {
            var messages = await messageService.GetChatHistoryAsync(clientInfo.CurrentChatId.Value);
            await SendMessageHistoryAsync(clientInfo, messages);
            ConsoleWrite($"История отправлена {clientInfo.Nickname}", "CHAT", ConsoleColor.DarkGreen);
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка истории: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось получить историю сообщений");
        }
    }

    private static async Task HandleGetUsersAsync(ClientInfo clientInfo, UserService userService)
    {
        ConsoleWrite($"{clientInfo.Nickname} запросил список пользователей", "USER", ConsoleColor.DarkCyan);

        try
        {
            var users = await userService.GetAllUsersAsync();

            var response = new
            {
                Type = ResponseMessageTypes.USERS_LIST,
                Users = users.Select(u => new
                {
                    u.Id,
                    u.Login,
                    u.Name,
                    u.Surname,
                    u.SecondSurname,
                    u.CreateDate,
                    u.DateOfLastActivity,
                    u.UrlAvatar,
                    Status = u.Status?.Name,
                    IsOnline = IsUserOnline(u.Id),
                    LastSeen = u.DateOfLastActivity
                })
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
            ConsoleWrite($"Список пользователей отправлен {clientInfo.Nickname}", "USER", ConsoleColor.DarkGreen);
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка списка пользователей: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось получить список пользователей");
        }
    }

    private static async Task SendUserInfoAsync(WebSocket socket, User user, string message)
    {
        var response = new
        {
            Status = "OK",
            Message = message,
            User = new
            {
                user.Id,
                user.Login,
                user.Name,
                user.Surname,
                user.SecondSurname,
                user.CreateDate,
                user.DateOfLastActivity,
                user.UrlAvatar,
                user.StatusId
            }
        };

        await SendResponseAsync(socket, JsonSerializer.Serialize(response, JsonOptions));
    }

    private static async Task SendChatsListAsync(ClientInfo clientInfo, List<Chat> chats)
    {
        var response = new
        {
            Type = ResponseMessageTypes.CHAT_LIST,
            Chats = chats.Select(c => new
            {
                c.Id,
                c.Name,
                c.ChatTypeId,
                c.CreateDate,
                ChatType = c.ChatType != null ? new
                {
                    c.ChatType.Id,
                    c.ChatType.Name,
                    c.ChatType.Description
                } : null,
                IsJoined = clientInfo.JoinedChats.Contains(c.Id),
                UnreadCount = UserUnreadMessages.ContainsKey(clientInfo.User.Id)
                    ? UserUnreadMessages[clientInfo.User.Id].Count
                    : 0
            })
        };

        await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
    }

    private static async Task SendMessageHistoryAsync(ClientInfo clientInfo, List<MessageUserInChat> messages)
    {
        var response = new
        {
            Type = ResponseMessageTypes.MESSAGE_HISTORY,
            ChatId = clientInfo.CurrentChatId,
            Messages = messages.Select(m => new
            {
                IdMessage = m.IdMessage,
                IdChat = m.IdChat,
                IdUser = m.IdUser,
                Message = m.IdMessageNavigation != null ? new
                {
                    m.IdMessageNavigation.Id,
                    m.IdMessageNavigation.Data,
                    m.IdMessageNavigation.DateAndTime,
                    m.IdMessageNavigation.IsDeleted,
                    m.IdMessageNavigation.IsUpdate,
                    m.IdMessageNavigation.IdMessagesReferred,
                    m.IdMessageNavigation.IsRead
                } : null,
                User = m.IdUserNavigation != null ? new
                {
                    m.IdUserNavigation.Id,
                    m.IdUserNavigation.Login,
                    m.IdUserNavigation.Name,
                    m.IdUserNavigation.Surname,
                    m.IdUserNavigation.SecondSurname,
                    m.IdUserNavigation.CreateDate,
                    m.IdUserNavigation.UrlAvatar
                } : null,
                Chat = m.IdChatNavigation != null ? new
                {
                    m.IdChatNavigation.Id,
                    m.IdChatNavigation.Name,
                    m.IdChatNavigation.ChatTypeId,
                    m.IdChatNavigation.CreateDate
                } : null
            })
        };

        await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
    }

    private static async Task BroadcastNewMessageAsync(MessageUserInChat messageInChat, IServiceProvider serviceProvider)
    {
        var messageObject = new
        {
            Type = ResponseMessageTypes.NEW_MESSAGE,
            Message = new
            {
                IdMessage = messageInChat.IdMessage,
                IdChat = messageInChat.IdChat,
                IdUser = messageInChat.IdUser,
                Message = messageInChat.IdMessageNavigation != null ? new
                {
                    messageInChat.IdMessageNavigation.Id,
                    messageInChat.IdMessageNavigation.Data,
                    messageInChat.IdMessageNavigation.DateAndTime,
                    messageInChat.IdMessageNavigation.IsDeleted,
                    messageInChat.IdMessageNavigation.IsUpdate,
                    messageInChat.IdMessageNavigation.IsRead
                } : null,
                User = messageInChat.IdUserNavigation != null ? new
                {
                    messageInChat.IdUserNavigation.Id,
                    messageInChat.IdUserNavigation.Login,
                    messageInChat.IdUserNavigation.Name,
                    messageInChat.IdUserNavigation.Surname,
                    messageInChat.IdUserNavigation.SecondSurname,
                    messageInChat.IdUserNavigation.UrlAvatar
                } : null
            }
        };

        var jsonMessage = JsonSerializer.Serialize(messageObject, JsonOptions);
        var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

        // Автоматически отмечаем сообщение как прочитанное для отправителя
        try
        {
            using var scope = serviceProvider.CreateScope();
            var messageService = scope.ServiceProvider.GetRequiredService<MessageService>();

            // Отправитель сразу видит свое сообщение как прочитанное
            await messageService.MarkMessageAsReadAsync(messageInChat.IdMessage, messageInChat.IdUser);

            ConsoleWrite($"Сообщение {messageInChat.IdMessage} автоматически отмечено как прочитанное для отправителя {messageInChat.IdUserNavigation?.Login}",
                "MESSAGE", ConsoleColor.DarkGreen);
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка автоматической отметки сообщения как прочитанного: {ex.Message}",
                "ERROR", ConsoleColor.Red);
        }

        // Получаем всех участников чата для правильного распределения уведомлений
        var chatParticipants = new List<int>();
        var chatName = "";
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

            // Получаем участников чата и название чата
            chatParticipants = await context.UserInChats
                .Where(uc => uc.ChatId == messageInChat.IdChat && uc.DateRelease == null)
                .Select(uc => uc.UserId)
                .ToListAsync();

            var chat = await context.Chats.FindAsync(messageInChat.IdChat);
            chatName = chat?.Name ?? "Неизвестный чат";
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка получения участников чата: {ex.Message}", "ERROR", ConsoleColor.Red);
        }

        // Уведомление для всех участников чата (включая тех, кто не в чате сейчас)
        var notificationObject = new
        {
            Type = ResponseMessageTypes.NEW_CHAT_MESSAGE_NOTIFICATION,
            ChatId = messageInChat.IdChat,
            ChatName = chatName,
            MessageId = messageInChat.IdMessage,
            SenderId = messageInChat.IdUser,
            SenderName = messageInChat.IdUserNavigation?.Name,
            SenderSurname = messageInChat.IdUserNavigation?.Surname,
            SenderAvatar = messageInChat.IdUserNavigation?.UrlAvatar,
            MessagePreview = messageInChat.IdMessageNavigation?.Data.Length > 50
                ? messageInChat.IdMessageNavigation.Data.Substring(0, 50) + "..."
                : messageInChat.IdMessageNavigation?.Data,
            Timestamp = DateTime.Now,
            UnreadCount = 1,
            IsMentioned = false, // Можно добавить логику упоминаний
            MessageType = "text" // Можно расширить для разных типов сообщений
        };

        var jsonNotification = JsonSerializer.Serialize(notificationObject, JsonOptions);
        var notificationBytes = Encoding.UTF8.GetBytes(jsonNotification);
        bool sost = true;

        // Для каждого участника чата
        foreach (var participantId in chatParticipants)
        {
            // Пропускаем отправителя
            if (participantId == messageInChat.IdUser)
                continue;

            // Ищем клиента среди подключенных
            var participantClient = ConnectedClients.Values
                .FirstOrDefault(c => c.User?.Id == participantId && c.Socket.State == WebSocketState.Open);

            if (participantClient != null)
            {
                try
                {
                    // Если участник находится в этом чате сейчас - отправляем полное сообщение
                    if (participantClient.CurrentChatId == messageInChat.IdChat)
                    {
                        await participantClient.Socket.SendAsync(
                            new ArraySegment<byte>(messageBytes),
                            WebSocketMessageType.Text,
                            false,
                            CancellationToken.None);

                        ConsoleWrite($"Полное сообщение отправлено участнику {participantClient.Nickname} в чате {messageInChat.IdChat}",
                            "MESSAGE", ConsoleColor.DarkGray);

                        // Автоматически отмечаем сообщение как прочитанное для участника, который в чате
                        try
                        {
                            using var scope = serviceProvider.CreateScope();
                            var messageService = scope.ServiceProvider.GetRequiredService<MessageService>();

                            await messageService.MarkMessageAsReadAsync(messageInChat.IdMessage, participantId);

                            // Отправляем уведомление автору о прочтении (если автор онлайн)
                            if (IsUserOnline(messageInChat.IdUser) && messageInChat.IdUser != participantId)
                            {
                                var readNotification = new
                                {
                                    Type = ResponseMessageTypes.MESSAGE_READ_CONFIRMATION,
                                    MessageId = messageInChat.IdMessage,
                                    ChatId = messageInChat.IdChat,
                                    ReadByUserId = participantId,
                                    ReadByUserName = $"{participantClient.User?.Name} {participantClient.User?.Surname}".Trim(),
                                    ReadByUserLogin = participantClient.User?.Login,
                                    MessageText = messageInChat.IdMessageNavigation?.Data?.Length > 50
                                        ? messageInChat.IdMessageNavigation.Data.Substring(0, 50) + "..."
                                        : messageInChat.IdMessageNavigation?.Data,
                                    OriginalMessageDate = messageInChat.IdMessageNavigation?.DateAndTime,
                                    ReadAt = DateTime.Now,
                                    Timestamp = DateTime.Now,
                                    IsAutoRead = false // Флаг, что прочтение автоматическое
                                };
                                sost = readNotification.IsAutoRead;

                                // Отправляем уведомление автору
                                if (UserConnectionMap.TryGetValue(messageInChat.IdUser, out var authorConnectionId))
                                {
                                    if (ConnectedClients.TryGetValue(authorConnectionId, out var authorClient) &&
                                        authorClient.Socket.State == WebSocketState.Open)
                                    {
                                        await SendResponseAsync(authorClient.Socket,
                                            JsonSerializer.Serialize(readNotification, JsonOptions));

                                        ConsoleWrite($"Автору {messageInChat.IdUserNavigation?.Login} отправлено уведомление об автоматическом прочтении сообщения {messageInChat.IdMessage} пользователем {participantClient.Nickname}",
                                            "MESSAGE", ConsoleColor.DarkCyan);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ConsoleWrite($"Ошибка автоматической отметки сообщения как прочитанного для участника {participantId}: {ex.Message}",
                                "ERROR", ConsoleColor.Red);
                        }
                    }
                    // Если участник состоит в чате, но не находится в нем - отправляем уведомление
                    else if (participantClient.JoinedChats.Contains(messageInChat.IdChat))
                    {
                        // Добавляем сообщение в непрочитанные для этого пользователя
                        if (!UserUnreadMessages.ContainsKey(participantId))
                        {
                            UserUnreadMessages[participantId] = new HashSet<int>();
                        }
                        UserUnreadMessages[participantId].Add(messageInChat.IdMessage);

                        // Отправляем уведомление
                        await participantClient.Socket.SendAsync(
                            new ArraySegment<byte>(notificationBytes),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);

                        ConsoleWrite($"Уведомление отправлено участнику {participantClient.Nickname} о новом сообщении в чате {messageInChat.IdChat}",
                            "MESSAGE", ConsoleColor.DarkGray);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWrite($"Ошибка отправки сообщения/уведомления участнику {participantId}: {ex.Message}",
                        "ERROR", ConsoleColor.Red);

                    // Если ошибка отправки, возможно клиент отключился
                    // Удаляем его из списка непрочитанных для очистки
                    UserUnreadMessages.TryRemove(participantId, out _);
                }
            }
            else
            {
                // Участник не онлайн, сохраняем сообщение как непрочитанное в БД
                // (это уже делается при сохранении сообщения через MessageService)
                ConsoleWrite($"Участник {participantId} не в сети, уведомление не отправлено",
                    "MESSAGE", ConsoleColor.DarkGray);
            }
        }

        // Логируем статистику отправки
        ConsoleWrite($"Сообщение {messageInChat.IdMessage} отправлено в чат {messageInChat.IdChat}. " +
                    $"Участников: {chatParticipants.Count}, " + $"Состояние сообщения: {Convert.ToString(sost)}, " +
                    $"Онлайн: {ConnectedClients.Values.Count(c => chatParticipants.Contains(c.User?.Id ?? -1))}, " +
                    $"В чате: {ConnectedClients.Values.Count(c => c.CurrentChatId == messageInChat.IdChat)}",
                    "STATS", ConsoleColor.Blue);
    }

    private static async Task SendUnreadNotificationsAsync(ClientInfo clientInfo, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null) return;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();
            var messageService = scope.ServiceProvider.GetRequiredService<MessageService>();
            var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();

            // Получаем все чаты пользователя
            var userChats = await context.UserInChats
                .Where(uc => uc.UserId == clientInfo.User.Id && uc.DateRelease == null)
                .Select(uc => uc.ChatId)
                .ToListAsync();

            ConsoleWrite($"{clientInfo.Nickname}: проверяем непрочитанные сообщения в {userChats.Count} чатах",
                "NOTIFY", ConsoleColor.DarkCyan);

            int totalUnread = 0;
            var notifications = new List<object>();

            foreach (var chatId in userChats)
            {
                try
                {
                    // Получаем все непрочитанные сообщения в чате
                    var unreadMessages = await messageService.GetUnreadMessagesAsync(clientInfo.User.Id, chatId);

                    if (unreadMessages.Any())
                    {
                        totalUnread += unreadMessages.Count;

                        // Группируем по отправителям для оптимизации
                        var groupedBySender = unreadMessages
                            .GroupBy(m => m.IdUser)
                            .ToDictionary(g => g.Key, g => g.ToList());

                        // Для каждого отправителя отправляем одно уведомление с количеством сообщений
                        foreach (var senderGroup in groupedBySender)
                        {
                            var senderId = senderGroup.Key;
                            var senderMessages = senderGroup.Value;
                            var latestMessage = senderMessages.Last(); // Последнее сообщение от этого отправителя

                            // Получаем информацию о чате
                            var chat = await context.Chats
                                .Include(c => c.ChatType)
                                .FirstOrDefaultAsync(c => c.Id == chatId);

                            // Получаем информацию об отправителе
                            var sender = await context.Users
                                .FirstOrDefaultAsync(u => u.Id == senderId);

                            if (chat != null && sender != null)
                            {
                                var notification = new
                                {
                                    Type = ResponseMessageTypes.NEW_CHAT_MESSAGE_NOTIFICATION,
                                    ChatId = chatId,
                                    ChatName = chat.Name,
                                    ChatType = chat.ChatTypeId,
                                    SenderId = senderId,
                                    SenderName = sender.Name,
                                    SenderSurname = sender.Surname,
                                    SenderAvatar = sender.UrlAvatar,
                                    // Последнее сообщение от этого отправителя
                                    MessagePreview = latestMessage.IdMessageNavigation?.Data.Length > 50
                                        ? latestMessage.IdMessageNavigation.Data.Substring(0, 50) + "..."
                                        : latestMessage.IdMessageNavigation?.Data,
                                    Timestamp = latestMessage.IdMessageNavigation?.DateAndTime ?? DateTime.Now,
                                    UnreadFromThisSender = senderMessages.Count,
                                    TotalUnreadInChat = unreadMessages.Count,
                                    IsOnline = IsUserOnline(senderId),
                                    IsPriority = senderMessages.Count > 3 // Приоритетное уведомление если много сообщений
                                };

                                notifications.Add(notification);

                                // Также сохраняем в кэш непрочитанных сообщений для быстрого доступа
                                if (!UserUnreadMessages.ContainsKey(clientInfo.User.Id))
                                {
                                    UserUnreadMessages[clientInfo.User.Id] = new HashSet<int>();
                                }

                                foreach (var message in senderMessages)
                                {
                                    UserUnreadMessages[clientInfo.User.Id].Add(message.IdMessage);
                                }
                            }
                        }

                        ConsoleWrite($"{clientInfo.Nickname}: в чате {chatId} найдено {unreadMessages.Count} непрочитанных сообщений",
                            "NOTIFY", ConsoleColor.DarkGray);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWrite($"Ошибка обработки чата {chatId} для {clientInfo.Nickname}: {ex.Message}",
                        "ERROR", ConsoleColor.Red);
                }
            }

            // Отправляем все уведомления клиенту
            if (notifications.Any())
            {
                // Сортируем уведомления: сначала приоритетные, затем по времени
                var sortedNotifications = notifications
                    .OrderByDescending(n => ((dynamic)n).IsPriority)
                    .ThenByDescending(n => ((dynamic)n).Timestamp)
                    .ToList();

                foreach (var notification in sortedNotifications)
                {
                    var jsonNotification = JsonSerializer.Serialize(notification, JsonOptions);
                    await SendResponseAsync(clientInfo.Socket, jsonNotification);

                    // Небольшая задержка между уведомлениями, чтобы не перегрузить клиента
                    await Task.Delay(50);
                }

                ConsoleWrite($"{clientInfo.Nickname}: отправлено {sortedNotifications.Count} уведомлений о {totalUnread} непрочитанных сообщениях",
                    "NOTIFY", ConsoleColor.Green);
            }
            else
            {
                // Отправляем сообщение, что непрочитанных сообщений нет
                var noNotifications = new
                {
                    Type = ResponseMessageTypes.SYSTEM_MESSAGE,
                    Message = "Нет непрочитанных сообщений",
                    Timestamp = DateTime.Now,
                    TotalUnread = 0
                };

                await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(noNotifications, JsonOptions));

                ConsoleWrite($"{clientInfo.Nickname}: непрочитанных сообщений не найдено",
                    "NOTIFY", ConsoleColor.DarkGreen);
            }

        }
        catch (Exception ex)
        {
            ConsoleWrite($"Общая ошибка отправки непрочитанных уведомлений для {clientInfo.Nickname}: {ex.Message}",
                "ERROR", ConsoleColor.Red);

            // Отправляем сообщение об ошибке клиенту
            try
            {
                var errorResponse = new
                {
                    Status = "ERROR",
                    Message = "Не удалось загрузить непрочитанные сообщения"
                };
                await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(errorResponse, JsonOptions));
            }
            catch { }
        }
    }

    // Метод для получения сводки по непрочитанным сообщениям (для клиента)
    private static async Task HandleGetUnreadSummaryAsync(ClientInfo clientInfo, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();
            var messageService = scope.ServiceProvider.GetRequiredService<MessageService>();

            // Получаем все чаты пользователя
            var userChats = await context.UserInChats
                .Where(uc => uc.UserId == clientInfo.User.Id && uc.DateRelease == null)
                .Select(uc => uc.ChatId)
                .ToListAsync();

            var summary = new List<object>();
            int totalUnread = 0;

            foreach (var chatId in userChats)
            {
                var unreadCount = await messageService.GetUnreadCountAsync(clientInfo.User.Id, chatId);
                if (unreadCount > 0)
                {
                    totalUnread += unreadCount;

                    var chat = await context.Chats.FindAsync(chatId);
                    var latestUnread = await messageService.GetUnreadMessagesAsync(clientInfo.User.Id, chatId, 1);

                    var lastMessage = latestUnread.FirstOrDefault();
                    var sender = lastMessage?.IdUserNavigation;

                    summary.Add(new
                    {
                        ChatId = chatId,
                        ChatName = chat?.Name,
                        UnreadCount = unreadCount,
                        LastMessageTime = lastMessage?.IdMessageNavigation?.DateAndTime,
                        LastMessagePreview = lastMessage?.IdMessageNavigation?.Data?.Length > 30
                            ? lastMessage.IdMessageNavigation.Data.Substring(0, 30) + "..."
                            : lastMessage?.IdMessageNavigation?.Data,
                        LastSenderName = sender != null ? $"{sender.Name} {sender.Surname}" : "Неизвестно",
                        LastSenderId = lastMessage?.IdUser
                    });
                }
            }

            var response = new
            {
                Type = "UNREAD_SUMMARY",
                TotalUnread = totalUnread,
                Summary = summary,
                Timestamp = DateTime.Now
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));

            ConsoleWrite($"{clientInfo.Nickname} запросил сводку по непрочитанным: {totalUnread} сообщений в {summary.Count} чатах",
                "NOTIFY", ConsoleColor.DarkCyan);
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка получения сводки по непрочитанным для {clientInfo.Nickname}: {ex.Message}",
                "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось получить сводку по непрочитанным сообщениям");
        }
    }

    private static async Task BroadcastSystemMessageAsync(string message, int? chatId = null)
    {
        var systemMessage = new
        {
            Type = ResponseMessageTypes.SYSTEM_MESSAGE,
            Message = message,
            Timestamp = DateTime.Now,
            ChatId = chatId
        };

        var jsonMessage = JsonSerializer.Serialize(systemMessage, JsonOptions);
        var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

        foreach (var client in ConnectedClients.Values.ToArray())
        {
            if (client.Socket.State == WebSocketState.Open &&
                (chatId == null || client.CurrentChatId == chatId || client.JoinedChats.Contains(chatId.Value)))
            {
                try
                {
                    await client.Socket.SendAsync(
                        new ArraySegment<byte>(messageBytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private static async Task BroadcastUserStatusChangeAsync(int userId, bool isOnline)
    {
        var statusMessage = new
        {
            Type = ResponseMessageTypes.USER_STATUS_CHANGE,
            UserId = userId,
            IsOnline = isOnline,
            LastActivity = DateTime.Now,
            Timestamp = DateTime.Now
        };

        var jsonMessage = JsonSerializer.Serialize(statusMessage, JsonOptions);
        var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

        foreach (var client in ConnectedClients.Values.ToArray())
        {
            if (client.Socket.State == WebSocketState.Open && client.User?.Id != userId)
            {
                try
                {
                    await client.Socket.SendAsync(
                        new ArraySegment<byte>(messageBytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
                catch { }
            }
        }
    }



    private static async Task BroadcastUserProfileUpdateAsync(int userId, object updates)
    {
        var updateMessage = new
        {
            Type = ResponseMessageTypes.USER_PROFILE_UPDATE,
            UserId = userId,
            Updates = updates,
            Timestamp = DateTime.Now
        };

        var jsonMessage = JsonSerializer.Serialize(updateMessage, JsonOptions);
        var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

        foreach (var client in ConnectedClients.Values.ToArray())
        {
            if (client.Socket.State == WebSocketState.Open && client.User?.Id != userId)
            {
                try
                {
                    await client.Socket.SendAsync(
                        new ArraySegment<byte>(messageBytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
                catch { }
            }
        }
    }

    public static void LogMessage(string message, string type = "INFO")
    {
        var timestamp = DateTime.Now;
        var logMessage = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] [{type}] {message}";

        lock (LogFileLock)
        {
            try
            {
                File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ConsoleWrite($"Ошибка записи лога: {ex.Message}", "ERROR", ConsoleColor.Red);
            }
        }
    }

    private static async Task SendResponseAsync(WebSocket socket, string message)
    {
        if (socket.State == WebSocketState.Open)
        {
            var responseBytes = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(
                new ArraySegment<byte>(responseBytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
    }

    private static async Task SendErrorResponseAsync(WebSocket socket, string errorMessage)
    {
        var errorResponse = new
        {
            Status = "ERROR",
            Message = errorMessage
        };

        await SendResponseAsync(socket, JsonSerializer.Serialize(errorResponse, JsonOptions));
    }

    private static async Task SendResponseAsync(WebSocket socket, object responseObject)
    {
        var jsonResponse = JsonSerializer.Serialize(responseObject, JsonOptions);
        await SendResponseAsync(socket, jsonResponse);
    }

    private static void LogToFile(object logEntry)
    {
        lock (LogFileLock)
        {
            try
            {
                var jsonEntry = JsonSerializer.Serialize(logEntry, JsonOptions);
                File.AppendAllText(LogFilePath, jsonEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ConsoleWrite($"Ошибка записи в лог: {ex.Message}", "ERROR", ConsoleColor.Red);
            }
        }
    }

    // Вспомогательный метод для форматирования времени
    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }

    // Статистика
    public static object GetServerStats()
    {
        var now = DateTime.Now;
        var uptime = now - ServerStartTime;

        return new
        {
            TotalConnectedClients = ConnectedClients.Count,
            ActiveClients = ConnectedClients.Values
                .Where(c => c.Socket.State == WebSocketState.Open)
                .Select(c => new
                {
                    Nickname = c.Nickname,
                    IP = c.IP,
                    CurrentChatId = c.CurrentChatId,
                    JoinedChats = c.JoinedChats.Count,
                    UserName = $"{c.User?.Name} {c.User?.Surname}".Trim() ?? c.Nickname ?? "Гость",
                    ConnectionTime = c.ConnectionTime.ToString("HH:mm:ss"),
                    LastActivity = c.LastActivityTime.ToString("HH:mm:ss"),
                    Status = c.User?.StatusId
                })
                .ToList(),
            ServerUptime = FormatTimeSpan(uptime),
            ServerStartTime = ServerStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
            CurrentTime = now.ToString("yyyy-MM-dd HH:mm:ss"),
            DiscoveryActive = _discoveryTimer?.Enabled ?? false,
            DiscoveryPort = DiscoveryPort,
            ServerIP = _serverIp,
            LogFileSize = File.Exists(LogFilePath) ? new FileInfo(LogFilePath).Length : 0,
            OnlineUsers = UserConnectionMap.Count
        };
    }

    public static List<object> GetConnectedClientsInfo()
    {
        return ConnectedClients.Values.Select(c => new
        {
            c.Nickname,
            c.IP,
            c.Port,
            ConnectionTime = c.ConnectionTime.ToString("HH:mm:ss"),
            LastActivity = c.LastActivityTime.ToString("HH:mm:ss"),
            CurrentChat = c.CurrentChatId,
            JoinedChats = c.JoinedChats,
            User = c.User != null ? new
            {
                c.User.Id,
                c.User.Login,
                c.User.Name,
                c.User.Surname,
                c.User.SecondSurname,
                c.User.UrlAvatar,
                c.User.DateOfLastActivity,
                c.User.StatusId
            } : null
        }).Cast<object>().ToList();
    }

    // Метод для вычисления похожести триграмм (fuzzy search)
    private static double CalculateTrigramSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0.0;

        s1 = s1.ToLowerInvariant();
        s2 = s2.ToLowerInvariant();

        // Если строки идентичны
        if (s1 == s2) return 1.0;

        // Если одна строка содержит другую
        if (s1.Contains(s2) || s2.Contains(s1)) return 0.9;

        // Генерируем триграммы
        var trigrams1 = GenerateTrigrams(s1);
        var trigrams2 = GenerateTrigrams(s2);

        if (trigrams1.Count == 0 || trigrams2.Count == 0)
            return 0.0;

        // Находим пересечение
        var intersection = trigrams1.Intersect(trigrams2).Count();
        var union = trigrams1.Union(trigrams2).Count();

        var similarity = (double)intersection / union;

        // Дополнительные эвристики для опечаток
        if (similarity > 0.5)
        {
            // Проверяем расстояние Левенштейна для очень похожих строк
            var distance = CalculateLevenshteinDistance(s1, s2);
            var maxLength = Math.Max(s1.Length, s2.Length);
            var distanceSimilarity = 1.0 - (double)distance / maxLength;

            // Усредняем результаты
            similarity = (similarity + distanceSimilarity) / 2;
        }

        return similarity;
    }

    private static int CalculateLevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a))
            return string.IsNullOrEmpty(b) ? 0 : b.Length;
        if (string.IsNullOrEmpty(b))
            return a.Length;

        var matrix = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++)
            matrix[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            matrix[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[a.Length, b.Length];
    }

    private static HashSet<string> GenerateTrigrams(string text)
    {
        var trigrams = new HashSet<string>();

        if (text.Length < 3)
        {
            // Для коротких строк используем биграммы или саму строку
            if (text.Length == 2)
                trigrams.Add(text);
            else if (text.Length == 1)
                trigrams.Add(text + "_");
            return trigrams;
        }

        for (int i = 0; i < text.Length - 2; i++)
        {
            var trigram = text.Substring(i, 3);
            trigrams.Add(trigram);
        }

        return trigrams;
    }

    private static async Task HandleSearchChats(ClientInfo clientInfo, JsonDocument jsonDoc, ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var searchTerm = root.GetProperty("searchTerm").GetString();
        var threshold = root.TryGetProperty("threshold", out var thresholdElement)
            ? thresholdElement.GetDouble()
            : 0.3;

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

        try
        {
            // Получаем ВСЕ чаты
            var allChats = await context.Chats
                .Include(c => c.ChatType)
                .Include(c => c.UserInChats)
                    .ThenInclude(uc => uc.User)
                .ToListAsync();

            // Фильтруем по похожести с использованием триграмм
            var filteredChats = allChats
                .Select(chat => new
                {
                    Chat = chat,
                    Similarity = CalculateTrigramSimilarity(searchTerm, chat.Name)
                })
                .Where(x => x.Similarity >= threshold)
                .OrderByDescending(x => x.Similarity)
                .Take(50)
                .ToList();

            // Также ищем по логинам участников чата
            var chatsByMembers = allChats
                .Select(chat => new
                {
                    Chat = chat,
                    Similarity = chat.UserInChats
                        .Where(uc => uc.DateRelease == null && uc.User != null)
                        .Select(uc => CalculateTrigramSimilarity(searchTerm, uc.User.Login))
                        .DefaultIfEmpty(0)
                        .Max()
                })
                .Where(x => x.Similarity >= threshold)
                .OrderByDescending(x => x.Similarity)
                .Take(50)
                .ToList();

            // Объединяем результаты
            var combinedResults = filteredChats.Concat(chatsByMembers).ToList();

            var allResults = combinedResults
                .GroupBy(x => x.Chat.Id)
                .Select(g => new
                {
                    Chat = g.First().Chat,
                    Similarity = g.Max(x => x.Similarity)
                })
                .OrderByDescending(x => x.Similarity)
                .Take(50)
                .ToList();

            ConsoleWrite($"{clientInfo.Nickname} ищет чаты: '{searchTerm}'", "SEARCH", ConsoleColor.DarkCyan);

            var response = new
            {
                Type = ResponseMessageTypes.SEARCH_CHATS_RESULTS,
                SearchTerm = searchTerm,
                Results = allResults.Select(r => new
                {
                    r.Chat.Id,
                    r.Chat.Name,
                    ChatType = r.Chat.ChatType != null ? new
                    {
                        r.Chat.ChatType.Id,
                        r.Chat.ChatType.Name,
                        r.Chat.ChatType.Description
                    } : null,
                    r.Chat.CreateDate,
                    MemberCount = r.Chat.UserInChats != null ?
                        r.Chat.UserInChats.Count(uc => uc.DateRelease == null) : 0,
                    IsJoined = clientInfo.JoinedChats.Contains(r.Chat.Id),
                    Similarity = r.Similarity,
                    Members = (r.Chat.UserInChats ?? new List<UserInChat>())
                        .Where(uc => uc.DateRelease == null && uc.User != null)
                        .Take(5)
                        .Select(uc => new
                        {
                            uc.User.Id,
                            uc.User.Login,
                            uc.User.Name
                        })
                        .ToList()
                }).ToList(),
                TotalResults = allResults.Count
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка поиска чатов: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось выполнить поиск чатов");
        }
    }

    private static async Task HandleSearchUsers(ClientInfo clientInfo, JsonDocument jsonDoc, UserService userService)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var searchTerm = root.GetProperty("searchTerm").GetString();
        var threshold = root.TryGetProperty("threshold", out var thresholdElement)
            ? thresholdElement.GetDouble()
            : 0.3;

        try
        {
            var allUsers = await userService.GetAllUsersAsync();

            // Фильтруем пользователей по триграммам
            var filteredUsers = allUsers
                .Select(user => new
                {
                    User = user,
                    LoginSimilarity = CalculateTrigramSimilarity(searchTerm, user.Login),
                    NameSimilarity = CalculateTrigramSimilarity(searchTerm, user.Name),
                    SurnameSimilarity = CalculateTrigramSimilarity(searchTerm, user.Surname),
                    FullNameSimilarity = CalculateTrigramSimilarity(searchTerm, $"{user.Name} {user.Surname}")
                })
                .Select(x => new
                {
                    x.User,
                    Similarity = Math.Max(
                        Math.Max(x.LoginSimilarity, x.NameSimilarity),
                        Math.Max(x.SurnameSimilarity, x.FullNameSimilarity)
                    )
                })
                .Where(x => x.Similarity >= threshold)
                .OrderByDescending(x => x.Similarity)
                .Take(50)
                .ToList();

            ConsoleWrite($"{clientInfo.Nickname} ищет пользователей: '{searchTerm}'", "SEARCH", ConsoleColor.DarkCyan);

            var response = new
            {
                Type = ResponseMessageTypes.SEARCH_USERS_RESULTS,
                SearchTerm = searchTerm,
                Results = filteredUsers.Select(r => new
                {
                    r.User.Id,
                    r.User.Login,
                    r.User.Name,
                    r.User.Surname,
                    r.User.SecondSurname,
                    r.User.CreateDate,
                    r.User.UrlAvatar,
                    Status = r.User.Status?.Name,
                    IsOnline = IsUserOnline(r.User.Id),
                    LastSeen = r.User.DateOfLastActivity,
                    Similarity = r.Similarity
                }).ToList(),
                TotalResults = filteredUsers.Count
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка поиска пользователей: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось выполнить поиск пользователей");
        }
    }

    private static async Task HandleGlobalSearch(ClientInfo clientInfo, JsonDocument jsonDoc,
    UserService userService, ChatService chatService, IServiceProvider serviceProvider)
    {
        if (clientInfo.User == null)
        {
            await SendErrorResponseAsync(clientInfo.Socket, "Необходима авторизация");
            return;
        }

        var root = jsonDoc.RootElement;
        var searchTerm = root.GetProperty("searchTerm").GetString();
        var threshold = root.TryGetProperty("threshold", out var thresholdElement)
            ? thresholdElement.GetDouble()
            : 0.3;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LocalMessangerDbContext>();

            // Создаем явные классы для результатов
            var searchResults = new List<SearchResultItem>();

            // Получаем чаты
            var allChats = await context.Chats
                .Include(c => c.ChatType)
                .Include(c => c.UserInChats)
                    .ThenInclude(uc => uc.User)
                .ToListAsync();

            // Обрабатываем чаты
            foreach (var chat in allChats)
            {
                var similarity = CalculateTrigramSimilarity(searchTerm, chat.Name);
                if (similarity >= threshold)
                {
                    searchResults.Add(new SearchResultItem
                    {
                        Type = "chat",
                        Chat = chat,
                        Similarity = similarity
                    });
                }
            }

            // Получаем пользователей
            var allUsers = await userService.GetAllUsersAsync();

            // Обрабатываем пользователей
            foreach (var user in allUsers)
            {
                var similarity = Math.Max(
                    CalculateTrigramSimilarity(searchTerm, user.Login),
                    CalculateTrigramSimilarity(searchTerm, $"{user.Name} {user.Surname}")
                );

                if (similarity >= threshold)
                {
                    searchResults.Add(new SearchResultItem
                    {
                        Type = "user",
                        User = user,
                        Similarity = similarity
                    });
                }
            }

            // Сортируем по убыванию похожести
            var sortedResults = searchResults
                .OrderByDescending(r => r.Similarity)
                .Take(50)
                .ToList();

            // Преобразуем в ответ
            var resultsList = new List<object>();

            foreach (var result in sortedResults)
            {
                if (result.Type == "chat" && result.Chat != null)
                {
                    resultsList.Add(new
                    {
                        Type = "chat",
                        Id = result.Chat.Id,
                        Name = result.Chat.Name,
                        ChatType = result.Chat.ChatType != null ? new
                        {
                            result.Chat.ChatType.Id,
                            result.Chat.ChatType.Name,
                            result.Chat.ChatType.Description
                        } : null,
                        CreateDate = result.Chat.CreateDate,
                        MemberCount = result.Chat.UserInChats != null ?
                            result.Chat.UserInChats.Count(uc => uc.DateRelease == null) : 0,
                        IsJoined = clientInfo.JoinedChats.Contains(result.Chat.Id),
                        Similarity = result.Similarity
                    });
                }
                else if (result.Type == "user" && result.User != null)
                {
                    resultsList.Add(new
                    {
                        Type = "user",
                        Id = result.User.Id,
                        Login = result.User.Login,
                        Name = result.User.Name,
                        Surname = result.User.Surname,
                        SecondSurname = result.User.SecondSurname,
                        UrlAvatar = result.User.UrlAvatar,
                        Status = result.User.Status?.Name,
                        IsOnline = IsUserOnline(result.User.Id),
                        LastSeen = result.User.DateOfLastActivity,
                        Similarity = result.Similarity
                    });
                }
            }

            ConsoleWrite($"{clientInfo.Nickname} выполняет глобальный поиск: '{searchTerm}'", "SEARCH", ConsoleColor.DarkCyan);

            var response = new
            {
                Type = ResponseMessageTypes.SEARCH_RESULTS,
                SearchTerm = searchTerm,
                Results = resultsList,
                TotalChats = searchResults.Count(r => r.Type == "chat"),
                TotalUsers = searchResults.Count(r => r.Type == "user")
            };

            await SendResponseAsync(clientInfo.Socket, JsonSerializer.Serialize(response, JsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWrite($"Ошибка глобального поиска: {ex.Message}", "ERROR", ConsoleColor.Red);
            await SendErrorResponseAsync(clientInfo.Socket, "Не удалось выполнить поиск");
        }
    }

    // Добавим вспомогательный класс для результатов поиска
    private class SearchResultItem
    {
        public string Type { get; set; } = string.Empty;
        public Chat? Chat { get; set; }
        public User? User { get; set; }
        public double Similarity { get; set; }
    }
}