using System;
using System.Collections.Generic;

namespace Server_My_Messenger.Models;

public partial class Statuses
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
