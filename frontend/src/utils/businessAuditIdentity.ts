export function sameBusinessAuditIdentifier(left: string | null, right: string): boolean {
    return left !== null
        && left.toLocaleLowerCase('en-US') === right.toLocaleLowerCase('en-US');
}
export function businessAuditReactKey(auditId: string): string {
    return auditId.toLocaleLowerCase('en-US');
}
