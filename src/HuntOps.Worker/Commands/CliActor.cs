using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Users;

namespace HuntOps.Worker.Commands;

/// <summary>Actor for worker CLI commands; acts on behalf of the owner once one exists.</summary>
internal sealed class CliActor : ICurrentActor
{
    public string ActorId => "cli";

    public string UserId { get; set; } = Owner.PlaceholderUserId;

    public ChangeChannel Channel => ChangeChannel.Cli;
}
