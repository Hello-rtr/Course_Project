using System;
using System.Collections.Generic;

namespace Server_My_Messenger.Models;

public partial class MessageUserInChat
{
    public int IdMessage { get; set; }

    public int IdChat { get; set; }

    public int IdUser { get; set; }

    public virtual Chat IdChatNavigation { get; set; } = null!;

    public virtual Message IdMessageNavigation { get; set; } = null!;

    public virtual User IdUserNavigation { get; set; } = null!;
}
