namespace Bms.Application.ExpenseRequests;
// A compatible recovery must keep the new analytic reader and storage mapping,
// even when editing is switched off. Never roll SQL back over staff actions.
public sealed record ApprovalAccrualEditPolicy(bool Enabled = true);
