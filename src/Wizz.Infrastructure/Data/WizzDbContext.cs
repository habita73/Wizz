namespace Wizz.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Wizz.Domain;

public sealed class WizzDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();
    public DbSet<StockDocumentLine> StockDocumentLines => Set<StockDocumentLine>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DeviceRegistration> DeviceRegistrations => Set<DeviceRegistration>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public WizzDbContext(DbContextOptions<WizzDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired().HasMaxLength(200);
            b.Property(p => p.Barcode).HasMaxLength(100);
            b.Property(p => p.Description).HasMaxLength(1000);
            b.Property(p => p.RetailPrice).HasColumnType("decimal(18,2)");
            b.Property(p => p.WholesalePrice).HasColumnType("decimal(18,2)");
            b.Property(p => p.StockQuantity).HasColumnType("decimal(18,4)");
            b.Property(p => p.ReorderLevel).HasColumnType("decimal(18,4)");
            b.HasIndex(p => p.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            b.HasIndex(p => p.Name);
            b.HasMany(p => p.Units).WithOne().HasForeignKey(u => u.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UnitOfMeasure>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.Name).IsRequired().HasMaxLength(50);
            b.Property(u => u.Symbol).IsRequired().HasMaxLength(20);
            b.Property(u => u.BaseQuantity).HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<Sale>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Subtotal).HasColumnType("decimal(18,2)");
            b.Property(s => s.Discount).HasColumnType("decimal(18,2)");
            b.Property(s => s.Total).HasColumnType("decimal(18,2)");
            b.Property(s => s.Paid).HasColumnType("decimal(18,2)");
            b.Property(s => s.Remaining).HasColumnType("decimal(18,2)");
            b.Property(s => s.InventoryApplied).HasDefaultValue(true);
            b.Property(s => s.CancelReason).HasMaxLength(500);
            b.Property(s => s.ModificationReason).HasMaxLength(500);
            b.Property(s => s.CustomerName).HasMaxLength(200);
            b.HasIndex(s => s.SaleDate);
            b.HasIndex(s => s.Status);
            b.HasIndex(s => s.CashierId);
            b.HasIndex(s => s.ShiftId);
            b.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<CashierShift>()
                .WithMany()
                .HasForeignKey(s => s.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleItem>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.Quantity).HasColumnType("decimal(18,4)");
            b.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
            b.Property(i => i.LineTotal).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<DebtPayment>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            b.Property(p => p.Method).HasConversion<int>();
            b.Property(p => p.Note).HasMaxLength(200);
            b.HasIndex(p => p.SaleId);
            b.HasIndex(p => p.PaidAt);
            b.HasOne<Sale>()
                .WithMany()
                .HasForeignKey(p => p.SaleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Store>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).IsRequired().HasMaxLength(200);
            b.Property(s => s.Address).HasMaxLength(500);
            b.Property(s => s.Phone).HasMaxLength(50);
            b.Property(s => s.TaxNumber).HasMaxLength(50);
            b.Property(s => s.CommercialRegister).HasMaxLength(50);
            b.Property(s => s.Currency).HasMaxLength(3).HasDefaultValue("DZD");
            b.Property(s => s.TimeZone).HasMaxLength(50).HasDefaultValue("Africa/Algiers");
            b.Property(s => s.InventoryEnabled).HasDefaultValue(true);
            b.Property(s => s.DocumentYearDigits).HasDefaultValue(4);
            b.Property(s => s.CashDrawerEnabled).HasDefaultValue(true);
            b.Property(s => s.CashDifferenceTolerance).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        });

        modelBuilder.Entity<StockMovement>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.QuantityDelta).HasColumnType("decimal(18,4)");
            b.Property(m => m.QuantityBefore).HasColumnType("decimal(18,4)");
            b.Property(m => m.QuantityAfter).HasColumnType("decimal(18,4)");
            b.Property(m => m.Reason).HasMaxLength(500);
            b.HasIndex(m => m.ProductId);
            b.HasIndex(m => m.SaleId);
            b.HasIndex(m => m.CreatedAt);
            b.HasOne(m => m.Product)
                .WithMany()
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockDocument>(b =>
        {
            b.HasKey(d => d.Id);
            b.Property(d => d.Number).IsRequired().HasMaxLength(30);
            b.Property(d => d.Reason).HasMaxLength(500);
            b.Property(d => d.Source).HasMaxLength(30);
            b.Property(d => d.VoidReason).HasMaxLength(500);
            b.Property(d => d.SupplierName).HasMaxLength(200);
            b.Property(d => d.SupplierInvoiceNumber).HasMaxLength(100);
            b.HasIndex(d => new { d.StoreId, d.Number }).IsUnique();
            b.HasIndex(d => new { d.StoreId, d.DocumentDate });
            b.HasIndex(d => d.ReferenceSaleId);
            b.HasIndex(d => d.LegacyStockMovementId)
                .IsUnique()
                .HasFilter("[LegacyStockMovementId] IS NOT NULL");
            b.HasMany(d => d.Lines)
                .WithOne(l => l.StockDocument)
                .HasForeignKey(l => l.StockDocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(d => d.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockDocumentLine>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.Quantity).HasColumnType("decimal(18,4)");
            b.Property(l => l.BaseQuantityDelta).HasColumnType("decimal(18,4)");
            b.Property(l => l.QuantityBefore).HasColumnType("decimal(18,4)");
            b.Property(l => l.QuantityAfter).HasColumnType("decimal(18,4)");
            b.Property(l => l.Reason).HasMaxLength(500);
            b.Property(l => l.UnitCost).HasColumnType("decimal(18,2)");
            b.Property(l => l.LineCost).HasColumnType("decimal(18,2)");
            b.HasIndex(l => l.ProductId);
            b.HasOne<Product>()
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UnitOfMeasure>()
                .WithMany()
                .HasForeignKey(l => l.UnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashierShift>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.SalesTotal).HasColumnType("decimal(18,2)");
            b.Property(s => s.CashTotal).HasColumnType("decimal(18,2)");
            b.Property(s => s.CreditTotal).HasColumnType("decimal(18,2)");
            b.Property(s => s.OpeningFloat).HasColumnType("decimal(18,2)");
            b.Property(s => s.DepositsTotal).HasColumnType("decimal(18,2)");
            b.Property(s => s.WithdrawalsTotal).HasColumnType("decimal(18,2)");
            b.Property(s => s.ExpectedCash).HasColumnType("decimal(18,2)");
            b.Property(s => s.CountedCash).HasColumnType("decimal(18,2)");
            b.Property(s => s.CashDifference).HasColumnType("decimal(18,2)");
            b.Property(s => s.CloseNote).HasMaxLength(500);
            b.HasIndex(s => s.OpenedAt);
            b.HasIndex(s => new { s.StoreId, s.CashierId })
                .IsUnique()
                .HasFilter("[ClosedAt] IS NULL");
            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(s => s.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashMovement>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.Type).HasConversion<int>();
            b.Property(m => m.Amount).HasColumnType("decimal(18,2)");
            b.Property(m => m.Reason).IsRequired().HasMaxLength(500);
            b.HasIndex(m => m.ShiftId);
            b.HasIndex(m => m.OccurredAt);
            b.HasIndex(m => m.ReferencePurchaseId);
            b.HasOne<CashierShift>()
                .WithMany()
                .HasForeignKey(m => m.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(m => m.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentSequence>(b =>
        {
            b.HasKey(s => new { s.StoreId, s.DocumentType, s.Year });
            b.Property(s => s.DocumentType).HasConversion<int>();
            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(s => s.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.Username).IsRequired().HasMaxLength(100);
            b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
            b.Property(u => u.FullName).IsRequired().HasMaxLength(200);
            b.Property(u => u.SecurityStamp).IsRequired().HasMaxLength(64);
            b.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Username).HasMaxLength(100);
            b.Property(a => a.Action).HasConversion<int>();
            b.Property(a => a.Outcome).HasConversion<int>();
            b.Property(a => a.EntityType).HasMaxLength(100);
            b.Property(a => a.EntityId).HasMaxLength(100);
            b.Property(a => a.Details).HasMaxLength(AuditLog.MaxDetailsLength);
            b.Property(a => a.IpAddress).HasMaxLength(64);
            b.HasIndex(a => a.OccurredAtUtc);
            b.HasIndex(a => a.UserId);
            b.HasIndex(a => new { a.Action, a.Outcome });
        });

        modelBuilder.Entity<DeviceRegistration>(b =>
        {
            b.HasKey(d => d.Id);
            b.Property(d => d.DeviceName).IsRequired().HasMaxLength(200);
            b.Property(d => d.DeviceType).HasMaxLength(50);
            b.Property(d => d.PublicKey).HasMaxLength(1000);
            b.HasIndex(d => d.DeviceId).IsUnique();
        });

        modelBuilder.Entity<Subscription>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.PlanName).HasMaxLength(100);
            b.Property(s => s.Provider).HasMaxLength(100);
            b.Property(s => s.ExternalId).HasMaxLength(200);
        });
    }
}