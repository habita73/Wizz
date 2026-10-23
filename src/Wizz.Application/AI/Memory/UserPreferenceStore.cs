namespace Wizz.Application.AI.Memory;

/// <summary>نوع السعر الذي يُعرض أولاً في إجابات المساعد.</summary>
public enum PriceMode
{
    /// <summary>سعر التجزئة — الخيار الافتراضي.</summary>
    Retail = 0,

    /// <summary>سعر الجملة.</summary>
    Wholesale = 1
}

/// <summary>
/// تفضيلات المستخدم التي يحترمها المساعد: نوع السعر المفضّل، وحدة القياس المفضّلة،
/// ومفتاح الخصوصية «حفظ السياق». تنفيذ نقي قابل للاختبار —
/// حفظها في localStorage مسؤولية طبقة الواجهة.
/// </summary>
public sealed class UserPreferenceStore
{
    /// <summary>مفتاح نوع السعر في اللقطة المحفوظة.</summary>
    public const string PriceModeKey = "priceMode";

    /// <summary>مفتاح وحدة القياس المفضّلة في اللقطة المحفوظة.</summary>
    public const string PreferredUnitKey = "preferredUnit";

    /// <summary>مفتاح خيار حفظ السياق في اللقطة المحفوظة.</summary>
    public const string RememberContextKey = "rememberContext";

    /// <summary>مفتاح السماح للمساعد المدمج بالبحث في الإنترنت (مغلق افتراضياً).</summary>
    public const string InternetAccessKey = "allowBuiltInInternet";

    /// <summary>الحد الأقصى لطول اسم الوحدة المفضّلة.</summary>
    public const int MaxUnitLength = 20;

    /// <summary>نوع السعر المفضّل (افتراضي: التجزئة).</summary>
    public PriceMode PriceMode { get; private set; } = PriceMode.Retail;

    /// <summary>وحدة القياس المفضّلة (رمز أو اسم) إن وُجدت.</summary>
    public string? PreferredUnit { get; private set; }

    /// <summary>هل يحتفظ المساعد بسياق المحادثة؟ (مفتاح الخصوصية — مفعّل افتراضياً).</summary>
    public bool RememberContext { get; private set; } = true;

    /// <summary>
    /// هل يُسمح للمساعد المدمج بالبحث في الإنترنت عند طلب صريح؟ (مغلق افتراضياً — Local-first).
    /// </summary>
    public bool AllowInternetAccess { get; private set; }

    /// <summary>هل السعر المفضّل هو سعر الجملة؟</summary>
    public bool IsWholesalePreferred => PriceMode == PriceMode.Wholesale;

    /// <summary>تعيين نوع السعر المفضّل.</summary>
    public void SetPriceMode(PriceMode mode)
        => PriceMode = mode == PriceMode.Wholesale ? PriceMode.Wholesale : PriceMode.Retail;

    /// <summary>تعيين/مسح وحدة القياس المفضّلة.</summary>
    public void SetPreferredUnit(string? unit)
    {
        var trimmed = unit?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            PreferredUnit = null;
            return;
        }

        PreferredUnit = trimmed.Length > MaxUnitLength ? trimmed[..MaxUnitLength] : trimmed;
    }

    /// <summary>تفعيل/تعطيل حفظ سياق المحادثة.</summary>
    public void SetRememberContext(bool enabled) => RememberContext = enabled;

    /// <summary>تفعيل/تعطيل البحث في الإنترنت للمساعد المدمج.</summary>
    public void SetAllowInternetAccess(bool enabled) => AllowInternetAccess = enabled;

    /// <summary>إعادة كل التفضيلات إلى القيم الافتراضية.</summary>
    public void Reset()
    {
        PriceMode = PriceMode.Retail;
        PreferredUnit = null;
        RememberContext = true;
        AllowInternetAccess = false;
    }

    /// <summary>لقطة نصّية قابلة للتخزين (تُحفظ كـ JSON في localStorage).</summary>
    public IReadOnlyDictionary<string, string> ToSnapshot() => new Dictionary<string, string>
    {
        [PriceModeKey] = PriceMode.ToString(),
        [PreferredUnitKey] = PreferredUnit ?? string.Empty,
        [RememberContextKey] = RememberContext ? "true" : "false",
        [InternetAccessKey] = AllowInternetAccess ? "true" : "false"
    };

    /// <summary>استعادة التفضيلات من لقطة محفوظة (قيم غير صالحة تُتجاهل).</summary>
    public static UserPreferenceStore FromSnapshot(IReadOnlyDictionary<string, string>? snapshot)
    {
        var store = new UserPreferenceStore();
        if (snapshot is null)
        {
            return store;
        }

        if (snapshot.TryGetValue(PriceModeKey, out var mode) &&
            Enum.TryParse<PriceMode>(mode, ignoreCase: true, out var parsed))
        {
            store.SetPriceMode(parsed);
        }

        if (snapshot.TryGetValue(PreferredUnitKey, out var unit))
        {
            store.SetPreferredUnit(unit);
        }

        if (snapshot.TryGetValue(RememberContextKey, out var remember))
        {
            store.SetRememberContext(!string.Equals(remember, "false", StringComparison.OrdinalIgnoreCase));
        }

        if (snapshot.TryGetValue(InternetAccessKey, out var internet))
        {
            store.SetAllowInternetAccess(string.Equals(internet, "true", StringComparison.OrdinalIgnoreCase));
        }

        return store;
    }
}
