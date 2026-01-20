using System;
using System.Collections.Generic;

namespace Server_My_Messenger.Models;

public partial class User
{
    public int Id { get; set; }

    public string Login { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string SecondSurname { get; set; } = null!;

    public string Surname { get; set; } = null!;

    public DateOnly CreateDate { get; set; }

    public DateTime DateOfLastActivity { get; set; }

    public int StatusId { get; set; }

    public string? UrlAvatar { get; set; }

    public virtual ICollection<MessageUserInChat> MessageUserInChats { get; set; } = new List<MessageUserInChat>();

    public virtual Statuses Status { get; set; } = null!;

    public virtual ICollection<UserInChat> UserInChats { get; set; } = new List<UserInChat>();
}
