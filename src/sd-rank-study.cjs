'use strict';
// One policy for condition generation, archived-result selection and reporting.
const P=require('./lab/engine.js');
const R=require('./research-recipes.cjs');
const G=require('./research-generation.cjs');
const POLICY='sd-rank-pp-inclusive-v1';
const ranks='23456789TJQKA';
function condition(rank,mode){
 const filter=R.defaultFilter();
 filter.role={type:'pair',rank:{type:'board',position:'top'}};
 filter.sd='none'; // own SD excluded; pocket, BDFD and BDSD remain unrestricted
 filter.blockers=[{rank:{type:'fixed',rank},mode}];
 return {condition_id:`rank_${rank}_all_${mode}`,label:`トップペア SDなし ${rank}${mode==='yes'?'あり':'なし'}（pp含む）`,enabled:true,kind:'generated',syntax:'',by_board:{},generator:{recipe_version:1,ruleset:'plo4-flop-v1',source:'filter',filter,additional_syntax:'*'}};
}
function createSet({set_id='sd_rank_pp_inclusive',name='ランク別SDブロッカー（pp含む）'}={}){
 return R.normalizeSet({set_id,name,revision:0,conditions:[...ranks].flatMap(r=>['yes','no'].map(m=>condition(r,m)))});
}
function sameSet(e,a,b){
 const x=e.expressionSet(a),y=e.expressionSet(b);
 for(let i=0;i<e.hands.length;i++)if(!!(x[i>>>5]&(1<<(i&31)))!==!!(y[i>>>5]&(1<<(i&31))))return false;
 return true;
}
function frequency(row){
 const a=row.Actions?.filter(a=>a.Key==='total_bet');
 if(a?.length!==1||typeof a[0].Frequency!=='number'||!Number.isFinite(a[0].Frequency)||a[0].Frequency<0||a[0].Frequency>100)throw Error('Invalid total_bet frequency');
 if(!Number.isSafeInteger(row.SelectedHands)||row.SelectedHands<1)throw Error('Invalid selected hand count');
 return a[0].Frequency;
}
async function summarize(rows,{progress=()=>{},signal}={}){
 if(!Array.isArray(rows)||!rows.length)throw Error('No result rows');
 const groups=new Map();
 for(const row of rows){const b=R.canonicalBoard(row.Board);if(!groups.has(b))groups.set(b,[]);groups.get(b).push(row);}
 const boards=[];
 for(const [board,items] of groups){
  if(signal?.aborted)throw Error('Cancelled');
  const spots=new Set(items.map(r=>r.SpotId).filter(Boolean));if(spots.size>1)throw Error('Multiple spots on one board; select a single node');
  const e=new P.Engine();await e.prepare(board);e.classifyPairDraws();
  const expected=new Map((await G.generatePrepared(e,createSet())).rows.map(r=>[r.condition_id,r]));
  const target=new Map();
  for(const p of e.blockerTargets(false)){
   const pair=p.p.map(c=>ranks[P.rank(c)-2]).sort((a,b)=>ranks.indexOf(b)-ranks.indexOf(a)).join('');
   const type=p.drawFlags&(P.F.nutOpen|P.F.nonOpen)?'oesd':p.drawFlags&(P.F.nutGut|P.F.nonGut)?'gut':'other';
   for(const rank of pair){if(!target.has(rank))target.set(rank,{oesd:new Set(),gut:new Set(),other:new Set()});target.get(rank)[type].add(pair);}
  }
  const entries=[],omitted=[];
  for(const [rank,targets] of target){
   const pair=[];
   for(const mode of ['yes','no']){
    const id=`rank_${rank}_all_${mode}`,matches=items.filter(r=>r.ConditionId===id);
    if(matches.length!==1)throw Error(`Missing or duplicate pp-inclusive row: ${board} ${id}. Regenerate with ${POLICY}; no nopp fallback.`);
    const row=matches[0],want=expected.get(id);
    if(!['completed','empty_condition','no_matching_hands'].includes(row.Status))throw Error(`Incomplete result: ${board} ${id} ${row.Status}`);
    if(want.status==='empty_condition'){
     if(row.Status==='completed')throw Error('Completed row for an empty definition');
    }else if(typeof row.Syntax!=='string'||!row.Syntax||!sameSet(e,row.Syntax,want.syntax))throw Error(`Definition mismatch: ${board} ${id}; pp exclusion or other filtering detected. Recompute.`);
    pair.push(row);
   }
   if(pair.some(r=>r.Status!=='completed')){omitted.push({rank,statuses:pair.map(r=>r.Status)});continue;}
   const yes=frequency(pair[0]),no=frequency(pair[1]);
   entries.push({rank,with_rank:yes,without_rank:no,delta:yes-no,with_hands:pair[0].SelectedHands,without_hands:pair[1].SelectedHands,targets:Object.fromEntries(Object.entries(targets).map(([k,v])=>[k,[...v].sort()]))});
  }
  entries.sort((a,b)=>b.delta-a.delta||ranks.indexOf(a.rank)-ranks.indexOf(b.rank));
  boards.push({board,spot_id:[...spots][0]??null,ranks:entries,omitted,summary:entries.length?`Δ最大 ${entries[0].rank}: ${entries[0].delta.toFixed(1)}ポイント（保有時 ${entries[0].with_rank.toFixed(1)}%）`:'比較可能なランクなし'});
  progress({done:boards.length,total:groups.size,board});
 }
 return {policy:POLICY,pocket_pairs:'included',own_sd:'excluded',bdfd:'unrestricted',bdsd:'unrestricted',boards,notes:['Δはランク保有あり－なし。pp・キッカー等の構成は固定しておらず、純粋なブロッカー効果ではない。','相手SD候補はボードから列挙。相手CSVの保有頻度による重み付けはしない。','非成立・範囲内該当なしを0%にしない。']};
}
function renderHtml(report){
 if(report.policy!==POLICY)throw Error('Report policy mismatch');
 const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const rows=report.boards.flatMap(b=>b.ranks.map(r=>`<tr><td>${esc(b.board)}</td><td>${r.rank}</td><td>${r.with_rank.toFixed(1)}</td><td>${r.without_rank.toFixed(1)}</td><td>${r.delta.toFixed(1)}</td><td>${r.with_hands} / ${r.without_hands}</td><td>${esc(r.targets.oesd.join('/'))}</td><td>${esc(r.targets.gut.join('/'))}</td><td>${esc(r.targets.other.join('/'))}</td></tr>`)).join('');
 return `<!doctype html><html lang="ja"><meta charset="utf-8"><title>SDランク比較（pp含む）</title><style>body{font-family:sans-serif;margin:32px;color:#142d40}table{border-collapse:collapse}td,th{padding:7px;border:1px solid #ccd}th{background:#e8f3f3}@media print{thead{display:table-header-group}}</style><h1>SDランク比較（pp含む）</h1><p>${POLICY} / 自分の通常SDなし</p><p>${report.notes.map(esc).join('<br>')}</p><table><thead><tr><th>ボード</th><th>ランク</th><th>あり%</th><th>なし%</th><th>Δpt</th><th>ハンド数 あり/なし</th><th>OESD組</th><th>ガット組</th><th>その他SD組</th></tr></thead><tbody>${rows}</tbody></table><p>省略：${esc(JSON.stringify(report.boards.filter(b=>b.omitted.length).map(b=>({board:b.board,omitted:b.omitted}))))}</p></html>`;
}
module.exports={POLICY,createSet,summarize,renderHtml};
