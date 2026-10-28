/// Development transport flags are never enabled by a production build.
export const isDevelopment = import.meta.env.DEV;
// Publication sources contain no demo catalogue and never fall back to mock data.
export const useMockApi = false;
export const DEFAULT_DEV_ACTOR_ID = 'dev-user';
const MAXIMUM_DEV_ACTOR_ID_LENGTH = 256;
function hasControlCharacter(value: string): boolean {
    for (const character of value) {
        const code = character.codePointAt(0) ?? 0;
        if (code < 0x20 || (code >= 0x7f && code <= 0x9f))
            return true;
    }
    return false;
}
/** Mirrors ActorIdentity.IsCanonical without sending an invalid header. */
export function resolveDevActorId(configuredValue: string | undefined): string {
    if (configuredValue === undefined)
        return DEFAULT_DEV_ACTOR_ID;
    const isCanonical = configuredValue.length > 0
        && configuredValue.length <= MAXIMUM_DEV_ACTOR_ID_LENGTH
        && configuredValue === configuredValue.trim()
        && !hasControlCharacter(configuredValue);
    return isCanonical ? configuredValue : DEFAULT_DEV_ACTOR_ID;
}
// Keep the development gate directly visible to Vite so a production build
// can erase both the configured identity and its fallback.
export const devActorId = isDevelopment
    ? resolveDevActorId(import.meta.env.VITE_DEV_ACTOR_ID)
    : '';
