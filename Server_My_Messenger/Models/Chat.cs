using System;
using System.Collections.Generic;

namespace Server_My_Messenger.Models;

public partial class Chat
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int ChatTypeId { get; set; }

    public DateOnly CreateDate { get; set; }

    public virtual ChatType ChatType { get; set; } = null!;

    public virtual ICollection<MessageUserInChat> MessageUserInChats { get; set; } = new List<MessageUserInChat>();

    public virtual ICollection<UserInChat> UserInChats { get; set; } = new List<UserInChat>();
}
