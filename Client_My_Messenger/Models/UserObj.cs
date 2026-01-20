using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client_My_Messenger.Models
{
    public class UserObj
    {
        public int Id { get; set; }
        public string Login { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Surname { get; set; } = null!;
        public string SecondSurname { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public DateOnly CreateDate { get; set; }
        public DateTime? DateOfLastActivity { get; set; }
        public string? UrlAvatar { get; set; }
        public int? StatusId { get; set; }
        public string? Status { get; set; }
        public bool? IsOnline { get; set; }
        public DateTime? LastSeen { get; set; }
    }
}
