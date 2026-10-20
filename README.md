<div align="center">

# 🛒 Wizz — نظام نقاط بيع ذكي

**نظام POS حديث، محلي أولاً، عربي RTL، مع مساعد AI — مصمم للتجار في الجزائر والعالم العربي**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Build Status](https://img.shields.io/github/actions/workflow/status/habita73/wizz/ci.yml?branch=main&logo=github-actions&logoColor=white)](https://github.com/habita73/wizz/actions/workflows/ci.yml)
[![Tests](https://img.shields.io/badge/tests-31%2F31%20passing-brightgreen?logo=xunit&logoColor=white)](https://github.com/habita73/wizz/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/badge/coverage-coming%20soon-yellow)](https://github.com/habita73/wizz/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?logo=opensourceinitiative&logoColor=white)](LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/habita73/wizz?style=social)](https://github.com/habita73/wizz/stargazers)
[![GitHub Forks](https://img.shields.io/github/forks/habita73/wizz?style=social)](https://github.com/habita73/wizz/network/members)

[![Arabic RTL](https://img.shields.io/badge/RTL-العربية-green?logo=googletranslate&logoColor=white)](#-العربية-rtl-أصيل)
[![Local First](https://img.shields.io/badge/Local--First-نعم-orange?logo=databricks&logoColor=white)](#-local-first-العمل-بدون-إنترنت)
[![AI Powered](https://img.shields.io/badge/AI-Ollama%20Local-purple?logo=openai&logoColor=white)](#-مساعد-ai-ذكي)
[![Modular](https://img.shields.io/badge/Architecture-Modular%20Monolith-blue?logo=architecture&logoColor=white)](#-معمارية-قابلة-للتوسع)

</div>

---

## 🎯 لماذا Wizz؟

| الميزة | الوصف |
|---|---|
| **🏪 محلي أولاً** | يعمل بالكامل دون إنترنت — البيع لا يتوقف أبداً |
| **🇩🇿 عربي RTL أصيل** | واجهة عربية حقيقية، أرقام عربية، طباعة RTL، دعم اللهجة الجزائرية |
| **🤖 مساعد AI ذكي** | نموذج لغوي محلي (Ollama) — خصوصية تامة، لا بيانات تغادر جهازك |
| **🧩 وحدات قابلة للتوسع** | ابدأ بالبيع فقط، أضف المخزون، العملاء، التقارير لاحقاً |
| **💰 نموذج تجاري عادل** | ترخيص محلي دائم + خدمات سحابية اختيارية، بياناتك ملكك دائماً |
| **🔧 مصمم للجزائر** | أرقام ضريبية، سجل تجاري، دنانير، طابعات حرارية عربية، إجراءات محاسبية محلية |

---

## 🚀 التشغيل السريع (Quick Start)

### المتطلبات
- Windows 10/11 (64-bit)
- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) أو SDK للتطوير
- (اختياري) [Ollama](https://ollama.ai/) للمساعد AI المحلي

### التثبيت للمستخدمين
```bash
# تحميل أحدث إصدار من Releases
# فك الضغط وتشغيل Wizz.exe
```

### التثبيت للمطورين
```bash
# 1. استنساخ
git clone https://github.com/habita73/wizz.git
cd wizz

# 2. استعادة الحزم والبناء
dotnet restore Wizz.sln
dotnet build Wizz.sln --configuration Release

# 3. تشغيل الاختبارات
dotnet test Wizz.sln --configuration Release

# 4. تشغيل التطبيق
cd src/Wizz.Presentation/Wizz.Presentation
dotnet run --configuration Release
```

---

## 🏗️ المعمارية (Architecture)

```
Wizz.sln
├── src/
│   ├── Wizz.Domain/              🧠 النواة المالية + كيانات المجال
│   │   ├── Money, SaleCalculator, SaleLine, SaleTotals
│   │   ├── Product, UnitOfMeasure, Sale, SaleItem
│   │   ├── Store, User, DeviceRegistration, Subscription
│   ├── Wizz.Application/         🎯 حالات الاستخدام، DTOs، واجهات
│   │   ├── UseCases (Sales, Products, Store, Backup, Receipt, AI)
│   │   ├── Abstractions (Repositories, Devices, AI, Backup)
│   │   ├── DTOs, Common (Result, ValidationErrors)
│   ├── Wizz.Infrastructure/      🔧 تنفيذات EF Core، أجهزة، AI، خدمات
│   │   ├── Data (WizzDbContext, Migrations)
│   │   ├── Repositories (Product, Sale, Store, User, UnitOfWork)
│   │   ├── Devices (MockScanner, MockPrinter)
│   │   ├── AI (LocalLlmProvider/Ollama, AiToolRegistry)
│   │   ├── Services (BackupService)
│   │   └── Extensions (DI Registration)
│   └── Wizz.Presentation/        🖥️ Blazor Web App (Windows-hosted)
│       ├── Pages (Sales, Products, Reports, Settings, AI)
│       ├── Components (RTL Layout, Arabic Inputs)
│       └── wwwroot (Assets, JS Interop)
└── tests/
    ├── Wizz.Domain.Tests/        ✅ 24 اختبار للنواة المالية
    └── Wizz.Integration.Tests/   ✅ 7 اختبارات تكامل (بيع، آجل، إيصال، SQLite)
```

### المبادئ المعمارية
- **Modular Monolith**: حدود واضحة بين الطبقات، قابل للاستخراج لخدمات مستقبلاً
- **Dependency Inversion**: الطبقات العليا تعرف واجهات، التنفيذات في Infrastructure
- **Clean Architecture**: Domain لا يعتمد على شيء، Application يعتمد على Domain فقط
- **Local-First**: لا اعتماد على شبكة، قاعدة بيانات ملف واحد (SQLite)
- **AI via Tools**: النموذج لا يصل لقاعدة البيانات مباشرة، يستخدم أدوات محددة بواجهات

---

## ✨ الميزات الأساسية (MVP - P0)

### 💰 النواة المالية (مختبرة: 24/24 اختبار)
- حسابات نقدية دقيقة (`decimal`، منزلتان عشريتان، `MidpointRounding.AwayFromZero`)
- سطر بيع بصفر مقبول مع تنبيه غير مانع
- منتجات مجانية (سعر > 0، إجمالي = 0 بعد تقريب)
- خصم ثابت على كامل البيع (لا نسبة مئوية)
- مدفوع/باقي في طبقة مستقلة عن النواة

### 🏪 إدارة المتجر والمنتجات
- متجر واحد، مستخدمون (كاشير/مدير/أدمن)، صلاحيات مبنية على الأدوار
- منتجات/خدمات: سعر تجزئة + جملة، وحدات متعددة (كيس↔كجم)، باركود
- بحث سريع، منع تكرار الباركود

### 🧾 عملية البيع
- سطور متعددة، إجماليات فورية، خصم، مدفوع/باقي، بيع آجل اختياري
- إيصال أساسي مع حقول قانونية دنيا (قابلة للتوسع)
- تصحيح/إلغاء موثق (يحفظ الأصل + الأثر + السبب + الفاعل)

### 💾 البيانات والنسخ الاحتياطي
- SQLite محلي (ملف واحد، نسخ/استعادة بنسخ الملف)
- نسخ احتياطي آلي/يدوي، استعادة في بيئة منفصلة للتحقق
- تدقيق (Audit Trail) للعمليات الحساسة

### 🤖 مساعد AI (محلي Ollama)
- أدوات محددة: ملخص المبيعات، بحث منتج، معلومات المتجر
- لا SQL مباشر، لا أوامر نظام، صلاحيات المستخدم مطبقة
- طلب غامض → يطلب توضيح، لا يخترع أرقاماً

### 🖨️ الأجهزة (محاكاة للتطوير)
- `MockScanner`: إدخال باركود نصي
- `MockPrinter`: معاينة HTML/نص، جاهز لـ ESC/POS الحقيقي

---

## 🗺️ خارطة الطريق (Roadmap)

| المرحلة | الميزات | الحالة |
|---|---|---|
| **MVP (P0)** | البيع، المنتجات، المتجر، المستخدمون، النسخ، AI، الإيصال | 🚧 **قيد التطوير** |
| **P1** | مخزون أساسي، عملاء/ديون، استيراد، طابعة/قارئ حقيقيين | 📋 مخطط |
| **V1.0** | تقارير متقدمة، تصدير PDF/Excel، إعدادات متقدمة | 📋 مخطط |
| **V1.5** | مزامنة متعددة الأجهزة، PostgreSQL، باقات/اشتراكات | 🔮 مستقبلي |
| **V2.0** | تطبيق هاتف، بوابة سحابية، تكاملات خارجية | 🔮 مستقبلي |

---

## 🤝 المساهمة

نرحب بمساهماتكم! راجعوا [دليل المساهمة](CONTRIBUTING.md) للبدء.

1. Fork المستودع
2. أنشئ فرع: `git checkout -b feature/amazing-feature`
3. التزم بالمعايير: `dotnet format`، اختبارات، تعليقات XML
4. Commit: `git commit -m 'feat: add amazing feature'`
5. Push وافتح Pull Request

---

## 📄 الترخيص

هذا المشروع مرخص تحت **رخصة MIT** — راجع [LICENSE](LICENSE) للتفاصيل.

---

## 🙏 شكر وتقدير

- **[.NET](https://dotnet.microsoft.com/)** — المنصة
- **[Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)** — واجهة الويب
- **[Entity Framework Core](https://efcore/)** — ORM
- **[Ollama](https://ollama.ai/)** — نماذج لغوية محلية
- **[OllamaSharp](https://github.com/ollama/ollamasharp)** — عميل .NET لـ Ollama
- **[xUnit](https://xunit.net/)** — إطار الاختبارات
- **[Contributor Covenant](https://www.contributor-covenant.org/)** — مدونة السلوك

---

## 📞 تواصل ودعم

| القناة | الوصف |
|---|---|
| [GitHub Issues](https://github.com/habita73/wizz/issues) | أخطاء، طلبات ميزات |
| [GitHub Discussions](https://github.com/habita73/wizz/discussions) | أسئلة، نقاشات، أفكار |
| [Security Policy](SECURITY.md) | الإبلاغ عن ثغرات أمنية |
| [Support](SUPPORT.md) | قنوات الدعم الكاملة |

---

<div align="center">

**صنع بـ ❤️ للتجار في الجزائر والعالم العربي**

*إذا أعجبك المشروع، لا تنسَ النجمة ⭐ — تدعم الاستمرارية!*

</div>
