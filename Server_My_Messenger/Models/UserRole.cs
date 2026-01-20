using System;
using System.Collections.Generic;

namespace Server_My_Messenger.Models;

public partial class UserRole
{
    public int Id { get; set; }

    public string Role { get; set; } = null!;

    public virtual ICollection<UserInChat> UserInChats { get; set; } = new List<UserInChat>();
}
