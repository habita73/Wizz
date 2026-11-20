namespace Wizz.Application.AI.Perception;

/// <summary>
/// اختيار القارئ لالتقاط صورة واحد، لأغراض المقارنة اليدوية فقط. الافتراضي هو القرار المعتمد:
/// قرة Windows أساسًا والقارئ المضمّن بديلًا أخيرًا حين لا تتوفر لغتها العربية.
/// </summary>
public enum OcrReaderChoice
{
    /// <summary>القرار المعتمد: Windows أساسًا، والمضمّن بديل أخير عند غياب القدرة.</summary>
    Default = 0,

    /// <summary>تشغيل قارئ Windows مباشرة، دون تبديل تلقائي عند فشل القراءة.</summary>
    Windows = 1,

    /// <summary>تشغيل القارئ المضمّن مباشرة، دون تبديل تلقائي عند فشل القراءة.</summary>
    RapidOcr = 2
}

/// <summary>حالة اختيار القارئ في الجلسة الحالية. لا تُحفظ ولا تُرسل ولا تغيّر الإعداد الافتراضي.</summary>
public interface IOcrReaderSelection
{
    OcrReaderChoice Current { get; }

    void Set(OcrReaderChoice choice);
}

/// <summary>اختيار قارئ لكل جلسة Blazor، يبدأ دائمًا بالخيار المعتمد.</summary>
public sealed class OcrReaderSelection : IOcrReaderSelection
{
    public OcrReaderChoice Current { get; private set; } = OcrReaderChoice.Default;

    public void Set(OcrReaderChoice choice) => Current = choice;
}

/// <summary>
/// يرسل القراءة إلى القارئ الذي اختاره المستخدم لهذه الصورة. الخيار الافتراضي يمرّ عبر
/// <see cref="CompositeOcrTextReader"/> فيبقى Windows أساسًا والمضمّن بديلًا أخيرًا كما هو معتمد.
/// لا يوجد تبديل تلقائي عند فشل القراءة، ولا أي مسار سحابي، ولا كتابة في قاعدة البيانات.
/// </summary>
public sealed class SelectableOcrTextReader : IOcrTextReader
{
    private readonly IOcrReaderSelection _selection;
    private readonly IOcrTextReader _defaultReader;
    private readonly IOcrTextReader _windowsReader;
    private readonly IOcrTextReader _rapidReader;

    public SelectableOcrTextReader(
        IOcrReaderSelection selection,
        IOcrTextReader defaultReader,
        IOcrTextReader windowsReader,
        IOcrTextReader rapidReader)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _defaultReader = defaultReader ?? throw new ArgumentNullException(nameof(defaultReader));
        _windowsReader = windowsReader ?? throw new ArgumentNullException(nameof(windowsReader));
        _rapidReader = rapidReader ?? throw new ArgumentNullException(nameof(rapidReader));
    }

    public bool IsSupported =>
        _defaultReader.IsSupported || _windowsReader.IsSupported || _rapidReader.IsSupported;

    public Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        var reader = _selection.Current switch
        {
            OcrReaderChoice.Windows => _windowsReader,
            OcrReaderChoice.RapidOcr => _rapidReader,
            _ => _defaultReader
        };

        return reader.ReadAsync(imageBytes, ct);
    }
}
