using System.Collections.Frozen;

namespace Bms.Application.Budgeting;

/// <summary>Execution-only exclusions approved on 2026-10-11 and completed on 2026-10-12. Source facts and
/// BDDS remain intact. These catalogue UUIDs were reconciled to source bindings;
/// catalogue codes are synthetic CF-* and editable names are not identities.</summary>
public static class ExecutionPaidExclusionPolicy
{
    private static readonly FrozenSet<Guid> Excluded = new Guid[]
    {
        Guid.Parse("897ee59f-8f63-42d2-84e8-19d46f436278"), // 311001 deposit placement
        Guid.Parse("1720650f-4d62-4535-b80f-134b7604f42c"), // 322001 deposit return
        Guid.Parse("08fe0af8-6015-4e3d-99fb-7c2ce9f0e1b6"), // 331001 own cash transfer out
        Guid.Parse("cb3c6997-2989-45de-9753-8b6be71aca62"), // 331002 own cash transfer in
        Guid.Parse("400a0f2f-24ee-42a5-8740-21e1ae11cb9f"), // 331003 collection transfer out
        Guid.Parse("d4567d9c-a6b3-4b3a-80d8-55afe5bb4535"), // 331004 collection transfer in
        Guid.Parse("abb33741-eb66-4988-8adb-7f6ede07aba5"), // 331005 own cash to bank
        Guid.Parse("ec632df1-aaf7-457b-89f9-d8f606aae5ca"), // 331006 collected cash to account
        Guid.Parse("b1fe559d-2792-4f9a-bb1c-a20633d02cd9"), // 331007 own bank to cash out
        Guid.Parse("f144967b-8bbe-4d57-9e54-54942f1378b4"), // 331008 own bank to cash in
        Guid.Parse("ca86ca59-3487-43f0-8cd6-d19bcb3bcc2a"), // 331903 legacy cash to transit out
        Guid.Parse("57f85512-d49f-48ac-89ff-8f47ff66e8c5"), // 331904 legacy cash to transit in
        Guid.Parse("d0079e35-1a00-4f1f-a877-606c0294ab6f"), // 331905 legacy collected cash to bank
        Guid.Parse("03c46d55-1c65-4143-8994-8dab526cf309"), // 331906 legacy transit to account
        Guid.Parse("15e787a3-1388-4c3c-98d3-5b35b9cd6199"), // unnumbered collected cash in transit
        Guid.Parse("98d37762-e58d-4a98-9679-47d74446dcee"), // inactive legacy 331005 bank cash transfer
        Guid.Parse("2c3eb152-422e-448f-b4fe-8a8043dcd372"), // 343001 own account transfer out
        Guid.Parse("5fd70ec4-e2eb-493e-8ec3-f99f7326c2eb"), // 343002 own account transfer in
    }.ToFrozenSet();

    public static bool IsExcluded(Guid cashFlowItemId) => Excluded.Contains(cashFlowItemId);
}
