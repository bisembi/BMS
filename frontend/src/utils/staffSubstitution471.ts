export function staffPeriod471(from: string, through: string | null | undefined) {
    const short = (s: string) => s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(2, 4);
    return `с ${short(from)}` + (through ? ` по ${short(through)}` : ' на неопределённый срок');
}
