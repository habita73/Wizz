# تسليم OpenCode — المرحلة 2: الوردية وبوابة الإغلاق والمرتجع (2026-11-14)

- **المرجع التشغيلي:** حزمة التوثيق `v0.6.2`؛ نطاق MVP P0 إلزامي وP1 اختياري (دون تغيير).
- **خط أساس الكود:** المرحلة 1 عند `9af2921`؛ حزمة `v0.6.1` عند `17c7124`؛ اقتراح المرحلة 2 عند `e124f17`.
- **حالة المهمة:** `DONE` ضمن نطاق D-31 الحرفي. القرار `PROPOSED` سابقًا صار `APPROVED` بتاريخ 2026-11-14.
- **القيود:** الصندوق والمشتريات والواجهات والصلاحيات (H-04) والمزامنة **خارج النطاق** وما زالت
  `NOT AUTHORIZED`.

## 1. ما أُنجز

| البند | الحالة |
|---|---|
| `CashierShift` + `ICashierShiftRepository`/`CashierShiftRepository` | `DONE` |
| `Sale.ShiftId` وحقول الإلغاء (`CancelledAt/ByUserId/CancelReason`) والتعديل (`ModifiedAt/ByUserId/ModificationReason`) | `DONE` |
| `OpenShiftUseCase` / `CloseShiftUseCase` / `GetOpenShiftUseCase` | `DONE` |
| الفتح التلقائي مع أول بيع داخل معاملة البيع | `DONE` |
| بوابة نافذة التعديل `ShiftGate` في التعديل والإلغاء (D-24) | `DONE` |
| `CreateSalesReturnUseCase` — مستند `MRB` جزئي بالأسطر مع منع تجاوز المتبقي | `DONE` |
| بطاقة المخزون: الملغى = خروج أصلي + دخول «إلغاء بيع» بتاريخ `CancelledAt` | `DONE` |
| المهاجرة `20261114123401_AddCashierShiftsAndSaleAudit` | `DONE` |
| `CashierShifts` في الجداول المطلوبة للنسخ الاحتياطي | `DONE` |

## 2. القرارات المطبقة

- **D-31:** تفويض صريح بتنفيذ المرحلة 2 بالنطاق الحرفي فقط.
- **القرارات الستة — الخيار (أ) في كل بند:**
  1. المرتجع الجزئي بالأسطر والكميات مع منع تجاوز المتبقي.
  2. `SalesTotal` = مجموع `Total` لغير الملغى؛ `CashTotal` = مجموع `Paid` لغير الآجل؛ `CreditTotal` = مجموع
     `Remaining` للآجل. تُجمَّد عند الإغلاق.
  3. المبيعات القديمة تبقى `ShiftId = NULL` وخارج نافذة التعديل.
  4. الكاشير صاحب الوردية يفتح ويغلق، مع قبول `ClosedByUserId` من الواجهة.
  5. بداية يوم العمل منتصف الليل المحلي بحسب `Store.TimeZone`.
  6. `MRB` بعد نافذة التعديل فقط، ومخزوني فقط بلا ردّ نقدي.
- **تفصيل تقني موثق:** حالة الوردية مشتقة من `ClosedAt` ولا تُخزَّن؛ ولا ورديتان مفتوحتان لنفس
  (المتجر، الكاشير) عبر فهرس فريد مفلتر `[ClosedAt] IS NULL`.
- **`MRB`** يستخدم النوع `SalesReturn` الموجود؛ لم يُضف نوع أو رمز جديد.

## 3. الملفات المتغيرة

- `src/Wizz.Domain/CashierShift.cs` (جديد)، `src/Wizz.Domain/Sale.cs`.
- `src/Wizz.Application/Abstractions/ICashierShiftRepository.cs` (جديد)، `IUnitOfWork.cs`.
- `src/Wizz.Application/UseCases/Sales/ShiftGate.cs`، `OpenShiftUseCase.cs`، `CloseShiftUseCase.cs`،
  `GetOpenShiftUseCase.cs`، `CreateSaleUseCase.cs`، `ModifySaleUseCase.cs`، `CancelSaleUseCase.cs`.
- `src/Wizz.Application/UseCases/StockDocuments/CreateSalesReturnUseCase.cs` (جديد)، `GetStockCardUseCase.cs`.
- `src/Wizz.Application/DTOs/CashierShiftDto.cs` (جديد).
- `src/Wizz.Infrastructure/Repositories/CashierShiftRepository.cs` (جديد)، `UnitOfWork.cs`.
- `src/Wizz.Infrastructure/Data/WizzDbContext.cs` + المهاجرة `20261114123401_AddCashierShiftsAndSaleAudit`.
- `src/Wizz.Infrastructure/Services/BackupService.cs`، `src/Wizz.Presentation/Wizz.Presentation/Program.cs`.
- `tests/Wizz.Integration.Tests/ShiftAndReturnTests.cs` (جديد)، `StockDocumentTests.cs`.
- `docs/DECISIONS_AND_OPEN_GATES.md` (D-31 + سجل التنفيذ)، `docs/proposals/PHASE2_AUTHORIZATION_REQUEST_AR.md`،
  `README.md`، `AGENTS.md`، `MANIFEST.json`، `PACK_MANIFEST_v0.6.2.json`.

## 4. التحقق الفعلي

| الأمر | النتيجة |
|---|---|
| `dotnet build Wizz.sln -c Release` | **0 أخطاء** |
| `dotnet test` — Domain | **24/24** |
| `dotnet test` — Integration | **245/245** (237 سابقة + 8 جديدة) |
| الاختبارات الجديدة (فلترة) | **8/8** — T-02أ، T-02ب، T-03، T-09، T-11، T-12، T-13، T-14 |
| مطابقة بصمات المانيفست | يُعاد توليدها ويُتحقق منها في نفس الوديعة |

**حدود التحقق:** الاختبارات على SQLite داخل الذاكرة وعلى ملف مؤقت، وليست اختبارًا على جهاز أو قاعدة إنتاجية.
T-14 يطبّق مهاجرة المرحلة 2 على قاعدة بمهاجرة المرحلة 1 ويتحقق من بقاء البيانات و`ShiftId = NULL`.

## 5. غير المحسوم / المؤجل

1. **عدّ النقدية وفرق الإغلاق وردّ المرتجع النقدي:** مع وحدة الصندوق (المرحلة 3).
2. **واجهة الوردية والمرتجع:** حالات الاستخدام جاهزة ومُسجلة في DI، لكن لا صفحات Blazor لها (خارج النطاق).
3. **الصلاحيات (H-04):** لا مصادقة ولا `[Authorize]`؛ الفتح/الإغلاق لا يقيّد الدور بعد.
4. **ساعة بداية يوم العمل:** منتصف الليل المحلي مثبت؛ إعداد الساعة `DEFERRED`.
5. **المشتريات والمزامنة:** خارج النطاق.

## 6. الخطوة التالية الواحدة

عرض **المرحلة 3** للتفويض (الصندوق/المشتريات/الواجهات/الصلاحيات/المزامنة)، مع توصية ببدء H-04 (المصادقة
والصلاحيات) أولًا لأنه يمس كل العمليات الحساسة. لا يُبدأ أي بند قبل تفويض صريح يذكره حرفيًا.

---

**تسليم قابل للنقل.** لا يعتمد على ذاكرة محادثة سابقة؛ الحالة الكاملة في الملفات المذكورة أعلاه.
