using System.Globalization;
using System.Text.Json;

namespace Wizz.Application.Devices;

/// <summary>
/// رسالة محاكاة قارئ الباركود بين نافذة المحاكي وتبويب البيع.
/// نصية بالكامل، ولا تمثل جهازًا حقيقيًا ولا رمز QR.
/// </summary>
public sealed record VirtualScanMessage(
    string Type,
    string Code = "",
    decimal Quantity = 1m,
    string? ProductName = null,
    bool Ok = false)
{
    /// <summary>يقرأ الرسالة القادمة من المتصفح بأمان؛ أي صيغة غير متوقعة تُرفض.</summary>
    public static VirtualScanMessage? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            var code = root.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString() ?? ""
                : "";

            var quantity = 1m;
            if (root.TryGetProperty("quantity", out var quantityElement))
            {
                quantity = quantityElement.ValueKind switch
                {
                    JsonValueKind.Number when quantityElement.TryGetDecimal(out var numeric) => numeric,
                    JsonValueKind.String when decimal.TryParse(
                        quantityElement.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var text) => text,
                    _ => 1m
                };
            }

            if (quantity <= 0)
            {
                quantity = 1m;
            }

            var productName = root.TryGetProperty("productName", out var nameElement)
                ? nameElement.GetString()
                : null;

            var ok = root.TryGetProperty("ok", out var okElement)
                     && okElement.ValueKind == JsonValueKind.True;

            return new VirtualScanMessage(type.Trim(), code.Trim(), quantity, productName, ok);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
