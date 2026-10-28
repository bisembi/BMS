// In-memory ownership only: a different live form must not adopt this form's pending save.
export const activeExpenseRequestCreateContexts = new Set<string>();
