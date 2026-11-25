namespace CargoHub.Api.Errors;

/// <summary>The request conflicts with existing data; the controllers turn it into a 409.</summary>
public sealed class ConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
