namespace Wizz.Application.DTOs;

/// <summary>سجل تسديد دين، يُعرض في قائمة الديون وفي الإيصال المطبوع.</summary>
public sealed record DebtPaymentDto(
    Guid Id,
    Guid SaleId,
    decimal Amount,
    PaymentMethodDto Method,
    DateTime PaidAt,
    string? Note,
    decimal RemainingAfter);

/// <summary>إدخال تسديد جديد لدين بيع آجل.</summary>
public sealed record DebtPaymentInputDto(
    Guid SaleId,
    decimal Amount,
    PaymentMethodDto Method,
    string? Note = null);
