namespace DairyBidding.SharedKernel;

public readonly struct Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }

    private Result(T value) { Value = value; IsSuccess = true; Error = null; }
    private Result(string error) { Value = default; IsSuccess = false; Error = error; }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}
