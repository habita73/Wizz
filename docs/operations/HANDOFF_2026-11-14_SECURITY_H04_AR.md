# تسليم OpenCode — حزمة الأمان H-04: المصادقة والصلاحيات والتدقيق (2026-11-14)

- **المرجع التشغيلي:** حزمة التوثيق `v0.6.3`؛ نطاق MVP P0 إلزامي وP1 اختياري (دون تغيير).
- **خط أساس الكود:** حزمة `v0.6.2` عند `79c8416`؛ وديعة H-04 عند `153c7ee`.
- **حالة المهمة:** `DONE` ضمن نطاق D-32 الحرفي. القرار `APPROVED` بتاريخ 2026-11-14.
- **القيود:** المزامنة والسحابة والهاتف وتصميم/تنفيذ المرحلة 3 (الصندوق/المشتريات/الواجهات) **خارج النطاق**
  وما زالت `NOT AUTHORIZED`.

## 1. ما أُنجز

| البند | الحالة |
|---|---|
| مصادقة كوكيز على جدول `User` القائم (دون ASP.NET Core Identity) | `DONE` |
| `PasswordHasher` PBKDF2-SHA256 (210,000 تكرار، ملح 16 بايت، مفتاح 32 بايت) | `DONE` |
| `User.SecurityStamp` + تدوير البصمة + `OnValidatePrincipal` لإبطال الجلسات | `DONE` |
| `AuditLog`/`AuditAction`/`AuditOutcome` + `IAuditLogger`/`AuditLogger`/`SafeAudit` | `DONE` |
| سياسات `Permission:*` + `[Authorize]` على الصفحات + إخفاء `NavMenu` | `DONE` |
| حراسة دوال التعديل في `ProductsList`/`SettingsDashboard`/`SalesHistory` | `DONE` |
| صفحتا `/login` و`/setup` (أول تشغيل) و`AccessDenied` و`AuthLayout` | `DONE` |
| `CurrentUser`/`UserClaimsFactory`/`AuthRoutes`/`RedirectToLogin` | `DONE` |
| قفل المحاولات `LoginAttemptTracker` (5 محاولات / 5 دقائق، نافذة 15 دقيقة) | `DONE` |
| تعطيل أي حساب ما زال يستخدم `Admin@123`/`Cashier@123` + Audit | `DONE` |
| إزالة بيانات الدخول الثابتة من `Program.cs` | `DONE` |
| تشديد مصفوفة الأدوار (`Manager` بلا `ManageUsers`/`BackupRestore`/`ManagePermissions`) | `DONE` |
| صلاحيتا `CancelSale`/`ModifySale` لـ`Manager`/`Admin` | `DONE` |
| المهاجرة `20261114201404_AddAuditLogsAndUserSecurityStamp` | `DONE` |
| `AuditLogs` في الجداول المطلوبة للنسخ الاحتياطي | `DONE` |
| CSS لصفحات المصادقة في `site.css` | `DONE` |

## 2. القرارات المطبقة

- **D-32:** تفويض صريح بتنفيذ حزمة الأمان H-04 بالنطاق الحرفي فقط.
- **قرارات تقنية داخل H-04 (موثقة في D-32):**
  1. مصادقة كوكيز مخصصة على جدول `User` القائم بدل ASP.NET Core Identity.
  2. `SecurityStamp` يتغيّر عند تغيير كلمة المرور أو الدور أو التعطيل، ويُتحقق عند كل طلب.
  3. سياسات الصلاحيات تُبنى من `User.HasPermission` وتُحمَّل كمطالبات عند الدخول.
  4. تعطيل الحسابات الافتراضية تلقائيًا عند بدء التشغيل عند بقائها على `Admin@123`/`Cashier@123`.
  5. قفل المحاولات في الذاكرة — مناسب لنقطة بيع محلية أحادية العملية، ويُصفَّر بإعادة التشغيل.
  6. تسجيل الخروج `POST` مع `.DisableAntiforgery()` لأن العملية لا تغيّر بيانات.
  7. `IAuditLogger` مسجل Scoped، و`SafeAudit` يمنع فشل التدقيق من إفشال العملية الأصلية.
  8. `LoginAttemptTracker` و`PasswordPolicy` و`PermissionPolicy` في طبقة `Wizz.Application/Security`.

## 3. الملفات المتغيرة

- **Domain:** `src/Wizz.Domain/User.cs` (بصمة الأمان وصلاحيتان جديدتان)، `src/Wizz.Domain/AuditLog.cs` (جديد).
- **Application:** `Abstractions/IAuditLogger.cs`، `Security/PasswordPolicy.cs`، `Security/LoginAttemptTracker.cs`،
  `Security/PermissionPolicy.cs` (جديدة).
- **Infrastructure:** `Data/WizzDbContext.cs` (`AuditLogs` + `SecurityStamp`)، `Services/AuditLogger.cs` (جديد)،
  `Services/BackupService.cs`، `Extensions/InfrastructureServiceCollectionExtensions.cs`،
  المهاجرة `20261114201404_AddAuditLogsAndUserSecurityStamp` + snapshot.
- **Presentation:** `Program.cs`، `Components/Routes.razor`، `Components/Layout/MainLayout.razor`،
  `Components/Layout/NavMenu.razor`، `Components/Layout/AuthLayout.razor` (جديد)،
  `Components/Account/RedirectToLogin.razor` (جديد)، `Security/UserClaimsFactory.cs`، `Security/AuthRoutes.cs`،
  `Services/CurrentUser.cs`، `Services/SafeAudit.cs` (جديدة)، `Pages/Auth/Login.razor`،
  `Pages/Auth/Setup.razor`، `Pages/Auth/AccessDenied.razor` (جديدة)، و`Pages/Index.razor`،
  `Pages/Products/ProductsList.razor`، `Pages/Sales/NewSale.razor`، `Pages/Sales/SalesHistory.razor`،
  `Pages/Sales/Debts.razor`، `Pages/Reports/ReportsDashboard.razor`، `Pages/AI/AIChat.razor`،
  `Pages/Settings/SettingsDashboard.razor`، `wwwroot/css/site.css`، `_Imports.razor`.
- **Tests:** `tests/Wizz.Integration.Tests/SecurityHardeningTests.cs` (جديد)،
  `tests/Wizz.Integration.Tests/BackupServiceTests.cs` (بيانات المستخدم صارت تضم `SecurityStamp`).
- **Docs:** `docs/DECISIONS_AND_OPEN_GATES.md` (D-32 + سجل التنفيذ + v0.6.3)، `README.md`، `MANIFEST.json`،
  `PACK_MANIFEST_v0.6.3.json`، وهذا الملف.

## 4. التحقق الفعلي

| الأمر | النتيجة |
|---|---|
| `dotnet build Wizz.sln -c Release` | **0 أخطاء** |
| `dotnet test` — Domain | **24/24** |
| `dotnet test` — Integration | **262/262** (245 سابقة + 17 جديدة) |
| اختبار الدخان HTTP (قاعدة مؤقتة، منفذ 5183) | `/` و`/settings` و`/products` و`/reports` ⇒ **302** إلى `/login` |
| اختبار الدخان HTTP — إعداد أول تشغيل | `/setup` ⇒ **200**، إنشاء مدير ⇒ **302** إلى `/` مع كوكي `wizz.auth` |
| اختبار الدخان HTTP — الدخول والخروج | `/login` ⇒ **200**، الدخول ⇒ **302** إلى `/` و`/settings` ⇒ **200**، `POST /auth/logout` ⇒ **302** ثم `/settings` ⇒ **302** إلى `/login` |
| اختبار الدخان HTTP — إغلاق `/setup` | بعد وجود مدير نشط: `/setup` لم يعد متاحًا لإنشاء مدير ثانٍ |

**حدود التحقق:** الاختبارات على SQLite داخل الذاكرة وعلى ملف مؤقت، واختبار الدخان على Kestrel محلي، وليست اختبارًا
على جهاز تاجر أو قاعدة إنتاجية. لا اختبار واجهة متصفح آلي لصفحات Blazor.

## 5. غير المحسوم / المؤجل

1. **المرحلة 3 (الصندوق/المشتريات/الواجهات):** تصميم مكتوب مطلوب قبل أي كود، ولم يبدأ.
2. **المزامنة والسحابة والهاتف:** خارج النطاق.
3. **قفل المحاولات عبر إعادة التشغيل:** في الذاكرة؛ إن لزم استمرار القفل عبر إعادة التشغيل فسيحتاج تخزينًا.
4. **استعادة نسخ احتياطية قديمة لا تضم جدول `AuditLogs`:** ترفضها `BackupService` بحكم قائمة الجداول المطلوبة.
5. **سياسة كلمات المرور:** 10 أحرف + حرف + رقم؛ لا تحقق من قوائم كلمات مرور مسربة (خارج النطاق).
6. **MVP-AT-09:** ما زال يحتاج اختبار استعادة فعلي في بيئة منفصلة.

## 6. الخطوة التالية الواحدة

كتابة **تصميم المرحلة 3** (الصندوق/المشتريات/الواجهات) وعرض قراراتها المحدودة على Farid قبل أي كود. لا يُبدأ
أي بند تنفيذي قبل تفويض صريح يذكره حرفيًا.

---

**تسليم قابل للنقل.** لا يعتمد على ذاكرة محادثة سابقة؛ الحالة الكاملة في الملفات المذكورة أعلاه.
