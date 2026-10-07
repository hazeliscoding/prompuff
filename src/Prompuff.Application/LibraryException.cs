namespace Prompuff.Application;

/// <summary>An expected problem with a message that is safe to show the user as-is.</summary>
public sealed class LibraryException(string userMessage, Exception? innerException = null)
    : Exception(userMessage, innerException);
