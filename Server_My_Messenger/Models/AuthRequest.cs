using System.Text.Json.Serialization;

namespace Server_My_Messenger.Models
{
    public class AuthRequest
    {
        public string Login { get; set; }
        public string Password { get; set; }
        public string Name { get; set; }
        public string Surname { get; set; }
        public string SecondSurname { get; set; }
    }
}