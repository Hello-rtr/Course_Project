using Client_My_Messenger.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Client_My_Messenger.Pages
{
    /// <summary>
    /// Логика взаимодействия для AutorizationPage.xaml
    /// </summary>
    public partial class AutorizationPage : Page
    {
        public static ClientWebSocket ws;
        public static CancellationTokenSource cts;
        public static User currentUser = null!;
        private static bool isRunning = true;
        private static bool isAuthenticated = false;
        private bool _isRegistrationMode = false;

        private string _lastLoginFilePath;

        public AutorizationPage(ClientWebSocket ws_, CancellationTokenSource cts_)
        {
            InitializeComponent();
            ws = ws_;
            cts = cts_;

            // Путь к файлу для сохранения последнего логина
            _lastLoginFilePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyMessenger",
                "lastlogin.txt");

            UpdateUIForMode();
        }

        private void UpdateUIForMode()
        {
            if (_isRegistrationMode)
            {
                Title = "Регистрация";
                ActionButton.Content = "Зарегистрироваться";
                ModeToggleButton.Content = "Уже есть аккаунт? Войти";

                NamePanel.Visibility = Visibility.Visible;
                SurnamePanel.Visibility = Visibility.Visible;
                SecondSurnamePanel.Visibility = Visibility.Visible;

                LoginText.Text = "";
                PasswordText.Password = "";
                NameText.Text = "";
                SurnameText.Text = "";
                SecondSurnameText.Text = "";
            }
            else
            {
                Title = "Авторизация";
                ActionButton.Content = "Войти";
                ModeToggleButton.Content = "Нет аккаунта? Зарегистрироваться";

                NamePanel.Visibility = Visibility.Collapsed;
                SurnamePanel.Visibility = Visibility.Collapsed;
                SecondSurnamePanel.Visibility = Visibility.Collapsed;

                NameText.Text = "";
                SurnameText.Text = "";
                SecondSurnameText.Text = "";
            }
        }

        private async Task HandleAuthAsync(string login, string password, string name = null, string surname = null, string secondSurname = null)
        {
            try
            {
                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Логин и пароль не могут быть пустыми", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_isRegistrationMode && string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("Имя обязательно при регистрации", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Проверка длины логина
                if (login.Length < 3 || login.Length > 15)
                {
                    MessageBox.Show("Логин должен содержать от 3 до 15 символов", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Проверка длины пароля
                if (password.Length < 3 || password.Length > 20)
                {
                    MessageBox.Show("Пароль должен содержать от 3 до 20 символов", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Отправляем команду аутентификации
                var authRequest = new
                {
                    type = "AUTH",
                    login = login,
                    password = password,
                    name = name,
                    surname = surname,
                    secondSurname = secondSurname
                };

                var authJson = JsonSerializer.Serialize(authRequest, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await GlobalMetods.SendJsonAsync(ws, cts, authJson);

                // Получаем ответ от сервера
                var response = await GlobalMetods.ReceiveJsonAsync(ws, cts);

                if (response != null)
                {
                    await ProcessAuthResponse(response);
                }
                else
                {
                    MessageBox.Show("Нет ответа от сервера", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (WebSocketException ex)
            {
                MessageBox.Show($"Ошибка соединения: {ex.Message}", "Ошибка сети", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод обработки ответа аутентификации
        private async Task ProcessAuthResponse(string response)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("status", out var statusElement) &&
                    statusElement.GetString()?.ToLower() == "ok")
                {
                    isAuthenticated = true;

                    // Десериализуем пользователя из ответа с учетом новых полей
                    var userJson = root.GetProperty("user").ToString();
                    currentUser = JsonSerializer.Deserialize<User>(userJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    })!;

                    // Сохраняем логин пользователя
                    SaveLastLogin(currentUser.Login);

                    // Показываем успешное сообщение
                    string successMessage = _isRegistrationMode ?
                        "✓ Регистрация прошла успешно!" :
                        "✓ Успешная авторизация!";

                    Dispatcher.Invoke(() =>
                    {
                        //MessageBox.Show(successMessage, "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        NavigationService.Navigate(new ChatsPage());
                    });
                }
                else if (root.TryGetProperty("message", out var messageElement))
                {
                    string errorMessage = messageElement.GetString();

                    // Проверяем специфичные ошибки и показываем соответствующие сообщения
                    if (errorMessage.Contains("уже в сети"))
                    {
                        MessageBox.Show("Этот пользователь уже авторизован в системе. Попробуйте другой аккаунт или подождите.",
                            "Ошибка авторизации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else if (errorMessage.Contains("Неверный пароль"))
                    {
                        MessageBox.Show("Неверный пароль. Проверьте правильность ввода.",
                            "Ошибка авторизации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else if (errorMessage.Contains("не может быть пустым"))
                    {
                        MessageBox.Show("Пожалуйста, заполните все обязательные поля.",
                            "Ошибка регистрации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else if (errorMessage.Contains("уже существует"))
                    {
                        MessageBox.Show("Пользователь с таким логином уже существует. Выберите другой логин.",
                            "Ошибка регистрации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"✗ {errorMessage}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show($"✗ Неизвестный ответ сервера: {response.Substring(0, Math.Min(response.Length, 100))}...",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (JsonException ex)
            {
                // Если ответ не JSON, может быть это текстовое сообщение от сервера
                if (response.Contains("Успешная авторизация") || response.Contains("Успешная регистрация"))
                {
                    // Пытаемся извлечь данные пользователя из текстового ответа
                    await HandleTextResponse(response);
                }
                else
                {
                    MessageBox.Show($"✗ Ошибка парсинга ответа сервера: {ex.Message}\nОтвет: {response}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"✗ Необработанная ошибка: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task HandleTextResponse(string response)
        {
            // Если сервер вернул текстовое сообщение вместо JSON
            // (например, для обратной совместимости)

            if (response.Contains("Успешная авторизация") || response.Contains("Успешная регистрация"))
            {
                // Создаем минимального пользователя
                currentUser = new User
                {
                    Id = 1, // Временный ID, сервер должен будет обновить
                    Login = LoginText.Text,
                    Password = "", // Не храним пароль
                    Name = NameText.Text,
                    Surname = SurnameText.Text,
                    SecondSurname = SecondSurnameText.Text,
                    CreateDate = DateOnly.FromDateTime(DateTime.Now)
                };

                // Сохраняем логин пользователя
                SaveLastLogin(currentUser.Login);

                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("✓ Авторизация прошла успешно! (текстовый режим)",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    NavigationService.Navigate(new ChatsPage());
                });
            }
        }

        private void SaveLastLogin(string login)
        {
            try
            {
                // Создаем директорию, если она не существует
                string directory = System.IO.Path.GetDirectoryName(_lastLoginFilePath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                // Сохраняем логин в файл
                System.IO.File.WriteAllText(_lastLoginFilePath, login);
            }
            catch (Exception)
            {
                // Игнорируем ошибки сохранения
            }
        }

        private string LoadLastLogin()
        {
            try
            {
                if (System.IO.File.Exists(_lastLoginFilePath))
                {
                    return System.IO.File.ReadAllText(_lastLoginFilePath);
                }
            }
            catch (Exception)
            {
                // Игнорируем ошибки чтения
            }
            return null;
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginText.Text.Trim();
            string password = PasswordText.Password;
            string name = NameText.Text.Trim();
            string surname = SurnameText.Text.Trim();
            string secondSurname = SecondSurnameText.Text.Trim();

            // Проверяем введенные данные
            if (string.IsNullOrEmpty(login))
            {
                MessageBox.Show("Введите логин", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoginText.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Введите пароль", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                PasswordText.Focus();
                return;
            }

            if (_isRegistrationMode && string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Введите имя", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameText.Focus();
                return;
            }

            // Отключаем кнопку во время выполнения запроса
            ActionButton.IsEnabled = false;
            ModeToggleButton.IsEnabled = false;

            // Запускаем асинхронную обработку
            Task.Run(async () =>
            {
                await HandleAuthAsync(login, password, name, surname, secondSurname);

                // Включаем кнопки обратно
                Dispatcher.Invoke(() =>
                {
                    ActionButton.IsEnabled = true;
                    ModeToggleButton.IsEnabled = true;
                });
            });
        }

        private void ModeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Переключаем режим
            _isRegistrationMode = !_isRegistrationMode;
            UpdateUIForMode();

            // Фокусируемся на первом поле
            if (_isRegistrationMode && !string.IsNullOrEmpty(LoginText.Text))
            {
                NameText.Focus();
            }
            else
            {
                LoginText.Focus();
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Загружаем последний сохраненный логин
            string lastLogin = LoadLastLogin();
            if (!string.IsNullOrEmpty(lastLogin))
            {
                LoginText.Text = lastLogin;
                PasswordText.Focus();
            }
            else
            {
                LoginText.Focus();
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Обработка нажатия Enter в текстовых полях
            if (e.Key == Key.Enter)
            {
                if (sender == LoginText && !_isRegistrationMode)
                {
                    PasswordText.Focus();
                }
                else if (sender == PasswordText && !_isRegistrationMode)
                {
                    ActionButton_Click(sender, e);
                }
                else if (sender == LoginText && _isRegistrationMode)
                {
                    NameText.Focus();
                }
                else if (sender == NameText && _isRegistrationMode)
                {
                    SurnameText.Focus();
                }
                else if (sender == SurnameText && _isRegistrationMode)
                {
                    SecondSurnameText.Focus();
                }
                else if (sender == SecondSurnameText && _isRegistrationMode)
                {
                    PasswordText.Focus();
                }
                else if (sender == PasswordText && _isRegistrationMode)
                {
                    ActionButton_Click(sender, e);
                }

                e.Handled = true;
            }
        }

        // Вспомогательные свойства для привязки данных
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(AutorizationPage), new PropertyMetadata("Авторизация"));
    }
}