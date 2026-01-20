using Microsoft.EntityFrameworkCore;
using Server_My_Messenger.Models;
using System;

namespace Server_My_Messenger
{
    public class MessageService
    {
        private readonly LocalMessangerDbContext _context;

        public MessageService(LocalMessangerDbContext context)
        {
            _context = context;
        }

        public async Task<int> GetUnreadCountAsync(int userId, int chatId)
        {
            try
            {
                return await _context.MessageUserInChats
                    .Where(muc => muc.IdChat == chatId && muc.IdUser != userId)
                    .Include(muc => muc.IdMessageNavigation)
                    .CountAsync(muc => !muc.IdMessageNavigation.IsRead);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения количества непрочитанных сообщений: {ex.Message}");
                return 0;
            }
        }

        public async Task<List<MessageUserInChat>> GetUnreadMessagesAsync(int userId, int chatId, int limit = 20)
        {
            try
            {
                return await _context.MessageUserInChats
                    .Where(muc => muc.IdChat == chatId && muc.IdUser != userId)
                    .Include(muc => muc.IdMessageNavigation)
                    .Include(muc => muc.IdUserNavigation)
                    .Where(muc => !muc.IdMessageNavigation.IsRead)
                    .OrderByDescending(muc => muc.IdMessageNavigation.DateAndTime)
                    .Take(limit)
                    .OrderBy(muc => muc.IdMessageNavigation.DateAndTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения непрочитанных сообщений: {ex.Message}");
                return new List<MessageUserInChat>();
            }
        }

        public async Task<MessageUserInChat> SendMessageAsync(int chatId, int userId, string messageText)
        {
            var message = new Message
            {
                Data = messageText,
                DateAndTime = DateTime.Now,
                IsDeleted = false,
                IsUpdate = false,
                IsRead = false
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            var messageUserInChat = new MessageUserInChat
            {
                IdMessage = message.Id,
                IdChat = chatId,
                IdUser = userId
            };

            _context.MessageUserInChats.Add(messageUserInChat);
            await _context.SaveChangesAsync();

            return await _context.MessageUserInChats
                .Include(muc => muc.IdMessageNavigation)
                .Include(muc => muc.IdUserNavigation)
                .FirstOrDefaultAsync(muc => muc.IdMessage == message.Id && muc.IdChat == chatId && muc.IdUser == userId);
        }

        public async Task<List<MessageUserInChat>> GetChatHistoryAsync(int chatId, int limit = 100)
        {
            return await _context.MessageUserInChats
                .Include(muc => muc.IdMessageNavigation)
                .Include(muc => muc.IdUserNavigation)
                .Include(muc => muc.IdChatNavigation)
                    .ThenInclude(c => c.ChatType)
                .Where(muc => muc.IdChat == chatId)
                .OrderByDescending(muc => muc.IdMessageNavigation.DateAndTime)
                .Take(limit)
                .OrderBy(muc => muc.IdMessageNavigation.DateAndTime)
                .ToListAsync();
        }

        public async Task<MessageUserInChat?> MarkMessageAsReadAsync(int messageId, int readerUserId)
        {
            var message = await _context.Messages.FindAsync(messageId);
            if (message == null)
                return null;

            // Проверяем, не прочитано ли уже сообщение
            if (message.IsRead)
                return await GetMessageUserInChatWithDetailsAsync(messageId);

            message.IsRead = false;
            await _context.SaveChangesAsync();

            var messageUserInChat = await GetMessageUserInChatWithDetailsAsync(messageId);

            // Обновляем информацию о прочтении в MessageUserInChat (если нужно)
            // Эта часть зависит от структуры вашей БД
            // Если есть таблица для отслеживания, кто прочитал сообщение, добавьте здесь

            return messageUserInChat;
        }

        // Обновленный метод для отметки всех сообщений как прочитанных
        public async Task<List<MessageUserInChat>> MarkAllMessagesAsReadAsync(int chatId, int readerUserId)
        {
            var messages = await _context.MessageUserInChats
                .Where(muc => muc.IdChat == chatId && muc.IdUser != readerUserId)
                .Include(muc => muc.IdMessageNavigation)
                .Where(muc => !muc.IdMessageNavigation.IsRead)
                .Include(muc => muc.IdUserNavigation)
                .Include(muc => muc.IdChatNavigation)
                .ToListAsync();

            foreach (var messageUserInChat in messages)
            {
                if (messageUserInChat.IdMessageNavigation != null)
                {
                    messageUserInChat.IdMessageNavigation.IsRead = true;
                }
            }

            await _context.SaveChangesAsync();
            return messages;
        }

        // Метод для получения деталей сообщения
        private async Task<MessageUserInChat?> GetMessageUserInChatWithDetailsAsync(int messageId)
        {
            return await _context.MessageUserInChats
                .Include(muc => muc.IdMessageNavigation)
                .Include(muc => muc.IdUserNavigation)
                    .ThenInclude(u => u.Status)
                .Include(muc => muc.IdChatNavigation)
                .FirstOrDefaultAsync(muc => muc.IdMessage == messageId);
        }

        // Метод для получения нескольких сообщений с деталями
        public async Task<List<MessageUserInChat>> GetMessagesWithDetailsAsync(List<int> messageIds)
        {
            return await _context.MessageUserInChats
                   .Include(m => m.IdMessageNavigation)
                   .Include(m => m.IdUserNavigation)
                   .Include(m => m.IdChatNavigation)
                   .Where(m => messageIds.Contains(m.IdMessage))
                   .ToListAsync();
        }
    }
}