namespace Wizz.Application.AI.Perception;

/// <summary>سطر مقروء مقبول من المحكّم: الاسم والسعر والكمية كما قرئت، بلا توزيع على أي حقل.</summary>
/// <param name="Name">اسم الصنف كما قرأه القارئ.</param>
/// <param name="Price">السعر المقروء، أو <c>null</c> إن لم يُقرأ.</param>
/// <param name="Quantity">الكمية المقروءة أو ١ إن لم تُقرأ.</param>
public sealed record OcrDraftLine(string Name, decimal? Price, decimal Quantity = 1m);

/// <summary>ما تستلمه الشاشة الهدف: النص الخام للمراجعة، والأصناف المقبولة للمطابقة الاختيارية.</summary>
/// <param name="Text">النص المستخرج كما قرئ.</param>
/// <param name="Lines">الأصناف التي قبلها المحكّم؛ قد تكون فارغة.</param>
public sealed record OcrDraftPayload(string Text, IReadOnlyList<OcrDraftLine> Lines);

/// <summary>
/// ينقل نتيجة قراءة المستند من شاشة القراءة إلى شاشة البيع أو الشراء داخل الجلسة الحالية فقط.
/// لا يكتب في قاعدة البيانات ولا في ملف؛ يُستلم مرة واحدة ثم يُفرَّغ. المطابقة والتطبيق قرار المستخدم في الشاشة الهدف.
/// </summary>
public sealed class OcrDraftHandoff
{
    public const string SaleTarget = "sale";
    public const string PurchaseTarget = "purchase";

    private readonly object _gate = new();
    private string? _text;
    private IReadOnlyList<OcrDraftLine> _lines = Array.Empty<OcrDraftLine>();
    private string? _target;

    /// <summary>يودع نصًا والأصناف المقبولة للمراجعة في الشاشة الهدف، بلا تعديل للمحتوى.</summary>
    public void Set(string? text, string target, IReadOnlyList<OcrDraftLine>? lines = null)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(target)) return;
        lock (_gate)
        {
            _text = text;
            _lines = lines?.ToArray() ?? Array.Empty<OcrDraftLine>();
            _target = target.Trim();
        }
    }

    /// <summary>يسترد الحمولة مرة واحدة إن كانت موجّهة لهذا الهدف، ثم يمسحها.</summary>
    public OcrDraftPayload? Take(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        lock (_gate)
        {
            if (_text is null || !string.Equals(_target, target.Trim(), StringComparison.Ordinal)) return null;
            var payload = new OcrDraftPayload(_text, _lines);
            _text = null;
            _lines = Array.Empty<OcrDraftLine>();
            _target = null;
            return payload;
        }
    }

    /// <summary>يمسح أي حمولة بانتظار الاستلام.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _text = null;
            _lines = Array.Empty<OcrDraftLine>();
            _target = null;
        }
    }
}
