using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Client_My_Messenger.Dialogs
{
    /// <summary>
    /// Логика взаимодействия для CreateChatDialog.xaml
    /// </summary>
    public partial class CreateChatDialog : Window
    {
        public string ChatName { get; private set; }
        public int ChatTypeId { get; private set; }

        public CreateChatDialog()
        {
            InitializeComponent();
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ChatNameTextBox.Text))
            {
                MessageBox.Show("Введите название чата");
                return;
            }

            ChatName = ChatNameTextBox.Text;
            var selectedItem = ChatTypeComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null && int.TryParse(selectedItem.Tag.ToString(), out int typeId))
            {
                ChatTypeId = typeId;
            }
            else
            {
                ChatTypeId = 1; // Групповой чат по умолчанию
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
