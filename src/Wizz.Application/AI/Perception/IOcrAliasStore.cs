namespace Wizz.Application.AI.Perception;

/// <summary>اسم مقروء ربطه المستخدم صراحةً بمنتج موجود. يُحفظ محليًا، بلا بيانات فاتورة ولا بيانات عميل.</summary>
/// <param name="RawName">الاسم كما قرأه القارئ.</param>
/// <param name="ProductName">اسم المنتج المعتمد الذي اختاره المستخدم.</param>
/// <param name="Confirmations">عدد مرات تأكيد المستخدم للربط نفسه.</param>
public sealed record OcrAlias(string RawName, string ProductName, int Confirmations);

/// <summary>
/// خزنة أسماء محلية تتعلّم فقط من تأكيد صريح من المستخدم. ليست تدريبًا لنموذج، ولا ترسل أي شيء،
/// ولا تحفظ إلا اسم الصنف ومرادفه. قابلة للعرض والحذف والإفراغ في أي وقت.
/// </summary>
public interface IOcrAliasStore
{
    IReadOnlyList<OcrAlias> GetAll();

    /// <summary>يعيد اسم المنتج المرتبط بالاسم المقروء، أو <c>null</c> إن لم يكن متعلّمًا.</summary>
    string? Resolve(string? rawName);

    void Remember(string rawName, string productName);

    void Forget(string rawName);

    void Clear();
}
