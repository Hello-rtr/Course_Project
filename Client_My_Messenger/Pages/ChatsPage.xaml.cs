using Client_My_Messenger.Dialogs;
using Client_My_Messenger.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Client_My_Messenger.Pages
{
    public partial class ChatsPage : Page, INotifyPropertyChanged
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private User _currentUser;
        private bool _isSelectingChat = false;
        private DispatcherTimer _uiTimer;
        private HashSet<int> _displayedChatIds = new HashSet<int>();
        private bool _isGlobalSearchMode = false;
        private object _selectedItem;
        private Task _receiveTask;
        private CancellationTokenSource _receiveCts;

        // Кэш непрочитанных сообщений
        private Dictionary<int, int> _unreadCounts = new Dictionary<int, int>();
        private Dictionary<int, List<int>> _readMessageIds = new Dictionary<int, List<int>>();
        private Dictionary<int, DateTime> _chatLastSeen = new Dictionary<int, DateTime>();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ChatsPage()
        {
            InitializeComponent();
            try
            {
                _ws = AutorizationPage.ws;
                _cts = AutorizationPage.cts;
                _currentUser = AutorizationPage.currentUser;
                _receiveCts = new CancellationTokenSource();

                // Инициализация коллекций
                _availableChats = new ObservableCollection<Chat>();
                _messageHistory = new ObservableCollection<MessageUserInChat>();
                _filteredChats = new ObservableCollection<Chat>();
                _searchResults = new ObservableCollection<SearchResult>();

                DataContext = this;

                // Таймер для обновления UI
                _uiTimer = new DispatcherTimer();
                _uiTimer.Interval = TimeSpan.FromMilliseconds(100);
                _uiTimer.Tick += (s, e) => ProcessUIActions();
                _uiTimer.Start();

                Loaded += async (s, e) =>
                {
                    try
                    {
                        _receiveTask = Task.Run(() => ReceiveMessagesAsync(_receiveCts.Token));
                        await LoadAvailableChatsAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatsPage] Ошибка загрузки: {ex.Message}");
                    }
                };

                Unloaded += (s, e) =>
                {
                    _uiTimer.Stop();
                    _receiveCts?.Cancel();
                    try
                    {
                        _receiveTask?.Wait(TimeSpan.FromSeconds(2));
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка инициализации: {ex.Message}");
            }
        }

        #region Прием сообщений от сервера

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("[ChatsPage] Начало приема сообщений");

            while (!cancellationToken.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                try
                {
                    var message = await GlobalMetods.ReceiveJsonAsync(_ws, _cts);

                    if (string.IsNullOrEmpty(message))
                    {
                        if (message == null)
                        {
                            Debug.WriteLine("[ChatsPage] Сервер закрыл соединение");
                            break;
                        }
                        continue;
                    }

                    Debug.WriteLine($"[ChatsPage] Получено сообщение: {message.Substring(0, Math.Min(200, message.Length))}...");

                    await ProcessIncomingMessageAsync(message);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[ChatsPage] Прием сообщений отменен");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ChatsPage] Ошибка приема: {ex.Message}");
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000);
                    }
                }
            }

            Debug.WriteLine("[ChatsPage] Прием сообщений завершен");
        }

        // Основной метод обработки входящих сообщений
        public async Task ProcessIncomingMessageAsync(string message)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(message);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("type", out var typeElement))
                {
                    var messageType = typeElement.GetString();
                    Debug.WriteLine($"[ChatsPage] Получен тип сообщения: {messageType}");

                    await ProcessMessageByTypeAsync(root, messageType);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обработки сообщения: {ex.Message}");
            }
        }

        private async Task ProcessMessageByTypeAsync(JsonElement root, string messageType)
        {
            switch (messageType)
            {
                case "CHAT_CREATED_AND_INVITED":
                    await ProcessChatCreatedAndInvitedAsync(root);
                    break;

                case "CHAT_LIST":
                    await ProcessChatListUpdateAsync(root);
                    break;

                case "CHAT_SELECTED":
                    await ProcessChatSelectedAsync(root);
                    break;

                case "MESSAGE_HISTORY":
                    await ProcessMessageHistoryUpdateAsync(root);
                    break;

                case "NEW_MESSAGE":
                    await ProcessNewMessageAsync(root);
                    break;

                case "CHAT_CREATED_WITH_USER":
                    await ProcessChatCreatedWithUserAsync(root);
                    break;

                case "MESSAGE_READ_CONFIRMATION":
                    await ProcessMessageReadConfirmationAsync(root);
                    break;

                case "MESSAGES_BATCH_READ":
                    await ProcessMessagesBatchReadAsync(root);
                    break;

                case "CHAT_CREATED":
                    await ProcessChatCreatedAsync(root);
                    break;

                case "PRIVATE_CHAT_CREATED":
                    await ProcessPrivateChatCreatedAsync(root);
                    break;

                case "JOINED_CHAT":
                    await ProcessJoinedChatAsync(root);
                    break;

                case "LEFT_CHAT":
                    await ProcessLeftChatAsync(root);
                    break;

                case "CHAT_MARKED_AS_READ":
                    await ProcessChatMarkedAsReadAsync(root);
                    break;

                case "NEW_CHAT_MESSAGE_NOTIFICATION":
                    await ProcessNewChatNotificationAsync(root);
                    break;

                case "SEARCH_RESULTS":
                case "GLOBAL_SEARCH_RESULTS":
                    await ProcessSearchResultsAsync(root);
                    break;

                default:
                    Debug.WriteLine($"[ChatsPage] Необработанный тип: {messageType}");
                    break;
            }
        }

        #endregion

        #region Свойства

        private ObservableCollection<Chat> _availableChats;
        public ObservableCollection<Chat> AvailableChats
        {
            get => _availableChats;
            set
            {
                _availableChats = value ?? new ObservableCollection<Chat>();
                OnPropertyChanged();
                FilterChats();
            }
        }

        private ObservableCollection<Chat> _filteredChats;
        public ObservableCollection<Chat> FilteredChats
        {
            get => _filteredChats ??= new ObservableCollection<Chat>();
            set
            {
                _filteredChats = value ?? new ObservableCollection<Chat>();
                OnPropertyChanged();
            }
        }

        private ObservableCollection<SearchResult> _searchResults;
        public ObservableCollection<SearchResult> SearchResults
        {
            get => _searchResults ??= new ObservableCollection<SearchResult>();
            set
            {
                _searchResults = value ?? new ObservableCollection<SearchResult>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentItems));
            }
        }

        public object CurrentItems => _isGlobalSearchMode ? (object)SearchResults : FilteredChats;

        public object SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem == value || _isSelectingChat) return;

                _selectedItem = value;
                OnPropertyChanged();

                if (value is Chat chat)
                {
                    SelectedChat = chat;
                }
                else if (value is SearchResult searchResult)
                {
                    HandleSearchResultSelection(searchResult);
                }
            }
        }

        private Chat _selectedChat;
        public Chat SelectedChat
        {
            get => _selectedChat;
            set
            {
                if (_selectedChat == value || _isSelectingChat) return;

                _selectedChat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChatTitle));
                OnPropertyChanged(nameof(IsChatSelected));

                if (value != null)
                {
                    _ = SelectChatAsync(value.Id);
                }
            }
        }

        private ObservableCollection<MessageUserInChat> _messageHistory;
        public ObservableCollection<MessageUserInChat> MessageHistory
        {
            get => _messageHistory;
            set
            {
                _messageHistory = value ?? new ObservableCollection<MessageUserInChat>();
                OnPropertyChanged();
            }
        }

        private string _currentMessage;
        public string CurrentMessage
        {
            get => _currentMessage;
            set
            {
                _currentMessage = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();

                if (!_isGlobalSearchMode)
                {
                    FilterChats();
                }
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsGlobalSearchMode
        {
            get => _isGlobalSearchMode;
            set
            {
                if (_isGlobalSearchMode != value)
                {
                    _isGlobalSearchMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentItems));
                    OnPropertyChanged(nameof(SearchInfo));

                    if (value && !string.IsNullOrWhiteSpace(_searchText))
                    {
                        _ = GlobalSearchAsync();
                    }
                    else if (!value)
                    {
                        ClearSearchResults();
                        FilterChats();
                    }
                }
            }
        }

        public string SearchInfo => _isGlobalSearchMode
            ? $"Глобальный поиск: {_searchText}"
            : "Локальный поиск в ваших чатах";

        public string ChatTitle => SelectedChat?.Name ?? "Выберите чат";
        public bool IsChatSelected => SelectedChat != null;
        public string UserName => _currentUser?.FullName ?? "Пользователь";

        #endregion

        #region Команды

        private ICommand _sendMessageCommand;
        public ICommand SendMessageCommand => _sendMessageCommand ??= new RelayCommand(
            async () => await SendMessageAsync(),
            () => !string.IsNullOrWhiteSpace(CurrentMessage) && IsChatSelected);

        private ICommand _executeSearchCommand;
        public ICommand ExecuteSearchCommand => _executeSearchCommand ??= new RelayCommand(
            async () => await ExecuteSearchAsync(),
            () => !string.IsNullOrWhiteSpace(SearchText));

        private ICommand _createChatCommand;
        public ICommand CreateChatCommand => _createChatCommand ??= new RelayCommand(
            () => ShowCreateChatDialog(),
            () => true);

        private ICommand _markAsReadCommand;
        public ICommand MarkAsReadCommand => _markAsReadCommand ??= new RelayCommand(
            async () => await MarkChatAsReadAsync(SelectedChat?.Id ?? 0),
            () => IsChatSelected && GetUnreadCountForChat(SelectedChat.Id) > 0);

        private ICommand _leaveChatCommand;
        public ICommand LeaveChatCommand => _leaveChatCommand ??= new RelayCommand(
            async () =>
            {
                if (SelectedChat != null)
                {
                    await LeaveChatAsync(SelectedChat.Id);
                }
            },
            () => IsChatSelected && (SelectedChat?.IsJoined ?? false));

        #endregion

        #region Основные методы

        private async Task ProcessChatCreatedWithUserAsync(JsonElement root)
        {
            try
            {
                SafeInvoke(async () =>
                {
                    if (root.TryGetProperty("chat", out var chatElement))
                    {
                        var chatJson = chatElement.ToString();
                        var newChat = JsonSerializer.Deserialize<Chat>(chatJson, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            PropertyNameCaseInsensitive = true
                        });

                        if (newChat != null)
                        {
                            await LoadAvailableChatsAsync();

                            var chat = AvailableChats.FirstOrDefault(c => c.Id == newChat.Id);
                            if (chat != null)
                            {
                                SelectedChat = chat;
                                await LoadMessageHistoryAsync();
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка создания чата: {ex.Message}");
            }
        }

        private async Task ProcessSearchResultsAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine("[ChatsPage] Обработка результатов поиска");

                SafeInvoke(() => SearchResults.Clear());

                if (root.TryGetProperty("results", out var resultsElement))
                {
                    var resultsJson = resultsElement.ToString();
                    Debug.WriteLine($"[ChatsPage] JSON результатов: {resultsJson}");

                    try
                    {
                        using var doc = JsonDocument.Parse(resultsJson);
                        var resultsArray = doc.RootElement.EnumerateArray();

                        SafeInvoke(() =>
                        {
                            bool hasResults = false;

                            foreach (var resultElement in resultsArray)
                            {
                                try
                                {
                                    var searchResult = new SearchResult();

                                    if (resultElement.TryGetProperty("type", out var typeElement))
                                        searchResult.Type = typeElement.GetString();

                                    if (resultElement.TryGetProperty("id", out var idElement))
                                        searchResult.Id = idElement.GetInt32();

                                    if (resultElement.TryGetProperty("name", out var nameElement))
                                        searchResult.Name = nameElement.GetString();

                                    if (resultElement.TryGetProperty("chatType", out var chatTypeElement))
                                    {
                                        if (chatTypeElement.ValueKind == JsonValueKind.String)
                                        {
                                            searchResult.ChatType = chatTypeElement.GetString();
                                        }
                                        else if (chatTypeElement.ValueKind == JsonValueKind.Object)
                                        {
                                            if (chatTypeElement.TryGetProperty("name", out var chatTypeName))
                                                searchResult.ChatType = chatTypeName.GetString();
                                        }
                                    }

                                    if (resultElement.TryGetProperty("description", out var descElement))
                                        searchResult.Description = descElement.GetString();

                                    if (resultElement.TryGetProperty("isJoined", out var joinedElement))
                                        searchResult.IsJoined = joinedElement.GetBoolean();

                                    if (resultElement.TryGetProperty("similarity", out var similarityElement))
                                        searchResult.Similarity = similarityElement.GetDouble();

                                    if (resultElement.TryGetProperty("memberCount", out var memberCountElement))
                                        searchResult.MemberCount = memberCountElement.GetInt32();

                                    if (resultElement.TryGetProperty("user", out var userElement))
                                    {
                                        try
                                        {
                                            var userJson = userElement.ToString();
                                            searchResult.User = JsonSerializer.Deserialize<UserObj>(userJson, new JsonSerializerOptions
                                            {
                                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                PropertyNameCaseInsensitive = true
                                            });
                                        }
                                        catch { }
                                    }

                                    SearchResults.Add(searchResult);
                                    hasResults = true;
                                    Debug.WriteLine($"[ChatsPage] Добавлен результат: {searchResult.Type} - {searchResult.Name}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[ChatsPage] Ошибка обработки элемента: {ex.Message}");
                                }
                            }

                            if (!hasResults)
                            {
                                SearchResults.Add(new SearchResult
                                {
                                    Type = "info",
                                    Name = "Ничего не найдено",
                                    Description = "Попробуйте изменить запрос поиска",
                                    Similarity = 0
                                });
                            }

                            OnPropertyChanged(nameof(SearchResults));
                            Debug.WriteLine($"[ChatsPage] Результатов поиска: {SearchResults.Count}");
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatsPage] Ошибка парсинга JSON: {ex.Message}");

                        SafeInvoke(() =>
                        {
                            SearchResults.Add(new SearchResult
                            {
                                Type = "info",
                                Name = "Ошибка данных",
                                Description = "Не удалось обработать результаты",
                                Similarity = 0
                            });
                        });
                    }
                }
                else
                {
                    Debug.WriteLine("[ChatsPage] В ответе нет свойства 'results'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обработки результатов поиска: {ex.Message}");
            }
        }

        private async Task ProcessChatCreatedAndInvitedAsync(JsonElement root)
        {
            try
            {
                SafeInvoke(async () =>
                {
                    if (root.TryGetProperty("chat", out var chatElement))
                    {
                        var chatJson = chatElement.ToString();
                        var newChat = JsonSerializer.Deserialize<Chat>(chatJson, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            PropertyNameCaseInsensitive = true
                        });

                        if (newChat != null)
                        {
                            await LoadAvailableChatsAsync();

                            var chat = AvailableChats.FirstOrDefault(c => c.Id == newChat.Id);
                            if (chat != null)
                            {
                                SelectedChat = chat;
                                await LoadMessageHistoryAsync();
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обработки создания чата: {ex.Message}");
            }
        }

        private async Task ProcessMessagesBatchReadAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine("[ChatsPage] Обработка массового прочтения сообщений");

                var chatId = root.GetProperty("chatId").GetInt32();
                var readByUserId = root.GetProperty("readByUserId").GetInt32();
                var readByUserName = root.GetProperty("readByUserName").GetString();
                var messageIds = root.GetProperty("messageIds").EnumerateArray()
                    .Select(x => x.GetInt32())
                    .ToList();

                SafeInvoke(() =>
                {
                    Debug.WriteLine($"[ChatsPage] {readByUserName} прочитал {messageIds.Count} сообщений в чате {chatId}");

                    if (SelectedChat != null && SelectedChat.Id == chatId)
                    {
                        bool updated = false;
                        foreach (var messageId in messageIds)
                        {
                            var message = MessageHistory.FirstOrDefault(m =>
                                m.IdMessage == messageId &&
                                m.User?.Id == _currentUser.Id);

                            if (message != null && message.Message != null && !message.Message.IsRead)
                            {
                                message.Message.IsRead = true;
                                updated = true;
                                Debug.WriteLine($"[ChatsPage] Статус нашего сообщения {messageId} обновлен на 'прочитано'");
                            }
                        }

                        if (updated)
                        {
                            UpdateMessageHistoryDisplay();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обработки массового прочтения: {ex.Message}");
            }
        }

        private void UpdateMessageHistoryDisplay()
        {
            try
            {
                var tempList = new ObservableCollection<MessageUserInChat>(MessageHistory);
                MessageHistory.Clear();
                foreach (var msg in tempList)
                {
                    MessageHistory.Add(msg);
                }
                Debug.WriteLine("[ChatsPage] Отображение истории сообщений обновлено");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обновления отображения: {ex.Message}");
            }
        }

        // Запрос списка доступных чатов
        private async Task LoadAvailableChatsAsync()
        {
            try
            {
                Debug.WriteLine("[ChatsPage] Запрашиваем список чатов...");

                var request = new
                {
                    type = "GET_CHATS"
                };

                var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                Debug.WriteLine($"[ChatsPage] Отправляем запрос: {requestJson}");
                await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка загрузки чатов: {ex.Message}");
            }
        }

        // Выбор чата для просмотра
        private async Task SelectChatAsync(int chatId)
        {
            try
            {
                _isSelectingChat = true;

                SafeInvoke(() =>
                {
                    MessageHistory.Clear();
                });

                _chatLastSeen[chatId] = DateTime.Now;

                var request = new
                {
                    type = "SELECT_CHAT",
                    chatId = chatId
                };

                var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);

                await Task.Delay(300);
                await LoadMessageHistoryAsync();

                await MarkChatAsReadAsync(chatId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка выбора чата: {ex.Message}");
            }
            finally
            {
                _isSelectingChat = false;
            }
        }

        // Загрузка истории сообщений выбранного чата
        private async Task LoadMessageHistoryAsync()
        {
            if (SelectedChat == null) return;

            try
            {
                Debug.WriteLine($"[ChatsPage] Запрашиваем историю для чата {SelectedChat.Id}");

                var request = new
                {
                    type = "GET_HISTORY"
                };

                var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка загрузки истории: {ex.Message}");
            }
        }

        // Отправка текстового сообщения
        public async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentMessage) || SelectedChat == null)
                return;

            try
            {
                var messageToSend = CurrentMessage.Trim();

                var tempMessage = new MessageUserInChat
                {
                    IdChat = SelectedChat.Id,
                    IdUser = _currentUser.Id,
                    IdMessage = -1,
                    Message = new MessageObj
                    {
                        Id = -1,
                        Data = messageToSend,
                        DateAndTime = DateTime.Now,
                        IsRead = true
                    },
                    User = new UserObj
                    {
                        Id = _currentUser.Id,
                        Name = _currentUser.Name,
                        Surname = _currentUser.Surname,
                        UrlAvatar = _currentUser.UrlAvatar
                    }
                };

                SafeInvoke(() =>
                {
                    MessageHistory.Add(tempMessage);
                    CurrentMessage = "";
                    MessageScrollViewer.ScrollToEnd();
                });

                var request = new
                {
                    type = "TEXT_MESSAGE",
                    content = messageToSend,
                    chatId = SelectedChat.Id
                };

                var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);
                Debug.WriteLine($"[ChatsPage] Сообщение отправлено на сервер: {messageToSend}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка отправки сообщения: {ex.Message}");
                CurrentMessage = "";
            }
        }

        #endregion

        #region Обработка входящих сообщений

        // Обновление списка чатов
        private async Task ProcessChatListUpdateAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine("[ChatsPage] Обработка списка чатов");

                if (root.TryGetProperty("chats", out var chatsElement))
                {
                    var chatsJson = chatsElement.ToString();
                    Debug.WriteLine($"[ChatsPage] Получены чаты: {chatsJson}");

                    var chats = JsonSerializer.Deserialize<List<Chat>>(chatsJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    });

                    SafeInvoke(() =>
                    {
                        if (chats != null)
                        {
                            Debug.WriteLine($"[ChatsPage] Десериализовано {chats.Count} чатов");

                            var currentSelectedId = SelectedChat?.Id;

                            AvailableChats.Clear();
                            _displayedChatIds.Clear();

                            foreach (var chat in chats)
                            {
                                chat.UnreadCount = GetUnreadCountForChat(chat.Id);
                                AvailableChats.Add(chat);
                                _displayedChatIds.Add(chat.Id);
                                Debug.WriteLine($"[ChatsPage] Добавлен чат: {chat.Name} (ID: {chat.Id}, Unread: {chat.UnreadCount})");
                            }

                            if (currentSelectedId.HasValue)
                            {
                                var chatToSelect = AvailableChats.FirstOrDefault(c => c.Id == currentSelectedId.Value);
                                if (chatToSelect != null)
                                {
                                    SelectedChat = chatToSelect;
                                }
                            }

                            FilterChats();
                            Debug.WriteLine($"[ChatsPage] Список чатов обновлен. Всего: {AvailableChats.Count}");
                        }
                        else
                        {
                            Debug.WriteLine("[ChatsPage] Не удалось десериализовать чаты");
                        }
                    });
                }
                else
                {
                    Debug.WriteLine("[ChatsPage] В ответе нет свойства 'chats'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обновления списка чатов: {ex.Message}");
            }
        }

        private async Task ProcessChatSelectedAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();
                var chatName = root.GetProperty("chatName").GetString();
                var message = root.GetProperty("message").GetString();

                SafeInvoke(() =>
                {
                    Debug.WriteLine($"[ChatsPage] Чат выбран: {chatName}");
                    var chat = AvailableChats.FirstOrDefault(c => c.Id == chatId);
                    if (chat != null)
                    {
                        SelectedChat = chat;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка выбора чата: {ex.Message}");
            }
        }

        // Обновление истории сообщений
        private async Task ProcessMessageHistoryUpdateAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine("[ChatsPage] Обработка истории сообщений");

                SafeInvoke(() => MessageHistory.Clear());

                if (root.TryGetProperty("messages", out var messagesElement))
                {
                    var messagesJson = messagesElement.ToString();
                    Debug.WriteLine($"[ChatsPage] Длина JSON истории: {messagesJson.Length}");

                    var newMessages = JsonSerializer.Deserialize<List<MessageUserInChat>>(messagesJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    });

                    if (newMessages != null)
                    {
                        Debug.WriteLine($"[ChatsPage] Десериализовано сообщений: {newMessages.Count}");

                        SafeInvoke(() =>
                        {
                            var sortedMessages = newMessages
                                .Where(m => m != null && m.Message != null && m.User != null)
                                .OrderBy(m => m.Message.DateAndTime)
                                .ToList();

                            foreach (var message in sortedMessages)
                            {
                                MessageHistory.Add(message);

                                if (message.User.Id != _currentUser.Id &&
                                    message.Message.IsRead == false &&
                                    !IsMessageRead(message.IdMessage, message.IdChat))
                                {
                                    AddUnreadMessage(message.IdChat, message.IdMessage);
                                }
                            }

                            if (SelectedChat != null)
                            {
                                SelectedChat.UnreadCount = GetUnreadCountForChat(SelectedChat.Id);
                                OnPropertyChanged(nameof(AvailableChats));
                                FilterChats();
                            }

                            MessageScrollViewer.ScrollToEnd();
                            Debug.WriteLine($"[ChatsPage] Добавлено {MessageHistory.Count} сообщений в историю");
                        });
                    }
                    else
                    {
                        Debug.WriteLine("[ChatsPage] Нет сообщений для десериализации");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка загрузки истории: {ex.Message}");
            }
        }

        // Обработка нового входящего сообщения
        private async Task ProcessNewMessageAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine("[ChatsPage] Обработка нового сообщения");

                if (!root.TryGetProperty("message", out var messageElement))
                {
                    Debug.WriteLine("[ChatsPage] В JSON нет свойства 'message'");
                    return;
                }

                var messageJson = messageElement.ToString();
                Debug.WriteLine($"[ChatsPage] JSON сообщения: {messageJson}");

                var newMessage = new MessageUserInChat();

                using var messageDoc = JsonDocument.Parse(messageJson);
                var messageRoot = messageDoc.RootElement;

                if (messageRoot.TryGetProperty("idMessage", out var idMessageElem))
                    newMessage.IdMessage = idMessageElem.GetInt32();

                if (messageRoot.TryGetProperty("idChat", out var idChatElem))
                    newMessage.IdChat = idChatElem.GetInt32();

                if (messageRoot.TryGetProperty("idUser", out var idUserElem))
                    newMessage.IdUser = idUserElem.GetInt32();

                if (messageRoot.TryGetProperty("message", out var messageObjElem))
                {
                    newMessage.Message = new MessageObj();

                    if (messageObjElem.TryGetProperty("id", out var msgIdElem))
                        newMessage.Message.Id = msgIdElem.GetInt32();

                    if (messageObjElem.TryGetProperty("data", out var dataElem))
                        newMessage.Message.Data = dataElem.GetString();

                    if (messageObjElem.TryGetProperty("dateAndTime", out var dateElem))
                        newMessage.Message.DateAndTime = dateElem.GetDateTime();

                    if (messageObjElem.TryGetProperty("isDeleted", out var isDeletedElem))
                        newMessage.Message.IsDeleted = isDeletedElem.GetBoolean();

                    if (messageObjElem.TryGetProperty("isUpdate", out var isUpdateElem))
                        newMessage.Message.IsUpdate = isUpdateElem.GetBoolean();

                    if (messageObjElem.TryGetProperty("isRead", out var isReadElem))
                        newMessage.Message.IsRead = isReadElem.GetBoolean();
                }

                if (messageRoot.TryGetProperty("user", out var userObjElem))
                {
                    newMessage.User = new UserObj();

                    if (userObjElem.TryGetProperty("id", out var userIdElem))
                        newMessage.User.Id = userIdElem.GetInt32();

                    if (userObjElem.TryGetProperty("login", out var loginElem))
                        newMessage.User.Login = loginElem.GetString();

                    if (userObjElem.TryGetProperty("name", out var nameElem))
                        newMessage.User.Name = nameElem.GetString();

                    if (userObjElem.TryGetProperty("surname", out var surnameElem))
                        newMessage.User.Surname = surnameElem.GetString();

                    if (userObjElem.TryGetProperty("secondSurname", out var secondSurnameElem))
                        newMessage.User.SecondSurname = secondSurnameElem.GetString();

                    if (userObjElem.TryGetProperty("urlAvatar", out var avatarElem))
                        newMessage.User.UrlAvatar = avatarElem.GetString();
                }

                if (newMessage.Message == null || newMessage.User == null)
                {
                    Debug.WriteLine("[ChatsPage] Не удалось получить все данные сообщения");
                    return;
                }

                SafeInvoke(() =>
                {
                    bool isOwnMessage = newMessage.User.Id == _currentUser.Id;
                    bool isCurrentChat = SelectedChat != null && newMessage.IdChat == SelectedChat.Id;

                    Debug.WriteLine($"[ChatsPage] Новое сообщение: от {newMessage.User.Name}, мое: {isOwnMessage}, текущий чат: {isCurrentChat}");

                    if (isOwnMessage)
                    {
                        var existingTempMessage = MessageHistory.FirstOrDefault(m =>
                            m.Message?.Id == -1 &&
                            Math.Abs((m.Message?.DateAndTime - newMessage.Message.DateAndTime).Value.TotalSeconds) < 2 &&
                            m.User?.Id == _currentUser.Id);

                        if (existingTempMessage != null)
                        {
                            var index = MessageHistory.IndexOf(existingTempMessage);
                            MessageHistory.RemoveAt(index);
                            MessageHistory.Insert(index, newMessage);
                            Debug.WriteLine($"[ChatsPage] Заменено временное сообщение на постоянное ID: {newMessage.IdMessage}");
                        }
                        else if (isCurrentChat)
                        {
                            MessageHistory.Add(newMessage);
                            Debug.WriteLine($"[ChatsPage] Добавлено наше сообщение ID: {newMessage.IdMessage}");
                        }
                    }
                    else
                    {
                        if (isCurrentChat)
                        {
                            MessageHistory.Add(newMessage);
                            Debug.WriteLine($"[ChatsPage] Добавлено чужое сообщение ID: {newMessage.IdMessage} в текущий чат");

                            _ = MarkMessageAsReadAsync(newMessage.IdMessage, newMessage.IdChat);

                            var chat = AvailableChats.FirstOrDefault(c => c.Id == newMessage.IdChat);
                            if (chat != null && chat.UnreadCount > 0)
                            {
                                chat.UnreadCount--;
                                OnPropertyChanged(nameof(AvailableChats));
                                FilterChats();
                            }
                        }
                        else
                        {
                            AddUnreadMessage(newMessage.IdChat, newMessage.IdMessage);
                            Debug.WriteLine($"[ChatsPage] Увеличено непрочитанное для чата {newMessage.IdChat}");

                            var chat = AvailableChats.FirstOrDefault(c => c.Id == newMessage.IdChat);
                            if (chat != null)
                            {
                                chat.UnreadCount = GetUnreadCountForChat(chat.Id);
                                OnPropertyChanged(nameof(AvailableChats));
                                FilterChats();
                            }
                        }
                    }

                    if (isCurrentChat)
                    {
                        MessageScrollViewer.ScrollToEnd();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обработки нового сообщения: {ex.Message}");
            }
        }

        private void UpdateMessageReadStatus(int chatId, int messageId, bool isRead)
        {
            SafeInvoke(() =>
            {
                if (SelectedChat != null && SelectedChat.Id == chatId)
                {
                    var message = MessageHistory.FirstOrDefault(m => m.IdMessage == messageId);
                    if (message != null && message.Message != null)
                    {
                        message.Message.IsRead = isRead;

                        var index = MessageHistory.IndexOf(message);
                        if (index >= 0)
                        {
                            MessageHistory.RemoveAt(index);
                            MessageHistory.Insert(index, message);
                        }
                    }
                }
            });
        }

        // Отметка сообщения как прочитанного
        private async Task MarkMessageAsReadAsync(int messageId, int chatId)
        {
            try
            {
                var request = new
                {
                    type = "MARK_AS_READ",
                    messageId = messageId
                };

                var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);
                Debug.WriteLine($"[ChatsPage] Отправлено подтверждение прочтения сообщения {messageId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка отправки подтверждения прочтения: {ex.Message}");
            }
        }

        private async Task ProcessChatCreatedAsync(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("chat", out var chatElement))
                {
                    var chatJson = chatElement.ToString();
                    var newChat = JsonSerializer.Deserialize<Chat>(chatJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    });

                    SafeInvoke(() =>
                    {
                        if (newChat != null && !_displayedChatIds.Contains(newChat.Id))
                        {
                            AvailableChats.Add(newChat);
                            _displayedChatIds.Add(newChat.Id);
                            FilterChats();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка создания чата: {ex.Message}");
            }
        }

        private async Task ProcessPrivateChatCreatedAsync(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("chat", out var chatElement))
                {
                    var chatJson = chatElement.ToString();
                    var newChat = JsonSerializer.Deserialize<Chat>(chatJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    });

                    SafeInvoke(async () =>
                    {
                        if (newChat != null)
                        {
                            await LoadAvailableChatsAsync();
                            await Task.Delay(500);

                            var chat = AvailableChats.FirstOrDefault(c => c.Id == newChat.Id);
                            if (chat != null)
                            {
                                SelectedChat = chat;
                                await LoadMessageHistoryAsync();
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка приватного чата: {ex.Message}");
            }
        }

        private async Task ProcessJoinedChatAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();
                var message = root.GetProperty("message").GetString();

                await LoadAvailableChatsAsync();

                SafeInvoke(() =>
                {
                    DispatcherTimer timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(500);
                    int attempts = 0;
                    timer.Tick += async (sender, e) =>
                    {
                        attempts++;
                        try
                        {
                            var joinedChat = AvailableChats.FirstOrDefault(c => c.Id == chatId);
                            if (joinedChat != null)
                            {
                                timer.Stop();
                                IsGlobalSearchMode = false;
                                SelectedChat = joinedChat;
                                await Task.Delay(300);
                                await LoadMessageHistoryAsync();
                            }
                            else if (attempts >= 5)
                            {
                                timer.Stop();
                            }
                            else
                            {
                                await LoadAvailableChatsAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            timer.Stop();
                            Debug.WriteLine($"[ChatsPage] Ошибка: {ex.Message}");
                        }
                    };
                    timer.Start();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка присоединения: {ex.Message}");
            }
        }

        private async Task ProcessLeftChatAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();
                var message = root.GetProperty("message").GetString();

                SafeInvoke(async () =>
                {
                    var chatToRemove = AvailableChats.FirstOrDefault(c => c.Id == chatId);
                    if (chatToRemove != null)
                    {
                        AvailableChats.Remove(chatToRemove);
                        _displayedChatIds.Remove(chatId);
                    }

                    if (SelectedChat != null && SelectedChat.Id == chatId)
                    {
                        SelectedChat = null;
                        MessageHistory.Clear();
                        OnPropertyChanged(nameof(ChatTitle));
                        OnPropertyChanged(nameof(IsChatSelected));
                    }

                    FilterChats();
                    await Task.Delay(100);
                    await LoadAvailableChatsAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка выхода: {ex.Message}");
            }
        }

        private async Task ProcessChatMarkedAsReadAsync(JsonElement root)
        {
            try
            {
                var chatId = root.GetProperty("chatId").GetInt32();

                SafeInvoke(() =>
                {
                    var chat = AvailableChats.FirstOrDefault(c => c.Id == chatId);
                    if (chat != null)
                    {
                        chat.UnreadCount = 0;
                        OnPropertyChanged(nameof(AvailableChats));
                        FilterChats();
                    }

                    MarkAllMessagesAsRead(chatId);

                    if (SelectedChat != null && SelectedChat.Id == chatId)
                    {
                        foreach (var message in MessageHistory)
                        {
                            if (message.Message != null)
                            {
                                message.Message.IsRead = true;
                            }
                        }

                        var tempList = new ObservableCollection<MessageUserInChat>(MessageHistory);
                        MessageHistory.Clear();
                        foreach (var msg in tempList)
                        {
                            MessageHistory.Add(msg);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка отметки чата: {ex.Message}");
            }
        }

        private async Task ProcessMessageReadConfirmationAsync(JsonElement root)
        {
            try
            {
                Debug.WriteLine("[ChatsPage] Обработка подтверждения прочтения");

                var messageId = root.GetProperty("messageId").GetInt32();
                var chatId = root.GetProperty("chatId").GetInt32();
                var readByUserName = root.GetProperty("readByUserName").GetString();
                var readByUserLogin = root.GetProperty("readByUserLogin").GetString();

                SafeInvoke(() =>
                {
                    Debug.WriteLine($"[ChatsPage] {readByUserName} прочитал ваше сообщение #{messageId} в чате {chatId}");

                    if (SelectedChat != null && SelectedChat.Id == chatId)
                    {
                        var ourMessage = MessageHistory.FirstOrDefault(m =>
                            m.IdMessage == messageId &&
                            m.User?.Id == _currentUser.Id);

                        if (ourMessage != null && ourMessage.Message != null && !ourMessage.Message.IsRead)
                        {
                            ourMessage.Message.IsRead = true;
                            UpdateMessageHistoryDisplay();
                            Debug.WriteLine($"[ChatsPage] Статус сообщения {messageId} обновлен: прочитано {readByUserName}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обработки подтверждения прочтения: {ex.Message}");
            }
        }

        private async Task ProcessNewChatNotificationAsync(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("chatId", out var chatIdElement))
                {
                    var chatId = chatIdElement.GetInt32();
                    IncrementUnreadCount(chatId);

                    SafeInvoke(() =>
                    {
                        var chat = AvailableChats.FirstOrDefault(c => c.Id == chatId);
                        if (chat != null)
                        {
                            chat.UnreadCount = GetUnreadCountForChat(chatId);
                            OnPropertyChanged(nameof(AvailableChats));
                            FilterChats();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка обработки уведомления: {ex.Message}");
            }
        }

        #endregion

        #region Управление непрочитанными сообщениями

        private void AddUnreadMessage(int chatId, int messageId)
        {
            if (!_unreadCounts.ContainsKey(chatId))
            {
                _unreadCounts[chatId] = 0;
            }

            if (!IsMessageRead(messageId, chatId))
            {
                _unreadCounts[chatId]++;

                if (!_readMessageIds.ContainsKey(chatId))
                {
                    _readMessageIds[chatId] = new List<int>();
                }
            }
        }

        private void IncrementUnreadCount(int chatId)
        {
            if (!_unreadCounts.ContainsKey(chatId))
            {
                _unreadCounts[chatId] = 0;
            }
            _unreadCounts[chatId]++;
        }

        private void MarkMessageAsRead(int chatId, int messageId)
        {
            if (_unreadCounts.ContainsKey(chatId) && _unreadCounts[chatId] > 0)
            {
                _unreadCounts[chatId]--;

                if (!_readMessageIds.ContainsKey(chatId))
                {
                    _readMessageIds[chatId] = new List<int>();
                }

                if (!_readMessageIds[chatId].Contains(messageId))
                {
                    _readMessageIds[chatId].Add(messageId);
                }
            }
        }

        private void MarkAllMessagesAsRead(int chatId)
        {
            if (_unreadCounts.ContainsKey(chatId))
            {
                _unreadCounts[chatId] = 0;
            }

            if (!_readMessageIds.ContainsKey(chatId))
            {
                _readMessageIds[chatId] = new List<int>();
            }

            _chatLastSeen[chatId] = DateTime.Now;
        }

        private int GetUnreadCountForChat(int chatId)
        {
            return _unreadCounts.TryGetValue(chatId, out var count) ? count : 0;
        }

        private bool IsMessageRead(int messageId, int chatId)
        {
            return _readMessageIds.TryGetValue(chatId, out var readMessages) &&
                   readMessages.Contains(messageId);
        }

        #endregion

        #region Вспомогательные методы

        // Фильтрация списка чатов
        private void FilterChats()
        {
            SafeInvoke(() =>
            {
                FilteredChats.Clear();

                if (AvailableChats == null || !AvailableChats.Any())
                    return;

                IEnumerable<Chat> chatsToShow = _isGlobalSearchMode ?
                    AvailableChats :
                    AvailableChats.Where(c => c.IsJoined == true);

                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    foreach (var chat in chatsToShow)
                    {
                        FilteredChats.Add(chat);
                    }
                }
                else
                {
                    var searchLower = SearchText.ToLower();
                    foreach (var chat in chatsToShow.Where(c =>
                        c.Name.ToLower().Contains(searchLower) ||
                        (c.ChatType?.Name?.ToLower()?.Contains(searchLower) ?? false)))
                    {
                        FilteredChats.Add(chat);
                    }
                }

                if (!FilteredChats.Any() && SelectedChat != null)
                {
                    SelectedChat = null;
                    MessageHistory.Clear();
                    OnPropertyChanged(nameof(ChatTitle));
                    OnPropertyChanged(nameof(IsChatSelected));
                }
            });
        }

        private void ClearSearchResults()
        {
            SafeInvoke(() => SearchResults.Clear());
        }

        // Глобальный поиск по всем чатам и пользователям
        private async Task GlobalSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            try
            {
                Debug.WriteLine($"[ChatsPage] Выполняю глобальный поиск: '{SearchText}'");

                var request = new
                {
                    type = "GLOBAL_SEARCH",
                    searchTerm = SearchText,
                    threshold = 0.2,
                    maxResults = 50
                };

                var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                Debug.WriteLine($"[ChatsPage] Отправляю запрос: {requestJson}");

                await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);

                SafeInvoke(() =>
                {
                    SearchResults.Clear();
                    SearchResults.Add(new SearchResult
                    {
                        Type = "info",
                        Name = "Поиск...",
                        Description = "Выполняется поиск, пожалуйста подождите",
                        Similarity = 0
                    });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка поиска: {ex.Message}");
            }
        }

        private async Task ExecuteSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            if (_isGlobalSearchMode)
            {
                await GlobalSearchAsync();
            }
            else
            {
                FilterChats();
            }
        }

        private void SafeInvoke(Action action)
        {
            try
            {
                if (Application.Current?.Dispatcher == null)
                    return;

                if (Application.Current.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(action);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SafeInvoke error: {ex.Message}");
            }
        }

        private void ProcessUIActions()
        {
        }

        #endregion

        #region Методы работы с чатами

        // Отметка всех сообщений чата как прочитанных
        private async Task MarkChatAsReadAsync(int chatId)
        {
            try
            {
                var chatRequest = new
                {
                    type = "MARK_CHAT_AS_READ",
                    chatId = chatId
                };

                var chatRequestJson = JsonSerializer.Serialize(chatRequest);
                await GlobalMetods.SendJsonAsync(_ws, _cts, chatRequestJson);
                Debug.WriteLine($"[ChatsPage] Отправлен запрос на отметку чата {chatId} как прочитанного");

                SafeInvoke(() =>
                {
                    MarkAllMessagesAsRead(chatId);

                    var chat = AvailableChats.FirstOrDefault(c => c.Id == chatId);
                    if (chat != null)
                    {
                        chat.UnreadCount = 0;
                        OnPropertyChanged(nameof(AvailableChats));
                        FilterChats();
                    }

                    if (SelectedChat != null && SelectedChat.Id == chatId)
                    {
                        foreach (var message in MessageHistory)
                        {
                            if (message.Message != null && message.User?.Id != _currentUser.Id)
                            {
                                message.Message.IsRead = true;
                            }
                        }
                        UpdateMessageHistoryDisplay();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка отметки чата: {ex.Message}");
            }
        }

        private async Task JoinChatAsync(int chatId)
        {
            try
            {
                var request = new
                {
                    type = "JOIN_CHAT",
                    chatId = chatId
                };
                await GlobalMetods.SendJsonAsync(_ws, _cts, JsonSerializer.Serialize(request));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка присоединения: {ex.Message}");
            }
        }

        // Выход из чата
        private async Task LeaveChatAsync(int chatId)
        {
            try
            {
                var request = new
                {
                    type = "LEAVE_CHAT",
                    chatId = chatId
                };
                await GlobalMetods.SendJsonAsync(_ws, _cts, JsonSerializer.Serialize(request));

                SafeInvoke(() =>
                {
                    var chatToRemove = AvailableChats.FirstOrDefault(c => c.Id == chatId);
                    if (chatToRemove != null)
                    {
                        AvailableChats.Remove(chatToRemove);
                        _displayedChatIds.Remove(chatId);
                    }

                    if (SelectedChat != null && SelectedChat.Id == chatId)
                    {
                        SelectedChat = null;
                        MessageHistory.Clear();
                        OnPropertyChanged(nameof(ChatTitle));
                        OnPropertyChanged(nameof(IsChatSelected));
                    }

                    FilterChats();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка выхода: {ex.Message}");
            }
        }

        // Создание простого чата
        private async Task CreateSimpleChatAsync(string name, int chatTypeId = 1)
        {
            try
            {
                Debug.WriteLine($"[ChatsPage] Создание чата: '{name}', тип: {chatTypeId}");

                var request = new
                {
                    type = "CREATE_CHAT",
                    name = name,
                    chatTypeId = chatTypeId
                };

                var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                Debug.WriteLine($"[ChatsPage] Отправка запроса: {requestJson}");
                await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка создания чата: {ex.Message}");
            }
        }

        private async Task CreatePrivateChatAsync(int targetUserId)
        {
            try
            {
                var request = new
                {
                    type = "CREATE_PRIVATE_CHAT",
                    targetUserId = targetUserId
                };
                await GlobalMetods.SendJsonAsync(_ws, _cts, JsonSerializer.Serialize(request));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка создания приватного чата: {ex.Message}");
            }
        }

        private void ShowCreateChatDialog()
        {
            var dialog = new Window
            {
                Title = "Создать чат",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            var stackPanel = new StackPanel { Margin = new Thickness(10) };
            var nameLabel = new TextBlock { Text = "Название чата:" };
            var nameTextBox = new TextBox { Margin = new Thickness(0, 5, 0, 10) };
            var typeLabel = new TextBlock { Text = "Тип чата:" };
            var typeComboBox = new ComboBox { Margin = new Thickness(0, 5, 0, 10) };
            typeComboBox.Items.Add(new ComboBoxItem { Content = "Групповой чат", Tag = 1 });
            typeComboBox.Items.Add(new ComboBoxItem { Content = "Приватный чат", Tag = 2 });
            typeComboBox.SelectedIndex = 0;

            var buttonStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var createButton = new Button { Content = "Создать", Width = 80, Margin = new Thickness(5, 0, 0, 0) };
            var cancelButton = new Button { Content = "Отмена", Width = 80 };

            buttonStack.Children.Add(cancelButton);
            buttonStack.Children.Add(createButton);
            stackPanel.Children.Add(nameLabel);
            stackPanel.Children.Add(nameTextBox);
            stackPanel.Children.Add(typeLabel);
            stackPanel.Children.Add(typeComboBox);
            stackPanel.Children.Add(buttonStack);
            dialog.Content = stackPanel;

            createButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(nameTextBox.Text))
                {
                    return;
                }

                var selectedItem = typeComboBox.SelectedItem as ComboBoxItem;
                int chatTypeId = selectedItem?.Tag as int? ?? 1;
                _ = CreateSimpleChatAsync(nameTextBox.Text.Trim(), chatTypeId);
                dialog.Close();
            };

            cancelButton.Click += (s, e) => dialog.Close();
            dialog.ShowDialog();
        }

        #endregion

        #region Обработчики событий

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && SendMessageCommand.CanExecute(null))
                {
                    SendMessageCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private async void RefreshChatsButton_Click(object sender, RoutedEventArgs e)
        {
            await ForceRefreshChatsAsync();
        }

        private async Task ForceRefreshChatsAsync()
        {
            SafeInvoke(() =>
            {
                AvailableChats.Clear();
                _displayedChatIds.Clear();
                FilteredChats.Clear();
            });

            await LoadAvailableChatsAsync();
            await Task.Delay(1000);
        }

        // Обработка выбора результата поиска
        private async void HandleSearchResultSelection(SearchResult searchResult)
        {
            Debug.WriteLine($"[ChatsPage] Выбран результат: {searchResult.Type} - {searchResult.Name}");

            if (searchResult.Type == "chat")
            {
                var existingChat = AvailableChats.FirstOrDefault(c => c.Id == searchResult.Id);

                if (existingChat != null && existingChat.IsJoined.GetValueOrDefault())
                {
                    SelectedChat = existingChat;
                    IsGlobalSearchMode = false;
                    SearchText = "";
                    await LoadMessageHistoryAsync();
                    Debug.WriteLine($"[ChatsPage] Выбран существующий чат: {existingChat.Name}");
                }
                else
                {
                    await JoinChatAndSwitchAsync(searchResult);
                }
            }
            else if (searchResult.Type == "user" && searchResult.User != null)
            {
                Debug.WriteLine($"[ChatsPage] Создание чата с пользователем: {searchResult.User.Name}");
                await ShowCreateChatDialogAsync(searchResult.User);
            }
            else if (searchResult.Type == "info")
            {
                Debug.WriteLine($"[ChatsPage] Информационный результат: {searchResult.Name}");
            }
        }

        private async Task JoinChatAndSwitchAsync(SearchResult searchResult)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                Debug.WriteLine($"[ChatsPage] Присоединяюсь к чату: {searchResult.Id}");

                await JoinChatAsync(searchResult.Id);

                await Task.Delay(1000);
                await ForceRefreshChatsAsync();

                var joinedChat = AvailableChats.FirstOrDefault(c => c.Id == searchResult.Id);
                if (joinedChat != null)
                {
                    IsGlobalSearchMode = false;
                    SearchText = "";
                    SelectedChat = joinedChat;
                    await LoadMessageHistoryAsync();
                    Debug.WriteLine($"[ChatsPage] Успешно присоединился к чату: {joinedChat.Name}");
                }
                else
                {
                    Debug.WriteLine($"[ChatsPage] Не удалось найти чат после присоединения");
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async Task ShowCreateChatDialogAsync(UserObj targetUser)
        {
            var dialog = new CreateChatFromSearchDialog(
                targetUser.Id,
                targetUser.Name,
                targetUser.Login ?? "");

            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    Debug.WriteLine($"[ChatsPage] Создаем чат с {targetUser.Name}, тип: {dialog.SelectedChatTypeId}");

                    await CreateChatWithUserAsync(dialog.ChatName.Trim(), dialog.SelectedChatTypeId, targetUser.Id);

                    IsGlobalSearchMode = false;
                    SearchText = "";

                    await Task.Delay(500);
                    await LoadAvailableChatsAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ChatsPage] Ошибка создания чата: {ex.Message}");
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
            else
            {
                Debug.WriteLine($"[ChatsPage] Пользователь отменил создание чата");
            }
        }

        private async Task CreateChatWithUserAsync(string chatName, int chatTypeId, int invitedUserId)
        {
            try
            {
                Debug.WriteLine($"[ChatsPage] Создаем чат с пользователем {invitedUserId}, название: {chatName}, тип: {chatTypeId}");

                if (chatTypeId == 2) // Приватный чат
                {
                    await CreatePrivateChatAsync(invitedUserId);
                }
                else // Групповой чат
                {
                    var request = new
                    {
                        type = "CREATE_CHAT_AND_INVITE",
                        chatName = chatName,
                        chatTypeId = chatTypeId,
                        invitedUserId = invitedUserId
                    };

                    var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    await GlobalMetods.SendJsonAsync(_ws, _cts, requestJson);
                    Debug.WriteLine($"[ChatsPage] Отправлен запрос на создание чата с приглашением");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatsPage] Ошибка создания чата с пользователем: {ex.Message}");
                throw;
            }
        }

        #endregion
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
    }
}