namespace Wizz.Application.Modules;

/// <summary>
/// وحدة قراءة الوثائق. نُقلت خلف بوابة الوحدات الاختيارية دون تغيير في سلوك المحكّم:
/// القراءة محلية، للعرض فقط، ولا تكتب في قاعدة البيانات ولا ترسل أي شيء.
/// </summary>
public static class DocumentReaderModule
{
    public const string Id = "documents.reader";

    public static readonly ModuleDescriptor Descriptor = new(
        id: Id,
        nameAr: "قراءة الوثائق",
        descriptionAr: "قراءة نصوص الصور محليًا وعرض حكم المدمج للعرض فقط، دون كتابة أو إرسال.",
        group: ModuleGroup.Documents,
        version: "1.0",
        requirementsAr: new[]
        {
            "بيانات لغتين مضمّنة محليًا (نحو ٥ ميغابايت)",
            "قرة Windows حين تتوفر كلغتها",
        },
        licenseCapability: ModuleLicenseCapability.Included);
}
