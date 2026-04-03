using MegaCrit.Sts2.Core.Models;

namespace SeedRollerCli;

internal sealed record NeowOptionInfo(
    ModelId ModelId,
    NeowChoiceKind Kind,
    string Title,
    string Description,
    string? Note);
