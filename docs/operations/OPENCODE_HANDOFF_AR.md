# تسليم OpenCode — نواة الحسابات المالية (النسخة العربية)

- **المرجع المعتمد:** حزمة التوثيق `v0.5.4`؛ نطاق MVP هو P0 إلزامي وP1 اختياري.
- **خط أساس Git:** `9587b29ecb6e16769f90376f833f6fdaedcc3cb7` — `docs: establish v0.5.4 baseline`.
- **حالة المهمة:** تنفيذ محدود غير ملتزم به؛ لا يعني قبولًا نهائيًا أو فتحًا لمهمة جديدة.

## القواعد المنفذة

- جميع الحسابات النقدية تستخدم `decimal`؛ لا `float` أو `double`.
- سعر الوحدة والخصم النقدي الثابت والمدفوع يقبلون منزلتين عشريتين كحد أقصى؛ ترفض الدقة الزائدة.
- الكمية قد تكون كسرية بلا حد دقة مصطنع.
- يقرب كل إجمالي سطر إلى منزلتين بـ`MidpointRounding.AwayFromZero`، ثم تجمع إجماليات السطور.
- الخصم مبلغ ثابت على كامل البيع. يقبل خصمًا مساويًا للإجمالي؛ لا يقبل خصمًا سالبًا أو أكبر منه.
- يقبل دفعًا مساويًا للإجمالي؛ لا يقبل دفعًا سالبًا أو أكبر منه.
- ترفض النواة البيع الفارغ، ومجموعة السطور `null`، والسطر `null`، وتحول تجاوز النطاق الحسابي إلى `SaleCalculationException` واضحة.

## الملفات المتغيرة غير الملتزمة

- `global.json` و`VentAuComptoir.sln`.
- `src/VentAuComptoir.Domain/`: مشروع Domain، `Money.cs`، `SaleLine.cs`، `SaleCalculator.cs`، `SaleTotals.cs`، و`SaleCalculationException.cs`.
- `tests/VentAuComptoir.Domain.Tests/`: مشروع الاختبار و`SaleCalculatorTests.cs`.
- هذا الملف: `docs/operations/OPENCODE_HANDOFF_AR.md`.

## التحقق الفعلي (مُصرَّح به في التسليم الأصلي)

نُفِّذت الأوامر التالية من جذر المشروع:

```powershell
dotnet build VentAuComptoir.sln --configuration Release --no-restore --nologo
dotnet test VentAuComptoir.sln --configuration Release --no-restore --nologo
```

**نتيجة مُصرَّح بها في التسليم الأصلي:** البناء نجح بلا تحذيرات أو أخطاء؛ الاختبارات `24` ناجحة و`0` فاشلة و`0` متجاوزة. فحص المصدر لم يجد استخدامًا لـ`float` أو`double` في `src` أو`tests`.

**ملاحظة:** هذه النتائج مُصرَّح بها في مستند التسليم الأصلي، ولم يتم التحقق منها بشكل مستقل في هذه المراجعة.

## القيود والحالة التالية

- لا تتضمن هذه النواة التخزين أو منع تكرار البيع أو الصلاحيات أو الآجل أو الطباعة أو الواجهة أو النسخ والاستعادة أو VPS.
- الكمية الكسرية مسموحة ولا تفرض الوثائق حد دقة لها؛ يلزم قرار لاحق فقط إذا ظهرت حاجة تجارية إلى سياسة دقة للوحدات.
- لا يوجد commit للتنفيذ. التغييرات المقصودة تظهر كغير ملتزمة عند `git status`، وخط الأساس أعلاه غير معدل.
- الإجراء الآمن التالي: يراجع فريِد هذه التغييرات ويوافق عليها قبل أي commit أو مهمة تنفيذ جديدة.

## تعليمات إلزامية إلى OpenCode

> Before acting, read `AGENTS.md`, `PROJECT_RULES.md`, `README.md`, `docs/PRODUCT_MASTER.md`, `docs/TECHNICAL_AND_SAFETY_RULES.md`, and the relevant decision and acceptance documents. Treat Farid as the final authority for architectural, product, and commercial decisions. Do not create new modules, alter approved scope, commit changes, or start another task without Farid's explicit approval.