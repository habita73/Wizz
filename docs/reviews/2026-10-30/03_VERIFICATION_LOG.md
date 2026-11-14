# سجل التحقق — 2026-10-30

- **الحالة:** `PROPOSED`
- **الغرض:** توثيق ما نُفّذ فعليًا من أوامر وتحقق، وما لم يُنفّذ، مع الأدلة القابلة لإعادة الإنتاج.
- **البيئة:** Windows، المستودع `X:\source\Habita73\Vent-au-comptoir`، شجرة Git نظيفة.
- **المرجع:** commit `26407c6` بتاريخ 2026-10-28.

---

## 1. البيئة

| العنصر | القيمة |
|---|---|
| نظام التشغيل | Windows |
| .NET SDK | `10.0.403` |
| `global.json` | يثبت `10.0.401` مع `rollForward: latestPatch` |
| نوع البناء | `Release` |
| الحل | `Wizz.sln` — 7 مشاريع |
| قاعدة البيانات | SQLite محلية (`wizz.db`) |
| حالة Git قبل التحقق | نظيفة |

**مشاريع الحل:**
- `src/Wizz.Domain`
- `src/Wizz.Application`
- `src/Wizz.Infrastructure`
- `src/Wizz.Presentation`
- `src/Wizz.Presentation.Client`
- `tests/Wizz.Domain.Tests`
- `tests/Wizz.Integration.Tests`

---

## 2. البناء

### الأمر

```powershell
dotnet build Wizz.sln -c Release --nologo
```

### النتيجة

```
Build succeeded.
    3772 Warning(s)
    0 Error(s)

Time Elapsed 00:00:14.49
```

### الملاحظات

- البناء ينجح ولا يمنع التشغيل.
- العدد الكبير من التحذيرات أغلبها StyleCop (`SA1101`, `SA1615`, وغيرها).
- `Directory.Build.props` يضبط `TreatWarningsAsErrors=false`، لذلك لا تفشل البناء.
- وظيفة `code-analysis` في CI تستخدم `TreatWarningsAsErrors=true` وستفشل حتمًا.

---

## 3. الاختبارات

### الأمر

```powershell
dotnet test Wizz.sln -c Release --no-build --nologo
```

### النتيجة الإجمالية

| المشروع | ناجح | فاشل | متجاوز | الإجمالي |
|---|---:|---:|---:|---:|
| `Wizz.Domain.Tests` | 24 | 0 | 0 | 24 |
| `Wizz.Integration.Tests` | 212 | 2 | 0 | 214 |
| **الإجمالي** | **236** | **2** | **0** | **238** |

### الاختباران الفاشلان

1. `Wizz.Integration.Tests.StockPolicyTests.ModifySale_UpdatesStockByDelta`
2. `Wizz.Integration.Tests.StockPolicyTests.ModifySale_ReducingQuantity_ReturnsStock`

### الاستثناء

```
Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
The database operation was expected to affect 1 row(s), but actually affected 0 row(s);
data may have been modified or deleted since entities were loaded.
```

**الموقع:** `Wizz.Application/UseCases/Sales/ModifySaleUseCase.cs:165` داخل `_unitOfWork.SaveChangesAsync(ct)`.

### سياق الاختبار

- `StockPolicyTests` يستخدم `IAsyncLifetime` وقاعدة SQLite في الذاكرة **لكل اختبار**.
- هذا يعني أن الفشل ليس ناتجًا عن تلوث حالة مشتركة بين الاختبارات.
- الفشل يتكرر في الاختبارين المرتبطين بتعديل البيع، ما يشير إلى خلل في مسار المعاملة/التتبع نفسه.

### التفسير الفني

`ModifySaleUseCase` يقوم بما يلي:

1. يحمّل البيع وأصنافه عبر EF Core (تكون متتبعة).
2. `Sale.Edit()` يمسح القائمة القديمة ويضيف أصنافًا جديدة.
3. يفتح معاملة SQL خامًا، يحذف `SaleItems` القديمة، يدخل الجديدة، ويحدّث `Sales`.
4. **يثبّت معاملة SQL** (`transaction.Commit()`).
5. يستدعي `SaveChangesAsync` من EF Core.
6. EF يحاول حذف الأصناف القديمة مرة أخرى → 0 صفوف → `DbUpdateConcurrencyException`.

**الأثر المحتمل:** أصناف البيع وإجمالياته قد تكون محفوظة بالفعل (من خطوة 4)، بينما تحديثات المخزون المرتبطة (المتتبعة في EF) تُلغى بسبب فشل الخطوة 5. هذا مسار محتمل لتناقض البيانات، وليس مجرد فشل اختبار.

### إعادة إنتاج مركزة

```powershell
dotnet test tests/Wizz.Integration.Tests/Wizz.Integration.Tests.csproj `
  -c Release --no-build --nologo `
  --filter "FullyQualifiedName~StockPolicyTests.ModifySale"
```

---

## 4. فحص المانيفست

### المنهج

مقارنة `size_bytes` و`sha256` لكل ملف من `active_files` في `PACK_MANIFEST_v0.5.4.json` مع `MANIFEST.json` والملف الفعلي.

### النتيجة

| الملف | المسجل في المانيفست | الفعلي | النتيجة |
|---|---|---|---|
| `README.md` | 3,702 بايت / `530f1d7d…` | 10,405 بايت / `c1c9f10d…` | **غير مطابق** |
| `PROJECT_RULES.md` | 22,650 بايت / `63bd2964…` | مطابق | مطابق |
| `AGENTS.md` | 22,787 بايت / `4c8a27f8…` | 22,787 بايت / `a438b7c4…` | **غير مطابق** |
| `docs/PRODUCT_MASTER.md` | 7,240 بايت / `ab63d720…` | مطابق | مطابق |
| `docs/MVP_AND_ACCEPTANCE.md` | 13,097 بايت / `4e379531…` | مطابق | مطابق |
| `docs/TECHNICAL_AND_SAFETY_RULES.md` | 7,495 بايت / `4a69dbbf…` | مطابق | مطابق |
| `docs/DECISIONS_AND_OPEN_GATES.md` | 6,556 بايت / `505b4f62…` | 10,061 بايت / `a6a15a58…` | **غير مطابق** |
| `docs/ARCHIVE_INDEX.md` | 2,378 بايت / `a088ee6b…` | مطابق | مطابق |
| `docs/FINAL_HANDOFF_AND_CLOSEOUT_v0.5.4.md` | 14,761 بايت / `9a6753f3…` | مطابق | مطابق |

**النتيجة:** 3 من 9 ملفات نشطة غير مطابقة. الحزمة v0.5.4 لم تعد متسقة مع نفسها.

---

## 5. فحوص ساكنة

### 5.1 المصادقة والصلاحيات

**الأمر:**

```powershell
rg -n "AddAuthentication|AddAuthorization|\[Authorize|AuthenticationState|Login" src tests -g "*.cs" -g "*.razor"
```

**النتيجة:** لا توجد أي نتيجة تتعلق بالمصادقة أو التفويض. النتائج الوحيدة كانت `LastLoginAt` في كيان المستخدم.

**الأمر:**

```powershell
rg -n "HasPermission" src tests -g "*.cs" -g "*.razor"
```

**النتيجة:** تعريفان فقط في `src/Wizz.Domain/User.cs`، ولا استدعاء من الواجهات أو حالات الاستخدام.

### 5.2 حركات المخزون

**الأمر:**

```powershell
rg -n "StockMovement" src/Wizz.Application/UseCases/Sales src/Wizz.Domain/Sale.cs
```

**النتيجة:** لا توجد أي نتيجة.

**الأمر:**

```powershell
rg -n "StockMovement" src -g "*.cs"
```

**النتيجة:** الحركات تُنشأ فقط في:
- `AdjustStockUseCase`
- `UpdateProductUseCase`
- `CreateProductUseCase`

ولا تُنشأ في `CreateSaleUseCase` أو `CancelSaleUseCase` أو `ModifySaleUseCase`.

### 5.3 تحصيل الديون

**الأمر:**

```powershell
rg -n "ApplyPayment" src tests -g "*.cs" -g "*.razor"
```

**النتيجة:** تعريف واحد فقط في `src/Wizz.Domain/Sale.cs`، ولا استدعاء.

### 5.4 النسخ الاحتياطي وWAL

**الأمر:**

```powershell
rg -n "journal_mode|WAL|PRAGMA|BackupDatabase|VACUUM INTO" src -g "*.cs"
```

**النتيجة:** لا توجد إعدادات WAL صريحة ولا استخدام لـ`BackupDatabase` أو `VACUUM INTO`.

**ملفات قاعدة البيانات:**

| الملف | الحجم | آخر تعديل |
|---|---:|---|
| `wizz.db` | 184,320 | 2026-10-28 22:37:40 |
| `wizz.db-shm` | 32,768 | 2026-10-28 22:39:34 |
| `wizz.db-wal` | 8,272 | 2026-10-28 22:39:34 |

وجود `-wal` و`-shm` يعني أن نسخ `wizz.db` وحده ليس لقطة متسقة مضمونة.

### 5.5 مفتاح AI

**الملف:** `src/Wizz.Infrastructure/AI/AiSettingsProvider.cs`

**النتيجة:** `CloudApiKey` يُخزن ويُقرأ كنص صريح في `ai-settings.json`، دون DPAPI أو تشفير.

### 5.6 حقول تدقيق البيع

**الملف:** `src/Wizz.Infrastructure/Data/Migrations/20261028210146_RemoveSaleAuditColumns.cs`

**النتيجة:** المهاجرة تحذف صراحة:
- `IsModified`
- `ModificationNote`
- `ModifiedAt`
- `ModifiedByUserId`

### 5.7 بيانات الدخول الثابتة

**الملف:** `src/Wizz.Presentation/Wizz.Presentation/Program.cs`

**النتيجة:** زرع `admin` بكلمة `Admin@123` و`cashier` بكلمة `Cashier@123`.

### 5.8 حالة Git

```powershell
git status --short
```

**النتيجة:** لا مخرجات — شجرة العمل نظيفة. مخرجات البناء في `bin/obj` مستثناة.

---

## 6. ما لم يتم التحقق منه

| البند | السبب |
|---|---|
| تشغيل التطبيق واختبار الواجهة | مراجعة ساكنة فقط؛ لا تشغيل تفاعلي |
| استعادة فعلية في بيئة منفصلة | تتطلب إذنًا وبيانات اختبار معزولة |
| نشر Windows / `dotnet publish` | خارج نطاق المراجعة |
| RTL بصريًا على جهاز فعلي | يتطلب جهاز/عرض فعلي |
| الطابعة والماسح | يتطلب أجهزة |
| السحابة/Ollama | يتطلب مفاتيح/خدمات خارجية |
| الامتثال القانوني والضريبي | يتطلب مراجعة قانونية جزائرية |
| التزامن تحت حمل | يتطلب اختبار حمل متعدد المستخدمين |

---

## 7. إعادة إنتاج النتائج

```powershell
# 1. التأكد من النظافة
git status --short

# 2. البناء
dotnet build Wizz.sln -c Release --nologo

# 3. الاختبارات
dotnet test Wizz.sln -c Release --no-build --nologo

# 4. فحص المانيفست
$m = Get-Content -Raw MANIFEST.json | ConvertFrom-Json
$p = Get-Content -Raw PACK_MANIFEST_v0.5.4.json | ConvertFrom-Json
foreach ($f in $p.active_files) {
  $entry = $m.files | Where-Object { $_.path -eq $f } | Select-Object -First 1
  $actual = Get-Item $f
  $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
  if ($entry.size_bytes -ne $actual.Length -or $entry.sha256 -ne $hash) {
    "MISMATCH`t$f"
  } else {
    "OK`t$f"
  }
}
```

---

## 8. الخلاصة

- البناء سليم تقنيًا لكنه ملوث بـ3,772 تحذيرًا.
- الاختبارات ليست خضراء: 2/238 فاشلان في مسار تعديل البيع.
- المانيفست غير متسق: 3/9 ملفات نشطة.
- الفحوص الساكنة تؤكد غياب المصادقة/الصلاحيات، وغياب حركات المخزون في البيع، وغياب تحصيل الديون، وضعف النسخ الاحتياطي.
- ما لم يُختبر صراحةً لا يجوز اعتباره ناجحًا.

---

**نهاية سجل التحقق.**
