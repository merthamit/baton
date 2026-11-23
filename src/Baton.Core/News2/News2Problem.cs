namespace Baton.Core.News2;

/// <summary>A reason a parameter could not be scored.</summary>
public sealed record News2Problem(News2Parameter Parameter, ProblemCode Code, string Message);
