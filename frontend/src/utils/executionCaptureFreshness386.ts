import { businessDayInput } from './businessTime';
// A dated report remains readable; admission independently requires the daily
// capture. Use the same 07:00 UTC+5 boundary, independent of the browser clock zone.
export function executionCaptureDelayed(capturedAtUtc: string, now = new Date()): boolean {
    const expected = new Date(`${businessDayInput(now)}T07:00:00+05:00`).getTime();
    const threshold = now.getTime() < expected ? expected - 86400000 : expected;
    return Date.parse(capturedAtUtc) < threshold;
}
