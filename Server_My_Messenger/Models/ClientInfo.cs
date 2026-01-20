using System.Net.WebSockets;
using Server_My_Messenger.Models;

namespace Server_My_Messenger
{
    public class ClientInfo
    {
        public WebSocket Socket { get; set; }
        public string ConnectionId { get; set; }
        public string Nickname { get; set; }
        public string IP { get; set; }
        public int Port { get; set; }
        public DateTime ConnectionTime { get; set; }
        public DateTime LastActivityTime { get; set; }
        public Task ClientTask { get; set; }
        public User User { get; set; }
        public int? CurrentChatId { get; set; }
        public List<int> JoinedChats { get; set; } = new List<int>();
        public Dictionary<int, DateTime> ChatLastSeen { get; set; } = new Dictionary<int, DateTime>();
    }
}