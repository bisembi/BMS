import type { ReferenceDataItem } from '../types';
import { budgetCentsText, budgetMatrixAmount, budgetMatrixRows, signedBudgetCents, type BudgetDraftLine } from './budgetInstanceMatrix';
type Input = {
    number: string;
    center: string;
    lines: readonly BudgetDraftLine[];
    items: readonly ReferenceDataItem[];
    months: readonly string[];
};
const ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
const xml = (value: string) => value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/gu, '')
    .replace(/&/gu, '&amp;').replace(/</gu, '&lt;').replace(/>/gu, '&gt;').replace(/"/gu, '&quot;');
const col = (index: number): string => index < 26 ? String.fromCharCode(65 + index) : col(Math.floor(index / 26) - 1) + col(index % 26);
const text = (ref: string, value: string, style = 0) => `<c r="${ref}" s="${style}" t="inlineStr"><is><t xml:space="preserve">${xml(value)}</t></is></c>`;
const num = (ref: string, value: string | number, style: number, formula?: string) => `<c r="${ref}" s="${style}">${formula ? `<f>${xml(formula)}</f>` : ''}<v>${value}</v></c>`;
const row = (index: number, cells: string, height = 24) => `<row r="${index}" ht="${height}" customHeight="1">${cells}</row>`;
const date = (value: string) => {
    if (!/^(19|20|21)\d{2}-(0[1-9]|1[0-2])-01$/u.test(value))
        throw new Error('Invalid export month');
    const serial = (Date.parse(value + 'T00:00:00Z') - Date.UTC(1899, 11, 30)) / 86400000;
    return value < '1900-03-01' ? serial - 1 : serial; // Excel's fictitious 1900-02-29.
};
// Addition has no Excel function-argument ceiling (SUM accepts at most 255 arguments).
const sum = (refs: string[]) => refs.length ? refs.join('+') : '0';
const checkedCents = (value: number) => {
    // Excel stores only 15 significant decimal digits. Never silently lose tiyns.
    if (!Number.isSafeInteger(value) || Math.abs(value) > 999999999999999)
        throw new Error('Excel numeric precision exceeded');
    return budgetCentsText(value);
};
/** Finite, uncompressed OOXML ZIP. No external links/macros; all user text is inline text, never a formula. */
function zip(files: readonly [
    string,
    string
][]): Uint8Array<ArrayBuffer> {
    const encoder = new TextEncoder();
    const chunks: Uint8Array<ArrayBuffer>[] = [], directory: Uint8Array<ArrayBuffer>[] = [];
    let offset = 0;
    for (const [path, source] of files) {
        const name = encoder.encode(path), data = encoder.encode(source);
        let crc = 0xffffffff;
        for (const byte of data) {
            crc ^= byte;
            for (let bit = 0; bit < 8; bit++)
                crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
        }
        crc = (crc ^ 0xffffffff) >>> 0;
        const header = new Uint8Array(30), h = new DataView(header.buffer);
        h.setUint32(0, 0x04034b50, true);
        h.setUint16(4, 20, true);
        h.setUint16(6, 0x800, true);
        h.setUint16(12, 33, true);
        h.setUint32(14, crc, true);
        h.setUint32(18, data.length, true);
        h.setUint32(22, data.length, true);
        h.setUint16(26, name.length, true);
        const central = new Uint8Array(46), c = new DataView(central.buffer);
        c.setUint32(0, 0x02014b50, true);
        c.setUint16(4, 20, true);
        c.setUint16(6, 20, true);
        c.setUint16(8, 0x800, true);
        c.setUint16(14, 33, true);
        c.setUint32(16, crc, true);
        c.setUint32(20, data.length, true);
        c.setUint32(24, data.length, true);
        c.setUint16(28, name.length, true);
        c.setUint32(42, offset, true);
        chunks.push(header, name, data);
        directory.push(central, name);
        offset += header.length + name.length + data.length;
    }
    const size = directory.reduce((total, part) => total + part.length, 0);
    if (offset + size > 64 * 1024 * 1024)
        throw new Error('Export too large');
    const end = new Uint8Array(22), e = new DataView(end.buffer);
    e.setUint32(0, 0x06054b50, true);
    e.setUint16(8, files.length, true);
    e.setUint16(10, files.length, true);
    e.setUint32(12, size, true);
    e.setUint32(16, offset, true);
    const result = new Uint8Array(offset + size + end.length);
    let position = 0;
    for (const part of [...chunks, ...directory, end]) {
        result.set(part, position);
        position += part.length;
    }
    return result;
}
export function buildBudgetWorkbook(input: Input): Uint8Array<ArrayBuffer> {
    const { lines, items, months } = input;
    if (!lines.length || lines.length > 20000 || !months.length || months.length > 120)
        throw new Error('Invalid export bounds');
    const cents = lines.map(line => {
        const value = signedBudgetCents(line.limitAmountKzt);
        if (!line.cashFlowItemId || value === null || value < 0 || !['Receipt', 'Payment'].includes(line.flowDirection)
            || !months.includes(line.budgetMonth))
            throw new Error('Invalid export line');
        checkedCents(value);
        date(line.budgetMonth);
        return value;
    });
    const signed = cents.map((value, index) => lines[index].flowDirection === 'Receipt' ? value : -value);
    const gross = cents.reduce((a, b) => a + b, 0), net = signed.reduce((a, b) => a + b, 0);
    checkedCents(gross);
    checkedCents(net);
    const title = row(2, text('A2', `Бюджет ${input.number}`, 5)) + row(3, text('A3', `ЦФО: ${input.center}`));
    const headings = ['№', 'Операция', 'Код статьи ДДС', 'Статья ДДС', 'Направление', 'Месяц', 'Сумма, ₸', 'Чистый поток, ₸', 'Идентификатор статьи'];
    let detail = title + row(5, headings.map((value, i) => text(col(i) + '5', value, 1)).join(''), 32);
    const byId = new Map(items.map(item => [item.id.toLowerCase(), item]));
    lines.forEach((line, i) => {
        const r = i + 6, article = byId.get(line.cashFlowItemId.toLowerCase());
        detail += row(r, num(`A${r}`, i + 1, 0) + text(`B${r}`, line.comment)
            + text(`C${r}`, article?.code ?? '') + text(`D${r}`, article?.name ?? 'Статья отсутствует в справочнике')
            + text(`E${r}`, line.flowDirection === 'Receipt' ? 'Поступление' : 'Выплата') + num(`F${r}`, date(line.budgetMonth), 4)
            + num(`G${r}`, checkedCents(cents[i]), 3) + num(`H${r}`, checkedCents(signed[i]), 3, `IF(E${r}="Поступление",G${r},-G${r})`)
            + text(`I${r}`, line.cashFlowItemId.toLowerCase()), line.comment.includes('\n') ? 40 : 24);
    });
    const end = lines.length + 5;
    detail += row(end + 1, text(`D${end + 1}`, 'Итого', 5) + num(`G${end + 1}`, checkedCents(gross), 3, `SUM(G6:G${end})`)
        + num(`H${end + 1}`, checkedCents(net), 3, `SUM(H6:H${end})`));
    const matrixRows = budgetMatrixRows(items, lines);
    const articles = new Map(matrixRows.flatMap((value, i) => value.kind === 'article' ? [[value.articleIds[0].toLowerCase(), i + 6] as const] : []));
    let matrix = title + row(5, text('A5', 'Статья ДДС', 1) + months.map((month, i) => num(col(i + 1) + '5', date(month), 6)).join('')
        + text(col(months.length + 1) + '5', 'Итого, ₸', 1), 32);
    matrixRows.forEach((value, index) => {
        const r = index + 6;
        let cells = text(`A${r}`, value.label, value.kind === 'article' ? 0 : 5);
        months.forEach((month, i) => {
            const aggregate = budgetMatrixAmount(lines, value.articleIds, [month]);
            if (aggregate.invalid)
                throw new Error('Invalid aggregate');
            if (aggregate.cents === null)
                return;
            const c = col(i + 1);
            const formula = value.kind === 'article'
                ? `SUMIFS('Строчный вид'!$H$6:$H$${end},'Строчный вид'!$I$6:$I$${end},"${value.articleIds[0].toLowerCase().replace(/"/gu, '""')}",'Строчный вид'!$F$6:$F$${end},${c}$5)`
                : sum(value.articleIds.map(id => c + articles.get(id.toLowerCase())));
            if (formula.length > 8192)
                throw new Error('Excel formula too long');
            cells += num(c + r, checkedCents(aggregate.cents), 2, formula);
        });
        const total = budgetMatrixAmount(lines, value.articleIds, months);
        if (total.invalid)
            throw new Error('Invalid row total');
        if (total.cents !== null)
            cells += num(col(months.length + 1) + r, checkedCents(total.cents), 2, `SUM(B${r}:${col(months.length)}${r})`);
        matrix += row(r, cells);
    });
    const final = matrixRows.length + 6;
    matrix += row(final, text(`A${final}`, 'Чистый денежный поток', 5) + months.map((month, i) => {
        const refs = [...articles.values()].map(r => col(i + 1) + r), formula = sum(refs);
        if (formula.length > 8192)
            throw new Error('Excel formula too long');
        const value = budgetMatrixAmount(lines, [...articles.keys()], [month]);
        return num(col(i + 1) + final, checkedCents(value.cents ?? 0), 2, formula);
    }).join('') + num(col(months.length + 1) + final, checkedCents(net), 2, `SUM(B${final}:${col(months.length)}${final})`));
    const sheet = (body: string, columns: string, last: string, lastColumn: string, filter = '') => `<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="${ns}"><dimension ref="A1:${last}"/><sheetViews><sheetView workbookViewId="0" showGridLines="0"><pane ySplit="5" topLeftCell="A6" state="frozen"/></sheetView></sheetViews><cols>${columns}</cols><sheetData>${body}</sheetData>${filter}<mergeCells count="2"><mergeCell ref="A2:${lastColumn}2"/><mergeCell ref="A3:${lastColumn}3"/></mergeCells><pageSetup orientation="landscape" paperSize="9"/></worksheet>`;
    return zip([
        ['[Content_Types].xml', `<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>${[1, 2].map(n => `<Override PartName="/xl/worksheets/sheet${n}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`).join('')}</Types>`],
        ['_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'],
        ['xl/workbook.xml', `<workbook xmlns="${ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Табличный вид" sheetId="1" r:id="rId1"/><sheet name="Строчный вид" sheetId="2" r:id="rId2"/></sheets><calcPr calcId="191029" fullCalcOnLoad="1"/></workbook>`],
        ['xl/_rels/workbook.xml.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'],
        ['xl/styles.xml', `<styleSheet xmlns="${ns}"><numFmts count="2"><numFmt numFmtId="164" formatCode="#,##0;[Red]-#,##0;0"/><numFmt numFmtId="165" formatCode="#,##0.00;[Red]-#,##0.00;0.00"/></numFmts><fonts count="3"><font><sz val="10"/><name val="Arial"/></font><font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Arial"/></font><font><b/><sz val="10"/><name val="Arial"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF31597F"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="7"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="1" fillId="2" borderId="0" xfId="0" applyAlignment="1" applyNumberFormat="1"><alignment horizontal="center" vertical="center"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>`],
        ['xl/worksheets/sheet1.xml', sheet(matrix, `<col min="1" max="1" width="60" customWidth="1"/><col min="2" max="${months.length + 2}" width="19" customWidth="1"/>`, col(months.length + 1) + final, col(months.length + 1))],
        ['xl/worksheets/sheet2.xml', sheet(detail, '<col min="1" max="1" width="6" customWidth="1"/><col min="2" max="2" width="55" customWidth="1"/><col min="3" max="3" width="18" customWidth="1"/><col min="4" max="4" width="60" customWidth="1"/><col min="5" max="5" width="18" customWidth="1"/><col min="6" max="6" width="14" customWidth="1"/><col min="7" max="8" width="22" customWidth="1"/><col min="9" max="9" width="38" hidden="1" customWidth="1"/>', `I${end + 1}`, 'H', `<autoFilter ref="A5:H${end}"/>`)],
    ]);
}
export function downloadBudgetWorkbook(input: Input): void {
    const bytes = buildBudgetWorkbook(input);
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `Бюджет ${input.number.replace(/[\\/:*?"<>|\u0000-\u001f]/gu, '_')}.xlsx`;
    document.body.append(anchor);
    try {
        anchor.click();
    }
    finally {
        anchor.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
}
