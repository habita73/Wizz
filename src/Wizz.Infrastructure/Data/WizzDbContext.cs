namespace Wizz.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Wizz.Domain;

public sealed class WizzDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<User> Users => Set<User>();
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
            b.Property(s => s.ModificationNote).HasMaxLength(500).HasColumnName("CorrectionReason");
            b.HasIndex(s => s.SaleDate);
            b.HasIndex(s => s.Status);
            b.HasIndex(s => s.CashierId);
            b.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SaleItem>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.Quantity).HasColumnType("decimal(18,4)");
            b.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
            b.Property(i => i.LineTotal).HasColumnType("decimal(18,2)");
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

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.Username).IsRequired().HasMaxLength(100);
            b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
            b.Property(u => u.FullName).IsRequired().HasMaxLength(200);
            b.HasIndex(u => u.Username).IsUnique();
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