using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client_My_Messenger.Models
{
    public class ChatObj
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int ChatTypeId { get; set; }
        public DateOnly CreateDate { get; set; }
        public bool? IsJoined { get; set; }
        public int? UnreadCount { get; set; }
    }
}
