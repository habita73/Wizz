# سجل التغييرات (Changelog)

جميع التغييرات البارزة في هذا المشروع موثقة في هذا الملف.

التنسيق مبني على [Keep a Changelog](https://keepachangelog.com/ar/1.0.0/)،
وهذا المشروع يتبع [إصدار دلالي (SemVer)](https://semver.org/lang/ar/).

## [1.0.0] - TBD (الإصدار الأول المستقر)

### المضافة (Added)
- **النواة المالية**: `Money`، `SaleCalculator`، `SaleLine`، `SaleTotals`، `SaleCalculationException` مع 24 اختبار وحدة
- **كيانات المجال**: `Product`، `UnitOfMeasure`، `Sale`، `SaleItem`، `Store`، `User`، `DeviceRegistration`، `Subscription`
- **طبقة Application**: حالات استخدام للبيع، المنتجات، المتجر، النسخ الاحتياطي، الإيصال، AI
- **DTOs**: `ProductDto`، `SaleDto`، `StoreDto`، `UserDto`، `ReceiptDto`، `AiDto`
- **واجهات (Abstractions)**: مستودعات، وحدة عمل، أجهزة، AI، نسخ احتياطي
- **طبقة Infrastructure**: EF Core SQLite، مستودعات، UnitOfWork، محاكيات أجهزة، AI محلي (Ollama)، أدوات AI، خدمة نسخ احتياطي
- **طبقة Presentation**: Blazor Web App (Windows-hosted، RTL عربي)
- **إدارة الحزم المركزية**: `Directory.Build.props`، `Directory.Packages.props`
- **CI/CD**: GitHub Actions workflow للبناء، الاختبارات، تحليل الكود، الأمان، Docker، Release
- **قوالب GitHub**: Bug Report، Feature Request، Question، Pull Request Template
- **ملفات المجتمع**: LICENSE (MIT)، CONTRIBUTING، CODE_OF_CONDUCT، SECURITY، SUPPORT
- **توثيق**: README احترافي مع Badges، CHANGELOG

### الأمن (Security)
- سياسة أمان (SECURITY.md) مع عملية إبلاغ ثغرات مسؤولة
- Dependabot للتحديثات التلقائية للتبعيات
- فحص حزم ضعيفة في CI

### البنية التحتية (Infrastructure)
- `.editorconfig` لتنسيق موحد
- `.gitignore` شامل لـ .NET/Blazor
- `global.json` لتثبيت .NET SDK
- `CODEOWNERS` للمراجعة التلقائية

---

## [0.1.0] - 2026-10-19 (الإصدار التجريبي الأول - Preview)

### المضافة (Added)
- هيكل الحل الأولي (Domain، Application، Infrastructure، Presentation)
- النواة المالية مع اختبارات
- كيانات المجال الأساسية
- EF Core SQLite مع DbContext
- مستودعات و UnitOfWork
- محاكيات Scanner و Printer
- LocalLlmProvider مع OllamaSharp
- AiToolRegistry مع أدوات: مبيعات، منتجات، متجر
- BackupService لـ SQLite
- تسجيل DI مركزي

### الاختبارات (Tests)
- 24 اختبار وحدة للنواة المالية — كلها تمر ✅

### التوثيق (Documentation)
- README أساسي
- AGENTS.md، PROJECT_RULES.md (قواعد المشروع الداخلية)

---

## تنسيق الإصدارات المستقبلية

### [x.y.z] - YYYY-MM-DD

#### المضافة (Added)
- الميزات الجديدة

#### المحدثة (Changed)
- تغييرات في الوظائف الموجودة

#### المُصلحة (Fixed)
- إصلاح الأخطاء

#### المحذوفة (Removed)
- ميزات محذوفة

#### الأمن (Security)
- إصلاحات ثغرات

#### المُلغاة (Deprecated)
- ميزات ستلغى في إصدار قادم

---

## روابط الإصدارات

- [الإصدار 1.0.0 (مخطط)](../../milestone/1)
- [الإصدار 0.1.0 (الحالي)](../../releases/tag/v0.1.0)

---

> **ملاحظة**: هذا المشروع في مرحلة تطوير نشطة. الإصدارات قبل 1.0.0 قد تحتوي على تغييرات كاسرة (Breaking Changes) دون إشعار مسبق. راجع [CHANGELOG](CHANGELOG.md) قبل الترقية.