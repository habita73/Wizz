namespace Wizz.Application.AI.Artifacts;

/// <summary>نوع المخرج (artifact) الذي يولّده المساعد داخل رده.</summary>
public enum ArtifactKind
{
    /// <summary>نص/تقرير منسّق قابل للعرض والتنزيل والطباعة.</summary>
    Text,

    /// <summary>جدول بيانات (صفوف/أعمدة) قابل للتنزيل CSV/JSON والطباعة.</summary>
    Table,

    /// <summary>صورة (base64 أو مسار أو رابط).</summary>
    Image,

    /// <summary>ملف/مستند (مثل PDF) معطى كـ base64 أو رابط للتنزيل.</summary>
    File,

    /// <summary>رابط خارجي/داخلي قابل للفتح.</summary>
    Link
}

/// <summary>
/// مخرج واحد ولّده المساعد. يُشتق من وسم <c>wizz-artifact</c> داخل نص الرد،
/// ويُعرض في الواجهة ككائن تفاعلي بأزرار: عرض/تنزيل/طباعة/حذف.
/// </summary>
public sealed class ChatArtifact
{
    /// <summary>معرّف فريد للكائن داخل الجلسة.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>نوع المخرج.</summary>
    public ArtifactKind Kind { get; set; } = ArtifactKind.Text;

    /// <summary>عنوان يُعرض للمستخدم (مثل «فاتورة #B43701»).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>وصف مختصر اختياري.</summary>
    public string? Description { get; set; }

    /// <summary>نوع MIME للتنزيل (مثل text/csv أو application/pdf).</summary>
    public string? MimeType { get; set; }

    /// <summary>اسم ملف التنزيل المقترح (بدون امتداد).</summary>
    public string? FileName { get; set; }

    /// <summary>المحتوى النصي (لـ Text/Table/Link).</summary>
    public string? Content { get; set; }

    /// <summary>عناوين الأعمدة (لـ Table).</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>صفوف البيانات (لـ Table).</summary>
    public List<List<string>> Rows { get; set; } = new();

    /// <summary>بيانات الصورة/الملف: base64 (بدون بادئة) أو رابط.</summary>
    public string? Data { get; set; }

    /// <summary>الرابط (لـ Link أو مصدر الصورة/الملف عند وجوده).</summary>
    public string? Url { get; set; }

    /// <summary>هل حُذف هذا المخرج من العرض؟</summary>
    public bool IsRemoved { get; set; }

    /// <summary>هل يحتوي بيانات قابلة للعرض مباشرة؟</summary>
    public bool HasPayload =>
        !string.IsNullOrWhiteSpace(Data) || !string.IsNullOrWhiteSpace(Url) ||
        !string.IsNullOrWhiteSpace(Content) || Rows.Count > 0;

    /// <summary>هل المحتوى النصي HTML مصرّح به (format=html)؟ غير ذلك يُعرض كنص آمن.</summary>
    public bool IsHtml =>
        Kind == ArtifactKind.Text &&
        string.Equals(MimeType, "text/html", StringComparison.OrdinalIgnoreCase);
}
