using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client_My_Messenger.Models
{
    public class NotificationState
    {
        private readonly Dictionary<int, int> _unreadCounts = new();
        private readonly object _lock = new();

        public event EventHandler<(int ChatId, int Count)>? UnreadCountChanged;

        public void Reset()
        {
            lock (_lock)
            {
                _unreadCounts.Clear();
            }
        }

        public void IncrementUnread(int chatId)
        {
            lock (_lock)
            {
                if (!_unreadCounts.ContainsKey(chatId))
                {
                    _unreadCounts[chatId] = 0;
                }
                _unreadCounts[chatId]++;

                UnreadCountChanged?.Invoke(this, (chatId, _unreadCounts[chatId]));
            }
        }

        public void ResetUnread(int chatId)
        {
            lock (_lock)
            {
                if (_unreadCounts.ContainsKey(chatId))
                {
                    _unreadCounts[chatId] = 0;
                    UnreadCountChanged?.Invoke(this, (chatId, 0));
                }
            }
        }

        public int GetUnreadCount(int chatId)
        {
            lock (_lock)
            {
                return _unreadCounts.TryGetValue(chatId, out var count) ? count : 0;
            }
        }

        public void UpdateFromServer(int chatId, int count)
        {
            lock (_lock)
            {
                _unreadCounts[chatId] = count;
                UnreadCountChanged?.Invoke(this, (chatId, count));
            }
        }
    }
}