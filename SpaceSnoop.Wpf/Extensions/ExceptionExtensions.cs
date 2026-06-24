namespace SpaceSnoop.Wpf.Extensions;

public static class ExceptionExtensions
{
    public static Exception Unwrap(this Exception exception)
    {
        return exception is AggregateException aggregate
            ? aggregate.Flatten().InnerException ?? exception
            : exception;
    }
}
