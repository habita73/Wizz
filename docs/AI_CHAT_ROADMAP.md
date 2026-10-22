# خارطة طريق تحسينات المحادثة والـ AI المدمج — مرجع مستمر

> **هذا الملف هو المرجع الوحيد المعتمد** لتتبع العمل من البداية للنهاية.  
> يُحدّث عند كل مرحلة مكتملة، ويُحذف عند الانتهاء الكامل.

---

## 📍 الحالة الحالية (Last Updated: 2026-10-22)

| المكون | الحالة |
|---------|-------|
| **ModelRouter** | ✅ يعمل (Built-in → Local → Cloud) |
| **Ollama** | ✅ متاح — `qwen2.5:0.5b-instruct` (397 MB) |
| **OpenRouter** | ✅ يعمل — مفتاح محفوظ، اختبار `200 OK` |
| **اختيار الجهة** | ✅ في UI (Auto / Built-in / Local / Cloud) |
| **محدد النموذج (Popover)** | ✅ داخل صندوق الإرسال في المحادثة |
| **شارة الجهة في الرسائل** | ✅ في ChatMessage.razor |
| **اختبارات** | ✅ 109/109 pass (24 Domain + 85 Integration) |
| **App URL** | `http://localhost:5183` — AI: `/ai`، Settings: `/settings` |
| **المرحلة 1أ** | ✅ مكتملة (زر «تعديل» على السؤال فقط + «نسخ» على الرد، أيقونات SVG داخلية، محدد النموذج داخل صندوق الإرسال، إلغاء التحرير يعمل) |
| **المرحلة 1ب** | ✅ مكتملة (محرك المخرجات: تحليل/عرض/تنزيل/طباعة/حذف + 21 اختباراً) |
| **المرحلة 2** | ✅ مكتملة (أدوات النظام: وقت/أقراص/طباعة/مواصفات + 18 اختباراً) |
| **المرحلة 3** | ✅ مكتملة (أسئلة جاهزة ذكية + تاريخ محفوظ + زر مسح + 27 اختباراً) |
| **المرحلة 4** | ✅ مكتملة (لوحة حالة في سطر واحد + `<details>` + tooltips + أزرار اختبار مصغّرة + الإعدادات التفصيلية في `/settings`) |

---

## 🗺️ المراحل العشر (54 مهمة إجمالية)

### ✅ المرحلة 0: أساسيات — **مكتملة 100%**
- [x] ModelRouter routing logic
- [x] LocalLlmProvider مع auto-selection
- [x] CloudLlmProvider (OpenRouter) مع probe محسن
- [x] AiSettingsProvider مع persistence (`%LOCALAPPDATA%\Wizz\ai-settings.json`)
- [x] DeviceCapabilityEvaluator مع RAM حقيقية + caching
- [x] UI: اختيار الجهة، اختبار السحابة، حالة المسارات

---

### ✅ المرحلة 1أ: واجهة رسائل تفاعلية (8 مهام) — **مكتملة ومنقّحة**

| # | المهمة | الملف | Done |
|---|---------|-------|------|
| 1أ-1 | `ChatMessage.razor` مكون موحد: زر «تعديل» على السؤال فقط + «نسخ» على الرد (أيقونات SVG داخلية + tooltip عربي) | `Pages/AI/ChatMessage.razor` | ✅ |
| 1أ-2 | حالة الرسائل `List<ChatItem>` + `ChatItem.cs` record (محادثة مسطّحة) | `Pages/AI/AIChat.razor` + `ChatItem.cs` | ✅ |
| 1أ-3 | ~~حذف رسالة~~ — أُلغي بطلب Farid (يكفي التعديل) | — | ⛔ |
| 1أ-4 | تحرير سؤال → حذف كل ما بعدها وإعادة البناء | `AIChat.razor.EditMessage()` + `TruncateFrom()` | ✅ |
| 1أ-5 | ~~إعادة توليد من الرد~~ — أُلغي بطلب Farid (الزر على السؤال فقط؛ الرد للنسخ) | — | ⛔ |
| 1أ-6 | نسخ الرد فقط (Clipboard API) | `AIChat.razor.CopyMessage()` | ✅ |
| 1أ-7 | ضبط تخطيط الرسائل (flex بدل float، لا تداخل) | `AIChat.razor` + `app.css (.msg-content)` | ✅ |
| 1أ-8 | محدد الجهة/النموذج داخل صندوق الإرسال (`.input-group`) | `ModelSelectorPopover.razor` + `AIChat.razor` | ✅ |
| 1أ-9 | إصلاح إلغاء وضع التحرير (`@key` لثبات هوية المكونات + toggle حالة محلي) | `ChatMessage.razor` + `AIChat.razor` | ✅ |

---

### ✅ المرحلة 1ب: اختيار النموذج من الشات + محرك المخرجات (6 مهام)

| # | المهمة | الملف | Done |
|---|---------|-------|------|
| 1ب-1 | `ModelSelectorPopover.razor` — اختيار الجهة من داخل المحادثة | `Pages/AI/ModelSelectorPopover.razor` | ✅ |
| 1ب-2 | شارة الجهة في كل رسالة | `ChatMessage.razor` | ✅ |
| 1ب-3 | تغيير الجهة → `ForceRoute` للرسائل اللاحقة | `AIChat.razor` (مطبّق) | ✅ |
| 1ب-4 | محرك المخرجات (Artifacts): `ChatArtifact` + `ArtifactParser` + `ArtifactExporter` + `ArtifactMarkers` + `ArtifactCard.razor` + `ChatExportModal.razor` (HTML/MD/JSON/CSV/TXT + طباعة/PDF) | `src/Wizz.Application/AI/Artifacts/*` + `Pages/AI/*` | ✅ |
| 1ب-5 | أنواع المخرجات المدعومة: Text/Table/Image/File/Link (لا حصر بقوالب ثابتة) | `ArtifactParser.cs` / `ArtifactCard.razor` | ✅ |
| 1ب-6 | طباعة مباشرة إلى PDF عبر المتصفح (`wizzPrint` → `@page A4` RTL) | `wwwroot/js/artifact-export.js` + `ArtifactExporter.ToPrintableDocument` | ✅ |
| 1ب-7 | إجراءات المخرج: عرض/معاينة، تنزيل، طباعة، حذف | `ArtifactCard.razor` | ✅ |
| 1ب-8 | اختبارات محرك المخرجات (21 اختباراً) | `tests/Wizz.Integration.Tests/ArtifactEngineTests.cs` | ✅ |

---

### ✅ المرحلة 2: أدوات النظام للـ Built-in (7 مهام)

| # | الأداة | الملف | Done |
|---|-------|-------|------|
| 2-1 | Registry | `AI/Tools/BuiltInToolRegistry.cs` + `IBuiltInTool.cs` + `BuiltInToolContext.cs` | ✅ |
| 2-2 | `GetTimeTool` | `AI/Tools/GetTimeTool.cs` | ✅ |
| 2-3 | `GetDriveInfoTool` | `AI/Tools/GetDriveInfoTool.cs` | ✅ |
| 2-4 | `GetPrintTemplatesTool` | `AI/Tools/GetPrintTemplatesTool.cs` | ✅ |
| 2-5 | `GetSystemInfoTool` | `AI/Tools/GetSystemInfoTool.cs` | ✅ |
| 2-6 | دمج في `BuiltInAssistant` | `BuiltInAssistant.cs` | ✅ |
| 2-7 | Tests | `tests/Wizz.Integration.Tests/BuiltInToolsTests.cs` (18 اختباراً) | ✅ |

---

### ✅ المرحلة 3: أسئلة جاهزة ذكية (5 مهام)

| # | المهمة | الملف | Done |
|---|---------|-------|------|
| 3-1 | تخزين تاريخ (LocalStorage + منطق نقي قابل للاختبار) | `AI/History/AiHistoryService.cs` + `Pages/AI/AiHistoryStore.cs` + `wwwroot/js/ai-history.js` | ✅ |
| 3-2 | عداد تكرار → أعلى 5 (مع كسر التعادل بالأحدث) | `AiHistoryService.Top` | ✅ |
| 3-3 | `QuickQuestionsBar.razor` — شريط أفقي | `Pages/AI/QuickQuestionsBar.razor` | ✅ |
| 3-4 | اقتراحات سياقية (حسب الصفحة) + دمج التاريخ | `AI/History/QuickQuestionSuggestions.cs` | ✅ |
| 3-5 | ضغطة = إرسال | `QuickQuestionsBar.razor` → `SendQuickQuestion` | ✅ |
| 3-6 | زر «مسح تاريخ الأسئلة» | `AIChat.razor` (`ClearHistory`) | ✅ |
| 3-7 | Tests (27 اختباراً) | `tests/Wizz.Integration.Tests/AiHistoryTests.cs` | ✅ |

**تحقق المتصفح (Phase 3):** الشريط يُعرض بـ4 اقتراحات سياقية + الشريط الجانبي + الحالة الفارغة؛ نقرة واحدة أرسلت `مبيعات اليوم` وظهر سؤال المستخدم + رد المساعد؛ `localStorage['wizz.ai.history.v1']` سجّل السؤال وعدّاده؛ بقي التاريخ بعد إعادة التحميل؛ زر «مسح تاريخ الأسئلة» أفرغ التخزين (`[]`) وأخفى عدّاد البطاقة.

---

### ✅ المرحلة 4: تبسيط اللوحة الجانبية (5 مهام)

| # | المهمة | الملف | Done |
|---|---------|-------|------|
| 4-1 | بطاقة حالة → سطر واحد | `Pages/AI/AiStatusPanel.razor` (سطر `⚡ مدمج · 🟢 نموذج · ☁️ سحابي` + شارات) | ✅ |
| 4-2 | `<details>` للتفاصيل | `AiStatusPanel.razor` (طي افتراضي + روابط الإعدادات) | ✅ |
| 4-3 | Tooltips (❓) بدل شروحات طويلة | `AiStatusPanel.razor` + تلميح صندوق الإدخال في `AIChat.razor` | ✅ |
| 4-4 | أزرار اختبار مصغرة (🔄) | `AiStatusPanel.razor` (تحديث + اختبار محلي/سحابي) + `ModelSelectorPopover.razor` (تحديث قائمة النماذج) | ✅ |
| 4-5 | نقل إعدادات تفصيلية لSettings فقط | `Pages/Settings/SettingsDashboard.razor` (النموذج المفضّل + مستوى الجهاز + دليل Ollama + شارات الحالة) — حُذفت بطاقتا «النموذج المحلي» و«الذكاء الاصطناعي السحابي» من الشريط الجانبي | ✅ |

**تحقق المتصفح (Phase 4):** الشريط الجانبي أصبح 3 بطاقات فقط (أسئلة جاهزة / المسارات / الجلسة)؛ سطر الحالة يُظهر `⚡ مدمج 🟢 qwen2.5:0.5b-instruct ☁️ OpenRouter` مع tooltip كامل؛ `<details>` يعرض 3 صفوف (مدمج / محلي / سحابي) مع tooltips ❓ وأزرار 🔄؛ اختبار المحلي أعاد `✅ النموذج المحلي يعمل — qwen2.5:0.5b-instruct` واختبار السحابة أعاد `✅ الاتصال بالسحابة (OpenRouter (سحابي مجاني)) ناجح.`؛ صفحة الإعدادات تعرض «النموذج المحلي المفضّل» (5 نماذج Ollama) + 🔄 + «تطبيق» (جرى التبديل إلى `qwen2.5-coder:7b` ثم العودة إلى `qwen2.5:0.5b-instruct`، وانعكس ذلك في سطر حالة المحادثة) + «قدرة الجهاز: قوي» + «متصل بـ Ollama — 5 نموذج مثبّت.» + شارات `⚡ مدمج 🟢 محلي ☁️ سحابي 3`. البناء ناجح (0 أخطاء) والاختبارات 109/109 ناجحة.

---

### 🟢 المرحلة 5: ذاكرة تفضيلات + سياق محادثة (5 مهام)

| # | المكون | Done |
|---|---------|------|
| 5-1 | `IConversationMemory` | ☐ |
| 5-2 | `ConversationMemory` | ☐ |
| 5-3 | `UserPreferenceStore` | ☐ |
| 5-4 | دمج في `BuiltInAssistant` | ☐ |
| 5-5 | زر «مسح الذاكرة» | ☐ |

---

### 🟢 المرحلة 6: توجيه تلقائي ذكي + أولوية قابلة للترتيب (5 مهام)

| # | المهمة | Done |
|---|---------|------|
| 6-1 | إعداد `AutoRoutePriority` | ☐ |
| 6-2 | Drag & Drop في Settings | ☐ |
| 6-3 | خوارزمية وزن ذكية | ☐ |
| 6-4 | شفافية قرار التوجيه | ☐ |
| 6-5 | سجل قرارات التوجيه | ☐ |

---

### 🟡 المرحلة 7: إنترنت مشروط للـ Built-in (4 مهام — اختياري)

| # | المهمة | Done |
|---|---------|------|
| 7-1 | إعداد `AllowBuiltInInternetAccess` | ☐ |
| 7-2 | `SearchWebTool` | ☐ |
| 7-3 | دمج في `BuiltInAssistant` | ☐ |
| 7-4 | مؤشر بصري في الإجابة | ☐ |

---

### 🟡 المرحلة 8: تصدير PDF + UX نهائي (5 مهام)

| # | المهمة | Done |
|---|---------|------|
| 8-1 | ~~`jsPDF` integration~~ — قرار معتمد: PDF عبر طباعة المتصفح (`@page A4` RTL) بلا مكتبة إضافية | `ArtifactExporter.ToPrintableDocument` (منجزة في 1ب-6) | ⛔ |
| 8-2 | قوالب طباعة A4 | ☐ |
| 8-3 | اختصارات لوحة مفاتيح | ☐ |
| 8-4 | مؤشر تحميل لكل رسالة | ☐ |
| 8-5 | Integration tests | ☐ |

---

## 📁 الملفات الجديدة التي أُنشئت (المهمة 1أ-1)

| الملف | الغرض |
|-------|-------|
| `src/Wizz.Presentation/Wizz.Presentation/Pages/AI/ChatMessage.razor` | مكون رسالة موحد مع أفعال (تعديل/حذف/نسخ/إعادة توليد) |
| `src/Wizz.Presentation/Wizz.Presentation/Pages/AI/ChatItem.cs` | Record للبيانات لكل رسالة (مع Id، Route، CanRegenerate) |

## 📁 الملفات المعدلة (المهمة 1أ-1)

| الملف | التغيير |
|-------|---------|
| `src/Wizz.Presentation/Wizz.Presentation/Pages/AI/AIChat.razor` | استخدام `<ChatMessage>` بدلاً من العرض المضمن، إضافة طرق `DeleteMessage`/`EditMessage`/`CopyMessage`/`RegenerateMessage`، تحويل `ChatMessage` class إلى `ChatItem` record في ملف منفصل |

---

## ✅ معايير القبول (Definition of Done) — المرحلة 1أ

| المعيار | الحالة |
|---------|-------|
| Build succeeds — `dotnet build Wizz.sln -c Release` | ✅ |
| Tests pass — `dotnet test Wizz.sln -c Release` (43/43) | ✅ |
| Runtime verified — الرسائل تُرسل وتُستقبل | ✅ |
| رسائل تُعرض مع أزرار الإجراءات (تعديل/حذف/نسخ/إعادة توليد) | ✅ |
| Arabic RTL مدعوم | ✅ |

---

## 🔄 قاعدة العمل (Workflow Rule)

> **لا تبدأ مهمة جديدة قبل:**
> 1. إكمال المهمة الحالية + Build + Tests + تحقق يدوي
> 2. تحديث هذا الملف (☐ → ✅، إضافة ملاحظات إن وجدت)
> 3. **مراجعة Farid** (أو موافقة صريحة للانتقال)

---

## 📝 سجل التحديثات (Changelog)

| التاريخ | المرحلة/المهمة | التغيير | بواسطة |
|---------|--------------|---------|--------|
| 2026-10-21 | 0 | أساسيات مكتملة، نموذج توجيه يعمل، 43 tests pass | — |
| 2026-10-21 | — | إنشاء هذا الملف كمرجع مستمر | Assistant |
| 2026-10-21 | 1أ-1 | إنشاء `ChatMessage.razor` + `ChatItem.cs` + تحديث `AIChat.razor` | Assistant |
| 2026-10-21 | 1أ-2 إلى 1أ-7 | حذف/تحرير/نسخ/إعادة توليد/سلاسل مترابطة + تمرير سلس | Assistant |
| 2026-10-21 | 1ب-1، 1ب-2 | إنشاء `ModelSelectorPopover.razor` + دمجه في `AIChat.razor` + شارة الجهة في الرسائل | Assistant |
| 2026-10-21 | 1أ (تنقيح) | إزالة الحذف، زر «إعادة توليد» على السؤال والرد، سلوك «التقطيع وإعادة البناء» عند التعديل/إعادة التوليد، نصوص ظاهرة على الأزرار، نسخ للرد فقط | Assistant |
| 2026-10-21 | 1أ (تنقيح 2) | دمج التعديل وإعادة التوليد في زر أيقوني واحد، إصلاح تخطيط الرسائل (flex)، إصلاح محدد النموذج وإظهاره داخل وضع التحرير | Assistant |
| 2026-10-21 | 1أ (تنقيح 3) | نقل زر «تعديل» إلى السؤال فقط (الرد = نسخ)، أيقونات SVG داخلية (لا اعتماد على Bootstrap Icons غير المثبّتة)، إعادة محدد النموذج إلى داخل صندوق الإرسال، إصلاح إلغاء التحرير عبر `@key` | Assistant |
| 2026-10-22 | 1ب-4 → 1ب-8 | محرك المخرجات (Artifacts): نموذج `ChatArtifact`، محلّل الوسم `wizz-artifact`، مُصدِّر HTML/MD/JSON/CSV/TXT، مولّد الوسوم `ArtifactMarkers`، مكوّن `ArtifactCard.razor` (عرض/تنزيل/طباعة/حذف)، نافذة `ChatExportModal.razor`، JS محلي `artifact-export.js`، 21 اختباراً | Assistant |
| 2026-10-22 | 2-1 → 2-7 | أدوات النظام للمساعد المدمج: `IBuiltInTool`/`BuiltInToolContext`/`BuiltInToolRegistry` (عطل أداة لا يوقف السلسلة)، `GetTimeTool` (ميلادي + هجري)، `GetDriveInfoTool` (تنبيه نقص المساحة)، `GetPrintTemplatesTool`، `GetSystemInfoTool`، دمج في `BuiltInAssistant`، 18 اختباراً | Assistant |
| 2026-10-22 | 3-1 → 3-7 | أسئلة جاهزة ذكية: `AiHistoryService` (ترتيب بالتكرار + تطبيع عربي)، `QuickQuestionSuggestions`، جسر `AiHistoryStore` + `ai-history.js` (localStorage)، شريط `QuickQuestionsBar.razor`، زر «مسح تاريخ الأسئلة»، 27 اختباراً | Assistant |
| 2026-10-22 | 4-1 → 4-5 | تبسيط اللوحة الجانبية: `AiStatusPanel.razor` (سطر حالة واحد + `<details>` + tooltips ❓ + أزرار اختبار 🔄)، حذف بطاقتَي النموذج المحلي والسحابي من المحادثة، نقل «النموذج المحلي المفضّل» + دليل Ollama + قياس قدرة الجهاز إلى `/settings`، تلميح مختصر تحت صندوق الإدخال، إظهار سبب فشل قراءة الحالة بدل ابتلاعه | Assistant |

---

## 🗑️ عند الانتهاء الكامل
> احذف هذا الملف (`docs/AI_CHAT_ROADMAP.md`) وخطط `.opencode/plan/` — الكود والاختبارات ستكون الوثيقة الحية.