using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wizz.Application.Abstractions;
using Wizz.Application.Security;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Services;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات حزمة الأمان H-04: تجزئة PBKDF2، سياسة كلمة المرور، مصفوفة الأدوار،
/// بصمة الأمان، سجل التدقيق، قفل محاولات الدخول، وترقية قاعدة البيانات.
/// </summary>
public sealed class SecurityHardeningTests
{
    // H-04 (أ): PBKDF2 ينتج تجزئة مختلفة عن كلمة المرور، ويقبل الصحيحة ويرفض الخاطئة.
    [Fact]
    public void PasswordHasher_HashesAndVerifies()
    {
        const string password = "StrongPass123";
        var hash = PasswordHasher.Hash(password);

        Assert.NotEqual(password, hash);
        Assert.Equal(3, hash.Split('.').Length);
        Assert.True(PasswordHasher.Verify(password, hash));
        Assert.False(PasswordHasher.Verify("WrongPass123", hash));
    }

    // H-04 (ب): ملح عشوائي لكل كلمة مرور — تجزئتان لنفس الكلمة تختلفان وكلتاهما تُتحقق.
    [Fact]
    public void PasswordHasher_UsesRandomSaltPerHash()
    {
        const string password = "StrongPass123";
        var first = PasswordHasher.Hash(password);
        var second = PasswordHasher.Hash(password);

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify(password, first));
        Assert.True(PasswordHasher.Verify(password, second));
    }

    // H-04 (ج): تجزئة تالفة أو فارغة لا تُقبل.
    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("210000.invalid.invalid")]
    public void PasswordHasher_RejectsMalformedHash(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("StrongPass123", storedHash));
    }

    // H-04 (د): سياسة كلمة المرور تفرض 10 أحرف + حرف + رقم.
    [Theory]
    [InlineData("", false)]
    [InlineData("Short1", false)]
    [InlineData("allletterslong", false)]
    [InlineData("1234567890123", false)]
    [InlineData("ValidPass123", true)]
    public void PasswordPolicy_EnforcesMinimumRules(string password, bool expectedValid)
    {
        Assert.Equal(expectedValid, PasswordPolicy.IsValid(password));
    }

    // H-04 (هـ): الكاشير لا يملك إدارة المنتجات أو التقارير أو المستخدمين أو النسخ.
    [Fact]
    public void CashierRole_HasOnlyOperationalPermissions()
    {
        Assert.True(UserRole.Cashier.HasPermission(Permission.CreateSale));
        Assert.True(UserRole.Cashier.HasPermission(Permission.ViewProducts));
        Assert.True(UserRole.Cashier.HasPermission(Permission.AccessAI));

        Assert.False(UserRole.Cashier.HasPermission(Permission.ManageProducts));
        Assert.False(UserRole.Cashier.HasPermission(Permission.ViewReports));
        Assert.False(UserRole.Cashier.HasPermission(Permission.ManageUsers));
        Assert.False(UserRole.Cashier.HasPermission(Permission.BackupRestore));
        Assert.False(UserRole.Cashier.HasPermission(Permission.CancelSale));
        Assert.False(UserRole.Cashier.HasPermission(Permission.ModifySale));
    }

    // H-04 (و): المدير يدير المنتجات والتقارير والإعدادات والإلغاء/التعديل،
    // لكنه لا يدير المستخدمين ولا النسخ الاحتياطي ولا مصفوفة الصلاحيات.
    [Fact]
    public void ManagerRole_CannotManageUsersBackupsOrPermissions()
    {
        Assert.True(UserRole.Manager.HasPermission(Permission.ManageProducts));
        Assert.True(UserRole.Manager.HasPermission(Permission.ViewReports));
        Assert.True(UserRole.Manager.HasPermission(Permission.ManageStoreSettings));
        Assert.True(UserRole.Manager.HasPermission(Permission.CancelSale));
        Assert.True(UserRole.Manager.HasPermission(Permission.ModifySale));

        Assert.False(UserRole.Manager.HasPermission(Permission.ManageUsers));
        Assert.False(UserRole.Manager.HasPermission(Permission.BackupRestore));
        Assert.False(UserRole.Manager.HasPermission(Permission.ManagePermissions));
    }

    // H-04 (ز): مدير النظام يملك كل الصلاحيات المعرّفة دون استثناء.
    [Fact]
    public void AdminRole_HasEveryPermission()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            Assert.True(UserRole.Admin.HasPermission(permission), $"Admin should have {permission}");
        }
    }

    // H-04 (ح): بصمة الأمان تتغير عند تغيير كلمة المرور أو الدور أو التعطيل، ولا تتغير عند تعديل الاسم.
    [Fact]
    public void SecurityStamp_RotatesOnSensitiveChangesOnly()
    {
        var user = new User("cashier1", "hash", "كاشير تجريبي", UserRole.Cashier);
        var initial = user.SecurityStamp;
        Assert.False(string.IsNullOrWhiteSpace(initial));

        user.UpdateProfile("كاشير تجريبي 2");
        Assert.Equal(initial, user.SecurityStamp);

        user.UpdateProfile("كاشير تجريبي 2", UserRole.Manager);
        var afterRole = user.SecurityStamp;
        Assert.NotEqual(initial, afterRole);

        user.ChangePassword("new-hash");
        var afterPassword = user.SecurityStamp;
        Assert.NotEqual(afterRole, afterPassword);

        user.Deactivate();
        Assert.NotEqual(afterPassword, user.SecurityStamp);
    }

    // H-04 (ط): قفل المحاولات بعد 5 إخفاقات، ويُرفع بالقفل الزمني أو بإعادة الضبط.
    [Fact]
    public void LoginAttemptTracker_LocksAfterFiveFailures()
    {
        var tracker = new LoginAttemptTracker();
        var now = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < LoginAttemptTracker.MaxFailures - 1; i++)
        {
            tracker.RecordFailure("cashier1", now);
            Assert.False(tracker.IsLocked("cashier1", now));
        }

        tracker.RecordFailure("cashier1", now);
        Assert.True(tracker.IsLocked("cashier1", now));
        Assert.True(tracker.IsLocked("CASHIER1", now.AddSeconds(30)));

        Assert.False(tracker.IsLocked("cashier1", now.Add(LoginAttemptTracker.LockoutDuration).AddSeconds(1)));

        tracker.RecordFailure("cashier1", now);
        tracker.Reset("cashier1");
        Assert.False(tracker.IsLocked("cashier1", now));
    }

    // H-04 (ي): سجل التدقيق يُحفظ فعليًا، ويقصّ التفاصيل الطويلة بدل رفض العملية.
    [Fact]
    public async Task AuditLogger_PersistsEntryAndTruncatesLongDetails()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new WizzDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var logger = new AuditLogger(db);
        var longDetails = new string('س', AuditLog.MaxDetailsLength + 250);

        await logger.LogAsync(new AuditEntry(
            AuditAction.SaleCancelled,
            AuditOutcome.Success,
            Username: "cashier1",
            EntityType: "Sale",
            EntityId: Guid.NewGuid().ToString(),
            Details: longDetails));

        var saved = await db.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AuditAction.SaleCancelled, saved.Action);
        Assert.Equal(AuditOutcome.Success, saved.Outcome);
        Assert.Equal("cashier1", saved.Username);
        Assert.Equal("Sale", saved.EntityType);
        Assert.NotNull(saved.Details);
        Assert.Equal(AuditLog.MaxDetailsLength, saved.Details!.Length);
    }
}

/// <summary>H-04 (ك): ترقية قاعدة بيانات المرحلة 2 إلى مهاجرة سجل التدقيق وبصمة الأمان.</summary>
public sealed class SecurityMigrationUpgradeTests : IAsyncLifetime
{
    private const string PhaseTwoMigration = "20261114123401_AddCashierShiftsAndSaleAudit";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"wizz_security_upgrade_{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task H04_Upgrade_AddsAuditLogsAndUniqueSecurityStamps()
    {
        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 11, 10, 9, 30, 0, DateTimeKind.Utc);

        await using (var db = new WizzDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(PhaseTwoMigration);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" ("Id", "Username", "PasswordHash", "FullName", "Role", "IsActive", "CreatedAt", "LastLoginAt")
                VALUES ({firstUserId}, {"admin"}, {"hash1"}, {"مدير قديم"}, {2}, {1}, {createdAt}, NULL)
                """);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" ("Id", "Username", "PasswordHash", "FullName", "Role", "IsActive", "CreatedAt", "LastLoginAt")
                VALUES ({secondUserId}, {"cashier"}, {"hash2"}, {"كاشير قديم"}, {0}, {1}, {createdAt}, NULL)
                """);
        }

        await using (var upgraded = new WizzDbContext(options))
        {
            await upgraded.Database.MigrateAsync();

            var users = await upgraded.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
            Assert.Equal(2, users.Count);
            Assert.All(users, u => Assert.False(string.IsNullOrWhiteSpace(u.SecurityStamp)));
            Assert.NotEqual(users[0].SecurityStamp, users[1].SecurityStamp);

            Assert.Equal(0, await upgraded.AuditLogs.CountAsync());

            var applied = await upgraded.Database
                .SqlQuery<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\"")
                .ToListAsync();
            Assert.Contains("20261114201404_AddAuditLogsAndUserSecurityStamp", applied);
        }
    }
}
