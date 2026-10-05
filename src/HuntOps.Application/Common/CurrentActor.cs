using HuntOps.Domain.Actions;
using HuntOps.Domain.Users;

namespace HuntOps.Application.Common;

/// <summary>Who is performing the current operation, and through which interface.</summary>
public interface ICurrentActor
{
    /// <summary>Audit identity, e.g. "apikey:hops_ab12cd34" or "cli".</summary>
    string ActorId { get; }

    /// <summary>The user whose data is affected (V1: always the single owner).</summary>
    string UserId { get; }

    ChangeChannel Channel { get; }
}

/// <summary>Default actor for background/system work and the CLI.</summary>
public sealed class SystemActor : ICurrentActor
{
    public string ActorId => "system";

    public string UserId => Owner.PlaceholderUserId;

    public ChangeChannel Channel => ChangeChannel.System;
}
