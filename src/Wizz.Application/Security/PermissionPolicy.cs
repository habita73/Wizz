namespace Wizz.Application.Security;

using Wizz.Domain;

/// <summary>
/// أسماء سياسات التفويض ومطالبة الصلاحيات. القيم ثوابت نصية لتُستخدم في سمات
/// <c>[Authorize(Policy = ...)]</c> داخل مكوّنات Razor.
/// </summary>
public static class PermissionPolicy
{
    public const string ClaimType = "wizz:permission";

    public const string CreateSale = "Permission:CreateSale";
    public const string ViewProducts = "Permission:ViewProducts";
    public const string ManageProducts = "Permission:ManageProducts";
    public const string ViewReports = "Permission:ViewReports";
    public const string ManageUsers = "Permission:ManageUsers";
    public const string ManageStoreSettings = "Permission:ManageStoreSettings";
    public const string BackupRestore = "Permission:BackupRestore";
    public const string ManagePermissions = "Permission:ManagePermissions";
    public const string AccessAI = "Permission:AccessAI";
    public const string CancelSale = "Permission:CancelSale";
    public const string ModifySale = "Permission:ModifySale";
    public const string OperateCashDrawer = "Permission:OperateCashDrawer";
    public const string ManageCashDrawer = "Permission:ManageCashDrawer";
    public const string ManagePurchases = "Permission:ManagePurchases";
    public const string ViewPurchaseCosts = "Permission:ViewPurchaseCosts";

    public static string For(Permission permission) => "Permission:" + permission;
}
