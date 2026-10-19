namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>عقد مستودع المستخدمين.</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
    Task<bool> ExistsUsernameAsync(string username, Guid? excludeId = null, CancellationToken ct = default);
}