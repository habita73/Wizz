# تصور هيكل الحل — غير منفذ وغير معتمد تفصيليًا

**الحالة:** تصور للمراجعة المعمارية فقط. لا تنشئ أي مشروع من هذا المخطط قبل إذن صريح.

الاتجاه العام المعتمد هو .NET/C# وBlazor وPostgreSQL (Q241)، لكن نمط Blazor، شكل التطبيق المحلي، حدود المشاريع، قاعدة البيانات المحلية/الخادمة، طريقة التعبئة والتحديث لم تُحسم تفصيليًا. لذلك لا يُعد المخطط التالي قرارًا معماريًا نهائيًا.

```text
VentAuComptoir/
├── PROJECT_RULES.md
├── AGENTS.md
├── docs/
├── prompts/
├── src/                 # تصور مستقبلي فقط
│   ├── Local/           # بيع محلي، واجهة، خدمات، تخزين/تكاملات
│   ├── Modules/         # Sales, Products, Inventory, Customers, Suppliers, Finance, HR...
│   ├── BuildingBlocks/  # Domain, Application, Contracts, Infrastructure
│   ├── Integrations/    # Printing, Barcode, Email, AI, Payment (optional)
│   └── Cloud/           # خدمات اختيارية عند اعتمادها
└── tests/               # Unit, Integration, Sync, Security, Recovery, UX/RTL
```

- `Modular Monolith` خيار مبدئي مقترح للمراجعة، وليس اعتمادًا نهائيًا. لا تبدأ Microservices دون حاجة مثبتة.
- لا تفترض أن كل المكونات المذكورة مشاريع مستقلة؛ يمكن أن تكون وحدات داخلية.
- PostgreSQL واتجاه .NET لا يمنحان إذنًا بإنشاء Solution أو مشاريع قابلة للبناء.
- قبل التنفيذ يلزم قرار معماري موثق، وتحديد حدود النواة والوحدات وطريقة التثبيت والعمل دون اتصال والنسخ/الاستعادة.
