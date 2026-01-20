using Client_My_Messenger.Models;
using Client_My_Messenger.Pages;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Client_My_Messenger
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {
        private ClientWebSocket _ws = new ClientWebSocket();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Показываем экран поиска
            ShowSearchScreen();

            // Запускаем поиск сервера
            await FindAndConnectToServerAsync();
        }

        private void ShowSearchScreen()
        {
            var stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Анимация загрузки
            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = 50,
                Height = 50,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 3,
                Margin = new Thickness(0, 0, 0, 20)
            };

            // Анимация вращения
            var rotateAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(2),
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };

            var rotateTransform = new RotateTransform();
            ellipse.RenderTransform = rotateTransform;
            rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);

            // Текст
            var textBlock = new TextBlock
            {
                Text = "Поиск сервера...",
                FontSize = 16,
                Foreground = Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Информация
            var infoText = new TextBlock
            {
                Text = "Убедитесь, что сервер запущен и оба устройства в одной сети",
                FontSize = 12,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 10, 20, 0)
            };

            stackPanel.Children.Add(ellipse);
            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(infoText);

            MainFrame.Content = stackPanel;
        }

        private async Task FindAndConnectToServerAsync()
        {
            DiscoveredServer server = null;

            try
            {
                Debug.WriteLine("[Client] Начинаю поиск сервера...");

                // Ждем сервер бесконечно
                server = await ServerDiscovery.WaitForServerAsync();

                if (server != null)
                {
                    Debug.WriteLine($"[Client] Сервер найден: {server.ServerName}");

                    // Пробуем подключиться
                    if (await ConnectToServerAsync(server))
                    {
                        // Успешно подключились, переходим к авторизации
                        Dispatcher.Invoke(() =>
                        {
                            MainFrame.Navigate(new AutorizationPage(_ws, _cts));
                            Title = $"My Messenger - {server.ServerName}";
                        });
                    }
                    else
                    {
                        // Ошибка подключения, пробуем снова
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show("Не удалось подключиться к серверу. Пробую снова...",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });

                        // Повторяем поиск
                        await FindAndConnectToServerAsync();
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[Client] Поиск отменен");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Client] Ошибка: {ex.Message}");

                // Показываем сообщение и пробуем снова через 5 секунд
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Ошибка: {ex.Message}\n\nПовторная попытка через 5 секунд...",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                });

                await Task.Delay(5000);
                await FindAndConnectToServerAsync();
            }
        }

        private async Task<bool> ConnectToServerAsync(DiscoveredServer server)
        {
            try
            {
                var uri = new Uri(server.WsEndpoint);

                Debug.WriteLine($"[Client] Подключаюсь к {uri}");

                await _ws.ConnectAsync(uri, _cts.Token);

                Dispatcher.Invoke(() =>
                {
                    //MessageBox.Show($"✓ Успешно подключено!\nСервер: {server.ServerName}", "Подключение", MessageBoxButton.OK, MessageBoxImage.Information);
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Client] Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        // Кнопка для ручного переподключения (опционально)
        private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
        {
            // Закрываем текущее соединение
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Reconnecting", CancellationToken.None);
                }
            }
            catch { }

            // Создаем новое соединение
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            // Запускаем поиск заново
            await FindAndConnectToServerAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Останавливаем поиск
            ServerDiscovery.StopDiscovery();

            // Закрываем соединение
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Closing", CancellationToken.None).Wait(1000);
                }
                _ws.Dispose();
                _cts.Cancel();
            }
            catch { }
        }
    }
}
