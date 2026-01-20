using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client_My_Messenger.Models
{
    public class Message
    {
        public int Id { get; set; }
        public string Data { get; set; } = null!;
        public DateTime DateAndTime { get; set; }
        public bool IsDeleted { get; set; }
        public int? IdMessagesReferred { get; set; }
        public bool IsUpdate { get; set; }
        public bool IsRead { get; set; }
    }
}
