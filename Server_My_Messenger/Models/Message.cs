using System;
using System.Collections.Generic;

namespace Server_My_Messenger.Models;

public partial class Message
{
    public int Id { get; set; }

    public string Data { get; set; } = null!;

    public DateTime DateAndTime { get; set; }

    public bool IsDeleted { get; set; }

    public int? IdMessagesReferred { get; set; }

    public bool IsUpdate { get; set; }

    public bool IsRead { get; set; }

    public virtual ICollection<MessageUserInChat> MessageUserInChats { get; set; } = new List<MessageUserInChat>();
}
