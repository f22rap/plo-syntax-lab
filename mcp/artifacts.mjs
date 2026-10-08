const escape=s=>String(s).replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g,'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
const short=s=>{const chars=[...String(s)];return chars.length>20?chars.slice(0,20).join('')+'…':String(s);};
const md=s=>String(s).replace(/[\r\n]+/g,' ').replace(/\|/g,'\\|').replace(/</g,'&lt;').replace(/>/g,'&gt;');
export const metricNote='比率は、選択したCSVの当該条件のweight合計を分母とした割合です。戦略全体の行動頻度ではありません。条件間に重複があり得るため、条件別比率を合算しません。';
export function chart(result){
 const rows=result.rows,colors=['#087f8c','#d18b22','#8064a2'],width=1100,height=120+rows.length*64;
 const parts=[`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img" aria-label="CSV weight比率"><rect width="100%" height="100%" fill="#fff"/><g font-family="sans-serif" font-size="14" fill="#182c3b"><text x="20" y="28">${escape(result.board)} · CSV間のweight比率</text>`];
 rows[0]?.sources.forEach((s,i)=>parts.push(`<rect x="${20+i*350}" y="44" width="14" height="14" fill="${colors[i]}"/><text x="${40+i*350}" y="57">${escape(short(s.label))}<title>${escape(s.label)}</title></text>`));
 rows.forEach((r,i)=>{
  const y=90+i*64;parts.push(`<text x="20" y="${y+18}">${escape(short(r.label))}<title>${escape(r.label)}</title></text>`);
  let x=360;if(r.status==='ZeroTotal')parts.push(`<text x="360" y="${y+18}">— 合計weightが0のため未定義</text>`);
  else r.sources.forEach((s,j)=>{const percent=Number(s.percent),w=700*percent/100;parts.push(`<rect x="${x}" y="${y}" width="${w}" height="28" fill="${colors[j]}"><title>${escape(s.label)}: ${escape(s.percent)}% / weight ${escape(s.weight_sum)}</title></rect>`);if(w>42)parts.push(`<text x="${x+w/2}" y="${y+19}" text-anchor="middle" fill="white">${percent.toFixed(1)}%</text>`);x+=w;});
 });parts.push('</g></svg>');return parts.join('');
}
export function report(result,narrative=''){
 const lines=[`# PLO比較レポート：${md(result.board)}`,'',`結果ID：${md(result.id)}`,`実行日時：${md(result.saved_utc)}`,'',metricNote,'','## 集計結果','','| 条件 | CSVラベル | 該当ハンド | weight合計 | 比率 |','| --- | --- | ---: | ---: | ---: |'];
 for(const r of result.rows)for(const s of r.sources)lines.push(`| ${md(r.label)} | ${md(s.label)} | ${s.matched_hands} | ${s.weight_sum} | ${s.percent===null?'—':s.percent+'%'} |`);
 lines.push('','## 使用したsyntax','');
 for(const r of result.rows)lines.push(`- ${md(r.label)}：\`${r.syntax||'全ハンド（syntaxなし）'}\``);
 if(narrative)lines.push('','## AIによる考察','',narrative,'','この考察は呼び出し元AIが作成した文章です。数値の根拠は上の集計結果です。');
 lines.push('','## 再現情報','','```json',JSON.stringify(result.provenance||{note:'旧履歴のためMCPの再現情報はありません。'},null,2),'```','','MonkerSolver実機での構文受理・抽出一致は未確認です。');
 return lines.join('\n');
}
