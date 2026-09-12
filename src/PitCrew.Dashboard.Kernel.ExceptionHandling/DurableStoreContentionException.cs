namespace PitCrew.Dashboard.Kernel.ExceptionHandling;

/// <summary>
/// Reports that a durable-store operation exhausted its bounded contention policy.
/// </summary>
public sealed class DurableStoreContentionException : Exception
{
  private const string DefaultMessage =
      "Durable storage remained contended after bounded retry attempts.";

  /// <summary>
  /// Initializes a new instance of the
  /// <see cref="DurableStoreContentionException"/> class.
  /// </summary>
  public DurableStoreContentionException()
      : base(DefaultMessage)
  {
  }

  /// <summary>
  /// Initializes a new instance of the
  /// <see cref="DurableStoreContentionException"/> class.
  /// </summary>
  /// <param name="message">The error message.</param>
  public DurableStoreContentionException(string? message)
      : base(message)
  {
  }

  /// <summary>
  /// Initializes a new instance of the
  /// <see cref="DurableStoreContentionException"/> class.
  /// </summary>
  /// <param name="message">The error message.</param>
  /// <param name="innerException">
  /// The exception that caused this exception.
  /// </param>
  public DurableStoreContentionException(
      string? message,
      Exception? innerException)
      : base(message, innerException)
  {
  }

  /// <summary>
  /// Initializes a new instance with the provider failure that exhausted contention handling.
  /// </summary>
  /// <param name="innerException">The provider-specific contention failure.</param>
  public DurableStoreContentionException(Exception innerException)
      : base(DefaultMessage, innerException)
  {
  }
}
