using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client_My_Messenger.Models
{
    public class MessageUserInChat
    {
        public int IdMessage { get; set; }
        public int IdChat { get; set; }
        public int IdUser { get; set; }

        // Основные свойства для прямой десериализации (как в консольном клиенте)
        public MessageObj Message { get; set; }
        public UserObj User { get; set; }
        public ChatObj Chat { get; set; }

        // Свойства-алиасы для обратной совместимости с XAML
        [System.Text.Json.Serialization.JsonIgnore]
        public MessageObj IdMessageNavigation
        {
            get => Message;
            set => Message = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public UserObj IdUserNavigation
        {
            get => User;
            set => User = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public ChatObj IdChatNavigation
        {
            get => Chat;
            set => Chat = value;
        }
    }
}