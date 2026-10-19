namespace Wizz.Application.Common;

/// <summary>
/// نتيجة عملية — نجاح مع قيمة أو فشل مع أخطاء.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly IReadOnlyList<string>? _errors;

    public Result(T value)
    {
        _value = value;
        _errors = null;
        IsSuccess = true;
    }

    public Result(IReadOnlyList<string> errors)
    {
        _value = default;
        _errors = errors;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("لا يمكن الوصول إلى Value عند الفشل. تحقق من IsSuccess أولاً.");

    public IReadOnlyList<string> Errors => IsFailure
        ? _errors!
        : throw new InvalidOperationException("لا توجد أخطاء عند النجاح.");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(params string[] errors) => new(errors);
    public static Result<T> Failure(IReadOnlyList<string> errors) => new(errors);

    public static implicit operator Result<T>(T value) => Success(value);
}

/// <summary>
/// نتيجة عملية بدون قيمة إرجاع — نجاح أو فشل مع أخطاء.
/// </summary>
public readonly struct Result
{
    private readonly IReadOnlyList<string>? _errors;

    public Result()
    {
        _errors = null;
        IsSuccess = true;
    }

    public Result(IReadOnlyList<string> errors)
    {
        _errors = errors;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<string> Errors => IsFailure
        ? _errors!
        : throw new InvalidOperationException("لا توجد أخطاء عند النجاح.");

    public static Result Success() => new();
    public static Result Failure(params string[] errors) => new(errors);
    public static Result Failure(IReadOnlyList<string> errors) => new(errors);
}