using System;
using System.Collections.Generic;

namespace Server_My_Messenger.Models;

public partial class UserInChat
{
    public int UserId { get; set; }

    public int ChatId { get; set; }

    public int RoleId { get; set; }

    public DateTime DateOfEntry { get; set; }

    public DateTime? DateRelease { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual UserRole Role { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
