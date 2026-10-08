'use strict';
const P = require('../lab/engine.js');
const SCALE = 10n ** 28n;
function decimal(text) {
 const m = String(text).trim().match(/^\+?(\d+)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/);
 if (!m) throw Error('weightは0〜1の数値が必要です。');
 const exponent = Number(m[3] || 0) - (m[2] || '').length + 28;
 if (Math.abs(exponent) > 1000) throw Error('weightの精度・指数が範囲外です。');
 let n = BigInt(m[1] + (m[2] || ''));
 if (exponent >= 0) n *= 10n ** BigInt(exponent);
 else { const d = 10n ** BigInt(-exponent); if (n % d) throw Error('weightは小数28桁までです。'); n /= d; }
 if (n > SCALE) throw Error('weightは0〜1の数値が必要です。');
 return n;
}
function format(n) { const s = n.toString().padStart(29, '0'); return (s.slice(0, -28) + '.' + s.slice(-28)).replace(/\.?0+$/, '') || '0'; }
function csv(text) {
 const rows = []; let row = [], value = '', quoted = false, closed = false;
 text = text.replace(/^\uFEFF/, '');
 for (let i = 0; i < text.length; i++) {
  const c = text[i];
  if (quoted) { if (c === '"') { if (text[i + 1] === '"') { value += '"'; i++; } else { quoted = false; closed = true; } } else value += c; }
  else if (c === '"') { if (value || closed) throw Error('CSVの引用符が不正です。'); quoted = true; }
  else if (c === ',' || c === '\n' || c === '\r') { row.push(value); value = ''; closed = false; if (c !== ',') { if (c === '\r' && text[i + 1] === '\n') i++; if (row.some(v => v !== '')) rows.push(row); row = []; } }
  else { if (closed) throw Error('CSVの引用符の後に不正な文字があります。'); value += c; }
 }
 if (quoted) throw Error('CSVの引用符が閉じていません。');
 if (value || row.length || closed) { row.push(value); rows.push(row); }
 return rows;
}
function validateSelection(data, board) {
 if (!data || data.game !== 'PLO4' || !Array.isArray(data.board) || data.board.join('') !== board || !Array.isArray(data.ranges) || data.ranges.length > 500) throw Error('条件JSONの形式またはボードが一致しません。');
 return data.ranges.map((r, i) => {
  if (typeof r.label !== 'string' || typeof r.syntax !== 'string' || !r.syntax.trim() || r.syntax.length > 50000 || !Number.isInteger(r.count) || r.count <= 0) throw Error('空集合・不正な条件は追加できません。');
  P.compile(r.syntax); return { cell: r.cell || `CUSTOM${i + 1}`, label: r.label, syntax: r.syntax, count: r.count };
 });
}
function parseDataset(boardCards, file, index = 0) {
  if (typeof file.text !== 'string' || typeof file.name !== 'string') throw Error('CSVが不正です。');
  const named = file.name.match(/(?:^|_)((?:[AKQJT2-9][shdc]){3,5})(?=_|\.|$)/i);
  if (named) { const b = P.parseBoard(named[1]); if (b.length !== boardCards.length || b.slice(0, 3).sort().join() !== boardCards.slice(0, 3).sort().join() || b.slice(3).join() !== boardCards.slice(3).join()) throw Error(`${file.name}: ボードが一致しません。`); }
  const rows = csv(file.text), headers = rows.shift()?.map(s => s.toLowerCase());
  if (!headers || new Set(headers).size !== headers.length) throw Error('CSVヘッダーが不正です。');
  const handIndex = headers.includes('hand') ? headers.indexOf('hand') : headers.indexOf('combo'), weightIndex = headers.indexOf('weight');
  if (handIndex < 0 || weightIndex < 0 || !rows.length) throw Error(`${file.name}: hand/comboとweight列、およびデータ行が必要です。`);
  const seen = new Set();
  return { name: file.name, label: String(file.label || `CSV${index + 1}`).trim(), rows: rows.map((row, i) => {
   try {
    if (row.length !== headers.length || !/^(?:[AKQJT2-9][shdc]){4}$/.test(row[handIndex])) throw Error('不正なPLO4ハンドまたは列数です。');
    const cards = row[handIndex].match(/../g).map(c => '23456789TJQKA'.indexOf(c[0]) * 4 + 'shdc'.indexOf(c[1]));
    if (new Set(cards).size !== 4 || cards.some(c => boardCards.includes(c))) throw Error('カードの重複またはボードとの衝突です。');
    const key = cards.slice().sort((a,b) => a-b).join(','); if (seen.has(key)) throw Error('重複ハンドです。'); seen.add(key);
    return { cards, weight: decimal(row[weightIndex]) };
   } catch(e) { throw Error(`${file.name}, 行${i + 2}: ${e.message}`); }
  }) };

}
function compare(input, options = {}) {
 const boardCards = P.parseBoard(input.board), board = boardCards.map(P.card).join('');
 if (!Array.isArray(input.files) || input.files.length < 2 || input.files.length > 3) throw Error('CSVを2〜3ファイル選択してください。');
 if (new Set(input.files.map(f => f.name)).size !== input.files.length) throw Error('同名のCSVを重複選択できません。');
 const datasets = options.datasets || input.files.map((file,index)=>parseDataset(boardCards,file,index));
 if (!Array.isArray(input.ranges) || input.ranges.length > 500) throw Error('条件が不正です。');
 const filters = input.ranges.map(r => { if (typeof r.label !== 'string' || typeof r.syntax !== 'string' || !r.syntax.trim() || r.syntax.length > 50000) throw Error('空のsyntaxは集計できません。'); return {...r, match: P.compile(r.syntax)}; });
 if (input.all) filters.unshift({cell:'ALL',label:'全ハンド（syntaxなし）',syntax:'',match:()=>true});
 if (!filters.length) throw Error('集計条件を選択してください。');
 return filters.map(f => {
  const stats = datasets.map(d => { let sum = 0n, count = 0; for (const r of d.rows) if (f.match(r.cards)) { sum += r.weight; count++; } return {sum,count}; });
  const total = stats.reduce((n,s)=>n+s.sum,0n), row = {board,cell:f.cell || '',label:f.label,syntax:f.syntax,csv_count:datasets.length,total_weight:format(total)};
  datasets.forEach((d,i)=>{const k=`csv${i+1}`; Object.assign(row,{[k+'_label']:d.label,[k+'_path']:d.name,[k+'_source_rows']:d.rows.length,[k+'_matched_hands']:stats[i].count,[k+'_weight_sum']:format(stats[i].sum),[k+'_percent']:total ? (options.percentAsString ? percent(stats[i].sum,total) : Number(stats[i].sum * 10000000000n / total) / 100000000) : null});});
  return {...row,status:total?'OK':'ZeroTotal',note:total?'':'Total weight is zero; percentages are undefined.'};
 });
}
function percent(sum,total) { const n=sum*10000000000n/total; return `${n/100000000n}.${String(n%100000000n).padStart(8,'0')}`; }
function resultCsv(rows) { const keys=Object.keys(rows[0]); const quote=v=>'"'+String(v ?? '').replaceAll('"','""')+'"'; return '\uFEFF'+[keys.map(quote).join(','),...rows.map(r=>keys.map(k=>quote(r[k])).join(','))].join('\r\n')+'\r\n'; }
module.exports = {compare, csv, decimal, format, parseDataset, percent, validateSelection, resultCsv};
