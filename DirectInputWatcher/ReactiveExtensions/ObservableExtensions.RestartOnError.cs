using System.Reactive.Linq;

namespace DirectInputWatcher;

/// <summary>
/// Reactive Extensions for IObservable.
/// </summary>
internal static partial class ObservableExtensions
{
    /// <summary>
    /// Catches errors from the observable, optionally emits an error notification,
    /// waits for a delay, then restarts the observable using the factory function.
    /// </summary>
    /// <typeparam name="T">The type of elements in the observable sequence.</typeparam>
    /// <param name="source">The source observable to monitor for errors.</param>
    /// <param name="restartFactory">A factory function that creates a new observable to restart the sequence.</param>
    /// <param name="restartDelay">The time to wait before restarting after an error occurs.</param>
    /// <param name="errorNotificationFactory">
    /// An optional factory function that creates a notification item from the caught exception.
    /// If null, no error notification is emitted.
    /// </param>
    /// <returns>
    /// An observable sequence that automatically restarts on errors after the specified delay,
    /// optionally emitting error notifications.
    /// </returns>
    /// <remarks>
    /// This operator provides resilience for long-running observable sequences by:
    /// <list type="bullet">
    /// <item>Intercepting errors that would normally terminate the sequence</item>
    /// <item>Optionally emitting an error notification to observers</item>
    /// <item>Waiting for the specified delay to prevent rapid retry loops</item>
    /// <item>Restarting the sequence using the provided factory function</item>
    /// </list>
    /// </remarks>
    public static IObservable<T> RestartOnError<T>(
        this IObservable<T> source,
        Func<IObservable<T>> restartFactory,
        TimeSpan restartDelay,
        Func<Exception, T>? errorNotificationFactory = null)
    {
        return source.Catch<T, Exception>(exception =>
        {
            IObservable<T> errorNotification = errorNotificationFactory != null
                ? Observable.Return(errorNotificationFactory(exception))
                : Observable.Empty<T>();

            return Observable.Concat(
                errorNotification,
                Observable.Timer(restartDelay).SelectMany(_ => restartFactory())
            );
        });
    }
}
