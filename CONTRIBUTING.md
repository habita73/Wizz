# دليل المساهمة في Wizz

شكراً لاهتمامك بالمساهمة في **Wizz**! هذا الدليل سيساعدك على البدء.

## 🚀 بداية سريعة

### المتطلبات
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git](https://git-scm.com/)
- محرر كود: [VS Code](https://code.visualstudio.com/) مع extensions (C# Dev Kit، C#، NuGet Gallery) أو [Visual Studio 2022](https://visualstudio.microsoft.com/) أو [JetBrains Rider](https://www.jetbrains.com/rider/)

### إعداد البيئة المحلية
```bash
# 1. استنساخ المستودع
git clone https://github.com/YOUR_USERNAME/wizz.git
cd wizz

# 2. استعادة الحزم
dotnet restore Wizz.sln

# 3. بناء الحل
dotnet build Wizz.sln --configuration Release

# 4. تشغيل الاختبارات
dotnet test Wizz.sln --configuration Release
```

## 🌿 سير العمل (Workflow)

### 1. إنشاء فرع (Branch)
```bash
# من main المحدث
git checkout main
git pull origin main
git checkout -b feature/your-feature-name
# أو: fix/your-bug-fix، docs/your-doc-update، refactor/your-refactor
```

**تسمية الفروع:**
- `feature/*` — ميزة جديدة
- `fix/*` — إصلاح خطأ
- `docs/*` — توثيق
- `refactor/*` — إعادة هيكلة
- `chore/*` — صيانة، تحديث تبعيات
- `test/*` — اختبارات

### 2. إجراء التغييرات
- اتبع **إرشادات الكود** أدناه
- أضف **اختبارات** لأي ميزة/إصلاح جديد
- حدث **التوثيق** إن لزم

### 3. اختبار التغييرات محلياً
```bash
# بناء
dotnet build Wizz.sln --configuration Release

# اختبارات
dotnet test Wizz.sln --configuration Release

# تنسيق (إن وجد dotnet-format)
dotnet format Wizz.sln
```

### 4. Commit و Push
```bash
# Commits ذات معنى
git add .
git commit -m "feat: add Arabic RTL support for thermal printers"

# أو للمهام المرتبطة بـ Issue
git commit -m "fix: resolve null reference in StoreSettings (closes #123)"

git push origin feature/your-feature-name
```

### 5. فتح Pull Request
- استخدم **قالب PR** (`.github/PULL_REQUEST_TEMPLATE.md`)
- اربط PR بـ Issue إن وجد
- تأكد من مرور CI

## 📏 إرشادات الكود

### أسلوب الكود (Code Style)
- نستخدم **StyleCop.Analyzers** و **Microsoft.CodeAnalysis.NetAnalyzers**
- `.editorconfig` يحدد التنسيق (مسافات 4، CRLF، UTF-8)
- `Directory.Build.props` يفعّل التحليل الصارم

### اتفاقيات التسمية (Naming Conventions)
| العنصر | الاتفاقية | مثال |
|---|---|---|
| Classes, Interfaces, Records, Enums | `PascalCase` | `ProductRepository`, `IProductRepository` |
| Methods, Properties, Events | `PascalCase` | `GetByIdAsync`, `IsActive` |
| Parameters, Local Variables | `camelCase` | `productId`, `cancellationToken` |
| Private Fields | `_camelCase` | `_dbContext`, `_logger` |
| Constants | `PascalCase` | `DefaultPageSize` |
| Test Methods | `PascalCase_Scenario_Expected` | `Calculate_SingleLine_ReturnsExpectedTotals` |

### تعليقات XML (للـ Public APIs)
```csharp
/// <summary>
/// يحصل على منتج بالمعرف مع وحداته.
/// </summary>
/// <param name="id">معرف المنتج</param>
/// <param name="ct">رمز الإلغاء</param>
/// <returns>المنتج أو null إذا لم يوجد</returns>
public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
```

### اللغة
- **الكود والتعليقات التقنية**: الإنجليزية (معيار الصناعة)
- **رسائل المستخدم، UI، السجلات الظاهرة**: العربية
- **أسماء المتغيرات/الدوال**: إنجليزية واضحة، لا تعريب (مثلاً `GetProductByBarcode` لا `GetProductByBarcodeArabic`)

### معالجة الأخطاء
- استخدم `Result<T>` و `Result` من `Wizz.Application.Common` للعمليات التي قد تفشل
- استثناءات المجال (`SaleCalculationException`، إلخ) للأخطاء غير المتوقعة في منطق الأعمال
- لا تستخدم `try/catch` العام إلا في الطبقة العلوية (Presentation)

### البرمجة غير المتزامنة
- استخدم `async/await` دائماً
- مرر `CancellationToken` في جميع الطرق غير المتزامنة العامة
- تجنب `.Result` و `.Wait()`

## 🧪 الاختبارات

### أنواع الاختبارات
| النوع | المجلد | متى تستخدم |
|---|---|---|
| Unit Tests | `tests/Wizz.Domain.Tests/` | منطق المجال، الحسابات، التحققات |
| Integration Tests | `tests/Wizz.Integration.Tests/` | EF Core، Repositories، قاعدة بيانات |
| UI Tests | `tests/Wizz.Presentation.Tests/` | Blazor Components، Pages |

### مبادئ الاختبار
- **AAA Pattern**: Arrange, Act, Assert
- اسم الاختبار: `MethodName_Scenario_ExpectedResult`
- اختبار واحد = تأكيد واحد منطقي (يمكن عدة Asserts مرتبطة)
- استخدم `FluentAssertions` للقراءة الأسهل
- بيانات الاختبار: استخدم `Bogus` أو `AutoFixture` للبيانات العشوائية

## 📝 التوثيق

- **README.md**: نقطة البداية، روابط سريعة
- **docs/**: توثيق معماري، قرارات (ADR)، أدلة
- **XML Comments**: للـ IntelliSense وتوليد الوثائق
- **CHANGELOG.md**: سجل التغييرات (Keep a Changelog format)

## 🔒 الأمان

- **لا** تضع أسراراً في الكود (API Keys، Passwords، Connection Strings)
- استخدم **User Secrets** للتطوير: `dotnet user-secrets set "Key" "Value"`
- للإنتاج: متغيرات بيئة أو Azure Key Vault / HashiCorp Vault
- راجع `SECURITY.md` للإبلاغ عن الثغرات

## 🏷️ الإصدارات (Versioning)

نتبع **Semantic Versioning (SemVer)**:
- `MAJOR.MINOR.PATCH` (مثلاً `1.2.3`)
- `MAJOR`: تغييرات كاسرة (Breaking)
- `MINOR`: ميزات جديدة متوافقة للخلف
- `PATCH`: إصلاحات أخطاء متوافقة للخلف

## 🤝 مدونة السلوك

يرجى قراءة واتباع [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## 📞 الحصول على المساعدة

- **GitHub Discussions**: للأسئلة العامة، الأفكار، النقاشات
- **GitHub Issues**: للأخطاء، طلبات الميزات
- **Security**: راجع `SECURITY.md`

---

> **نصيحة**: ابدأ بـ Issue صغير (Good First Issue) للتعرف على الكود قبل المساهمات الكبيرة.