# نموذج بيانات مفاهيمي — غير نهائي، تصميم فقط

## مجموعات كيانات متوقعة
- المؤسسة/المتجر: Store, StoreSettings, Branch, Device.
- الهوية: LocalUser, Role, Permission, Session, Delegation, Approval.
- المنتجات: Product, Barcode, Unit, Price, ProductBundle/BOM.
- المخزون: StockBalance, StockMovement, StockAdjustment, StockCount, Warehouse, Batch, SerialNumber, Reservation.
- المبيعات: Sale, SaleLine, Payment, Return/Exchange, SaleVoid/Correction.
- الأطراف: Customer, Supplier, BranchProfile, Agreement.
- المشتريات: PurchaseOrder, GoodsReceipt, SupplierInvoice, SupplierQuote, SupplierReturn.
- الوثائق: BusinessDocument, DocumentLine, DocumentSequence, Attachment, TemplateVersion.
- المالية التشغيلية: CashDrawer, Shift, Expense, BankAccount, Cheque, Loan, Budget, Settlement.
- الموارد البشرية والأصول: Employee, Contract, Attendance, PayrollRecord, Asset, Maintenance, FleetVehicle.
- الحوكمة: AuditLog, ApprovalRequest, Incident, Risk, RiskTreatment, RiskException.
- المزامنة/الأتمتة: OutboxMessage, InboxProcessedEvent, SyncCheckpoint, SyncAttempt, ConflictRecord, Task, DomainEvent, AIToolExecution.

## قواعد التصميم المبدئية
- معرف تقني ثابت منفصل عن رقم الوثيقة الظاهر؛ StoreId ثابت لا يتغير بتغيير اسم المتجر.
- المال يستخدم نوعًا عشريًا دقيقًا (مثل decimal/numeric)، لا float/double.
- سجل الحركات أصل للمخزون؛ الرصيد قيمة مشتقة أو مخزنة للتسريع مع ضوابط اتساق.
- لا تعدّل بيعًا معتمدًا بصمت؛ استخدم إلغاء/تصحيح موثقًا. لا تعِد استخدام رقم وثيقة صدر بالفعل.
- تحقق من السعر والصلاحيات في طبقة التطبيق الموثوقة، لا تثق بمدخل الواجهة وحده.
- لكل مجال سياسة مصدر حقيقة ومزامنة وتعارض محددة؛ لا Last Write Wins عام.
- سجّل وقت العملية والتسجيل والمزامنة مع المنطقة الزمنية ومعالجة انحراف الساعة.
- تفاصيل الكيانات والقيود والعلاقات والترحيلات لا تُحسم قبل تحليل متطلبات مفصل، ولا تُنشأ قاعدة بيانات/ترحيلات قبل إذن التنفيذ.

## حالة النطاق
الدفعات والصلاحية والأرقام التسلسلية والمرتجعات والديون والضرائب والوثائق أصبحت احتياجات معتمدة في المخطط العام بدرجات مختلفة بحسب نوع المنتج/النشاط. تبقى قواعد التنفيذ الدقيقة والامتثال القانوني والتحقق من العمليات بحاجة إلى تفصيل، وليست أسئلة مفتوحة لإعادة حسم أصل الحاجة.
