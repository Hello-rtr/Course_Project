using Microsoft.EntityFrameworkCore;
using Server_My_Messenger.Models;

namespace Server_My_Messenger
{
    public class ChatService
    {
        private readonly LocalMessangerDbContext _context;

        public ChatService(LocalMessangerDbContext context)
        {
            _context = context;
        }

        public async Task<Chat> CreateChatAsync(string name, int chatTypeId, int creatorId)
        {
            try
            {
                // Проверяем уникальность названия чата
                var existingChat = await _context.Chats
                    .FirstOrDefaultAsync(c => c.Name == name && c.ChatTypeId == chatTypeId);

                if (existingChat != null)
                {
                    throw new InvalidOperationException($"Чат с названием '{name}' уже существует");
                }

                var chat = new Chat
                {
                    Name = name.Trim(),
                    ChatTypeId = chatTypeId,
                    CreateDate = DateOnly.FromDateTime(DateTime.Now)
                };

                _context.Chats.Add(chat);
                await _context.SaveChangesAsync(); // Сохраняем, чтобы получить ID

                // Добавляем создателя в чат
                var userInChat = new UserInChat
                {
                    UserId = creatorId,
                    ChatId = chat.Id,
                    RoleId = 1, // Администратор
                    DateOfEntry = DateTime.Now
                };

                _context.UserInChats.Add(userInChat);
                await _context.SaveChangesAsync();

                return chat;
            }
            catch (Exception ex)
            {
                // Логируем ошибку для отладки
                Console.WriteLine($"[ChatService] Ошибка создания чата: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[ChatService] Внутренняя ошибка: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        public async Task<Chat> CreatePrivateChatAsync(int user1Id, int user2Id)
        {
            var user1 = await _context.Users.FindAsync(user1Id);
            var user2 = await _context.Users.FindAsync(user2Id);

            if (user1 == null || user2 == null)
                return null;

            var chat = new Chat
            {
                Name = $"Приватный: {user1.Name} и {user2.Name}",
                ChatTypeId = 2, // ID для приватных чатов
                CreateDate = DateOnly.FromDateTime(DateTime.Now)
            };

            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            var memberRole = await GetOrCreateRoleAsync("Member");

            var userInChat1 = new UserInChat
            {
                UserId = user1Id,
                ChatId = chat.Id,
                RoleId = memberRole.Id,
                DateOfEntry = DateTime.Now
            };

            var userInChat2 = new UserInChat
            {
                UserId = user2Id,
                ChatId = chat.Id,
                RoleId = memberRole.Id,
                DateOfEntry = DateTime.Now
            };

            _context.UserInChats.AddRange(userInChat1, userInChat2);
            await _context.SaveChangesAsync();

            return chat;
        }

        public async Task<UserRole> GetOrCreateRoleAsync(string roleName)
        {
            var role = await _context.UserRoles.FirstOrDefaultAsync(r => r.Role == roleName);
            if (role == null)
            {
                role = new UserRole { Role = roleName };
                _context.UserRoles.Add(role);
                await _context.SaveChangesAsync();
            }
            return role;
        }

        public async Task<UserInChat> AddUserToChatAsync(int userId, int chatId, string roleName = "Member")
        {
            // Проверяем, есть ли уже запись о пользователе в этом чате
            var existingUserInChat = await _context.UserInChats
                .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChatId == chatId);

            if (existingUserInChat != null)
            {
                // Если пользователь был в чате ранее, обновляем запись
                existingUserInChat.DateRelease = null;
                existingUserInChat.DateOfEntry = DateTime.Now;

                var existingRole = await GetOrCreateRoleAsync(roleName);
                existingUserInChat.RoleId = existingRole.Id;

                await _context.SaveChangesAsync();
                return existingUserInChat;
            }

            // Если записи нет, создаем новую
            var newRole = await GetOrCreateRoleAsync(roleName);

            var userInChat = new UserInChat
            {
                UserId = userId,
                ChatId = chatId,
                RoleId = newRole.Id,
                DateOfEntry = DateTime.Now
            };

            _context.UserInChats.Add(userInChat);
            await _context.SaveChangesAsync();

            return userInChat;
        }

        public async Task<bool> RemoveUserFromChatAsync(int userId, int chatId)
        {
            var userInChat = await _context.UserInChats
                .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChatId == chatId && uc.DateRelease == null);

            if (userInChat != null)
            {
                userInChat.DateRelease = DateTime.Now;
                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        }

        public async Task<List<Chat>> GetUserChatsAsync(int userId)
        {
            return await _context.UserInChats
                .Where(uc => uc.UserId == userId && uc.DateRelease == null)
                .Include(uc => uc.Chat)
                    .ThenInclude(c => c.ChatType)
                .Select(uc => uc.Chat)
                .ToListAsync();
        }

        public async Task<List<UserInChat>> GetChatUsersAsync(int chatId)
        {
            return await _context.UserInChats
                .Where(uc => uc.ChatId == chatId && uc.DateRelease == null)
                .Include(uc => uc.User)
                    .ThenInclude(u => u.Status)
                .Include(uc => uc.Role)
                .ToListAsync();
        }

        public async Task<bool> UpdateUserRoleAsync(int userId, int chatId, string newRoleName)
        {
            var userInChat = await _context.UserInChats
                .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChatId == chatId && uc.DateRelease == null);

            if (userInChat == null)
                return false;

            var newRole = await GetOrCreateRoleAsync(newRoleName);
            userInChat.RoleId = newRole.Id;
            await _context.SaveChangesAsync();

            return true;
        }
    }
}