namespace Wizz.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>تنفيذ مستودع المستخدمين.</summary>
public sealed class UserRepository : IUserRepository
{
    private readonly WizzDbContext _db;

    public UserRepository(WizzDbContext db) => _db = db;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => await _db.Users.FirstOrDefaultAsync(u => u.Username == username.ToLowerInvariant(), ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
        => await _db.Users.OrderBy(u => u.Username).ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await _db.Users.AddAsync(user, ct);

    public void Update(User user) => _db.Users.Update(user);

    public async Task<bool> ExistsUsernameAsync(string username, Guid? excludeId = null, CancellationToken ct = default)
    {
        var normalized = username.ToLowerInvariant();
        return await _db.Users.AnyAsync(u => u.Username == normalized && (!excludeId.HasValue || u.Id != excludeId.Value), ct);
    }
}