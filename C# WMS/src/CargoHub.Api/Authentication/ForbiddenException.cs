namespace CargoHub.Api.Authentication;

/// <summary>The calling account may not do this; the controllers turn it into a 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
