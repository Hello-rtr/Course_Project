using Microsoft.EntityFrameworkCore;
using Server_My_Messenger.Models;

namespace Server_My_Messenger
{
    public class UserService
    {
        private readonly LocalMessangerDbContext _context;

        public UserService(LocalMessangerDbContext context)
        {
            _context = context;
        }

        public async Task<User> GetUserByLoginAsync(string login)
        {
            return await _context.Users
                .Include(u => u.Status)
                .FirstOrDefaultAsync(u => u.Login == login);
        }

        public async Task<User> CreateUserAsync(User user)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<User> GetUserByIdAsync(int id)
        {
            return await _context.Users
                .Include(u => u.Status)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task UpdateUserAsync(User user)
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users
                .Include(u => u.Status)
                .ToListAsync();
        }
    }
}