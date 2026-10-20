# قنوات الدعم (Support Channels)

شكراً لاستخدامك **Wizz**! إليك كيفية الحصول على المساعدة.

## 📚 المساعدة الذاتية (Self-Service)

| المورد | الوصف | الرابط |
|---|---|---|
| **README** | نقطة البداية، التثبيت، التشغيل السريع | [README.md](README.md) |
| **التوثيق** | أدلة مفصلة، معمارية، قرارات | [docs/](docs/) |
| **CHANGELOG** | سجل التغييرات والإصدارات | [CHANGELOG.md](CHANGELOG.md) |
| **FAQ** | الأسئلة الشائعة | [docs/FAQ.md](docs/FAQ.md) (قريباً) |

## 💬 الدعم المجتمعي (Community Support)

### GitHub Discussions (مفضل للأسئلة العامة)
- **الأسئلة العامة**: كيف أفعل كذا؟ ما هو الأفضل؟
- **الأفكار/المقترحات**: ميزة جديدة، تحسين
- **العرض/المشاركة**: مشروعك المبني على Wizz
- **الرابط**: [GitHub Discussions](https://github.com/habita73/wizz/discussions)

### GitHub Issues (للمشاكل المحددة)
- **Bug Reports**: خطأ قابل لإعادة الإنتاج
- **Feature Requests**: طلب ميزة جديدة موثقة
- **الرابط**: [GitHub Issues](https://github.com/habita73/wizz/issues)

> **قبل فتح Issue**: ابحث في Issues و Discussions الموجودة — قد يكون سؤالك مطروحاً مسبقاً.

## 🛠️ الدعم التقني للمطورين

| الحاجة | القناة |
|---|---|
| إعداد بيئة التطوير | [CONTRIBUTING.md](CONTRIBUTING.md) |
| مراجعة الكود (Code Review) | Pull Request على GitHub |
| قرارات معمارية (ADR) | [docs/decisions/](docs/decisions/) |
| تقارير CI/CD | تبويب Actions في GitHub |

## 📧 الدعم المباشر (للعملاء/الشركاء)

إذا كنت **عميلاً تجارياً** أو **شريكاً** وتحتاج دعماً مضموناً (SLA):

| المستوى | الوصف | وقت الاستجابة |
|---|---|---|
| **Community** | دعم مجتمعي مجاني | أفضل جهد (Best Effort) |
| **Standard** | بريد إلكتروني، تحديثات أمنية | ≤ 2 يوم عمل |
| **Professional** | بريد + هاتف، أولوية في الإصلاحات | ≤ 4 ساعات عمل |
| **Enterprise** | مدير حساب مخصص، SLA مخصص | ≤ 1 ساعة عمل |

**للاستفسارات التجارية**: [business@wizz-pos.com]

## 🐛 الإبلاغ عن خطأ (Bug Report)

استخدم قالب **[Bug Report](.github/ISSUE_TEMPLATE/bug_report.yml)** وتأكد من تضمين:
1. إصدار Wizz
2. نظام التشغيل وإصدار .NET
3. خطوات إعادة الإنتاج
4. السلوك المتوقع vs الفعلي
5. السجلات (Logs) ولقطات الشاشة

## 💡 طلب ميزة (Feature Request)

استخدم قالب **[Feature Request](.github/ISSUE_TEMPLATE/feature_request.yml)**.

## 🔒 الإبلاغ عن ثغرة أمنية

**لا تفتح Issue عاماً!** راجع **[SECURITY.md](SECURITY.md)** للقنوات الصحيحة.

## ❓ الأسئلة الشائعة (FAQ)

### س: كيف أبدأ استخدام Wizz؟
ج: راجع [README.md](README.md) — قسم "التشغيل السريع".

### س: هل يدعم Wizz اللغة الفرنسية/الإنجليزية؟
ج: الواجهة الأساسية عربية RTL. الفرنسية والإنجليزية مخططة كترجمات لاحقة.

### س: هل يمكنني استخدام قاعدة بيانات غير SQLite؟
ج: المعمارية تدعم Multi-provider (PostgreSQL، SQL Server). حالياً SQLite فقط مفعل افتراضياً.

### س: كيف أضيف طابعة حرارية جديدة؟
ج: نفذ واجهة `IPrinter` في `Wizz.Application.Abstractions` وسجلها في DI.

### س: هل يعمل AI بدون إنترنت؟
ج: نعم، `LocalLlmProvider` يستخدم Ollama محلياً. السحابي اختياري.

---

**لم تجد إجابتك؟** افتح [Discussion](https://github.com/habita73/wizz/discussions) أو [Issue](https://github.com/habita73/wizz/issues/new/choose).
