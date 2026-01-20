using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client_My_Messenger.Models
{
    public class GlobalSearchResult
    {
        public string Type { get; set; } = string.Empty; // "chat" или "user"
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsJoined { get; set; }
        public double Similarity { get; set; }
        public int MemberCount { get; set; }
        public string ChatType { get; set; } = string.Empty;
        public User? User { get; set; } // для результатов пользователей
        public Chat? Chat { get; set; } // для результатов чатов
    }
}
