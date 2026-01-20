using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Client_My_Messenger.Dialogs
{
    public partial class CreateChatFromSearchDialog : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _targetUserName;
        private string _targetUserLogin;
        private int _targetUserId;
        private string _chatName;
        private bool _isChatNameValid;

        public string TargetUserName
        {
            get => _targetUserName;
            set
            {
                _targetUserName = value;
                OnPropertyChanged();
            }
        }

        public string TargetUserLogin
        {
            get => _targetUserLogin;
            set
            {
                _targetUserLogin = value;
                OnPropertyChanged();
            }
        }

        public int TargetUserId
        {
            get => _targetUserId;
            set => _targetUserId = value;
        }

        public string ChatName
        {
            get => _chatName;
            set
            {
                _chatName = value;
                OnPropertyChanged();
                IsChatNameValid = !string.IsNullOrWhiteSpace(_chatName) && _chatName.Length >= 3;
            }
        }

        public bool IsChatNameValid
        {
            get => _isChatNameValid;
            set
            {
                _isChatNameValid = value;
                OnPropertyChanged();
            }
        }

        public int SelectedChatTypeId { get; private set; }

        public CreateChatFromSearchDialog(int targetUserId, string targetUserName, string targetUserLogin)
        {
            InitializeComponent();
            DataContext = this;

            TargetUserId = targetUserId;
            TargetUserName = targetUserName;
            TargetUserLogin = targetUserLogin;

            // Устанавливаем начальное название чата
            ChatName = $"Чат с {targetUserName}";
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsChatNameValid)
            {
                MessageBox.Show("Название чата должно содержать не менее 3 символов",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedItem = ChatTypeComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null && int.TryParse(selectedItem.Tag.ToString(), out int typeId))
            {
                SelectedChatTypeId = typeId;
            }
            else
            {
                SelectedChatTypeId = 2; // Приватный по умолчанию
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}