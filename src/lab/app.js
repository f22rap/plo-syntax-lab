'use strict';
const $=id=>document.getElementById(id), nf=new Intl.NumberFormat('ja-JP');
const symbols={s:'♠',h:'♥',d:'♦',c:'♣'}, suitClass={s:'spades',h:'hearts',d:'diamonds',c:'clubs'};
let worker,sequence=0,pending=new Map(),summary,role='all',lastResult,rows=[],refreshVersion=0,analyzeVersion=0,timer,pickerSlot=0,pendingCards=['As','Kd','7s'];
const labels={fd:'全てのFD',fdNut:'ナッツFD',fdSecond:'セカンドナッツFD',fdThird:'サードナッツFD',fdLow:'それ以下のFD',fdTriple:'同スート3枚以上のFD',dualFd:'2スートのFD',sd:'全てのSD',wrap:'ラップ（9アウト以上）',nutGut:'ナッツガットショット',nutOpen:'ナッツオープンエンド',nonGut:'アンナッツガットショット',nonOpen:'アンナッツオープンエンド',bdfd:'全てのBDFD',bdNut:'ナッツBDFD',bdSecond:'セカンドナッツBDFD',bdLow:'それ以下のBDFD',bdTriple:'同スート3枚以上のBDFD',bdsd:'全てのBDSD',bdsd9:'BDSD 9+',bdsd8:'BDSD 8',bdsd4:'BDSD 4',bdsdOther:'その他のBDSD'};
function toast(s){$('toast').textContent=s;$('toast').hidden=false;clearTimeout(toast.timer);toast.timer=setTimeout(()=>$('toast').hidden=true,3500);}
function showError(e){$('error').textContent=e.message||String(e);$('error').hidden=false;}
function initWorker(){
 if(worker)worker.terminate();for(const p of pending.values())p.reject(Error('計算を更新しました'));pending.clear();
 const inline=$('engine-inline').textContent.trim();
 if(inline){const source=inline+'\nlet engine;onmessage=async({data})=>{const{id,type}=data;try{let result;if(type==="prepare"){engine=new PLO.Engine();result=await engine.prepare(PLO.parseBoard(data.board),p=>postMessage({type:"progress",...p}));}else if(!engine?.ready)throw Error("先にボードを解析してください");else if(type==="query")result=engine.query(data.filter);else if(type==="syntax")result=engine.syntax(data.filter);else if(type==="bdsdDetails")result=engine.bdsdDetails(data.hand);else if(type==="export")result=data.rows.map(item=>{const r=engine.syntax(item.filter);return{label:item.label,filter:item.filter,count:r.count,syntax:r.syntax}});postMessage({id,result})}catch(e){postMessage({id,error:e.message})}};';const url=URL.createObjectURL(new Blob([source],{type:'text/javascript'}));worker=new Worker(url);URL.revokeObjectURL(url);}
 else worker=new Worker('worker.js');
 worker.onmessage=({data})=>{if(data.type==='progress'){$('progress-bar').value=data.percent;$('progress-pct').textContent=data.percent+'%';$('progress-text').textContent=data.message;return;}const p=pending.get(data.id);if(!p)return;pending.delete(data.id);data.error?p.reject(Error(data.error)):p.resolve(data.result);};
 worker.onerror=e=>{for(const p of pending.values())p.reject(Error(e.message||'計算を開始できませんでした。'));pending.clear();};
}
function request(type,data={}){return new Promise((resolve,reject)=>{const id=++sequence;pending.set(id,{resolve,reject});worker.postMessage({id,type,...data});});}
function renderCards(){const wrap=$('board-cards');wrap.replaceChildren();for(let i=0;i<5;i++){const slot=document.createElement('div');slot.className='card-slot';const label=document.createElement('span');label.className='slot-label';label.textContent=i===0?'FLOP':i===3?'TURN':i===4?'RIVER':'\u00a0';const button=document.createElement('button'),c=pendingCards[i];button.className='playing-card '+(c?suitClass[c[1]]:'empty');button.setAttribute('aria-label',(i<3?'フロップ'+(i+1):i===3?'ターン':'リバー')+'：'+(c||'未選択'));if(c)button.innerHTML=`<b>${c[0]}</b><span>${symbols[c[1]]}</span>`;else button.textContent='+';button.onclick=()=>openPicker(i);slot.append(label,button);wrap.append(slot);}}
function openPicker(i){pickerSlot=Math.min(i,pendingCards.length);const wrap=$('deck-picker');wrap.replaceChildren();for(const s of 'shdc'){const row=document.createElement('div');row.className='deck-row';for(const r of 'AKQJT98765432'){const c=r+s,b=document.createElement('button');b.className=suitClass[s];b.textContent=r+symbols[s];b.setAttribute('aria-label',c);b.disabled=pendingCards.includes(c)&&pendingCards[pickerSlot]!==c;b.onclick=()=>{pendingCards[pickerSlot]=c;$('board-input').value=pendingCards.join(' ');renderCards();$('card-dialog').close();};row.append(b);}wrap.append(row);}$('remove-card').disabled=pickerSlot>=pendingCards.length;$('card-dialog').showModal();}
function roleLabel(key=role){if(key==='all')return '全てのハンド';if(key.startsWith('cat:'))return PLO.CAT[Number(key.slice(4))]+'・全て';return summary.roles.find(r=>r.key===key)?.label||key;}
function selectionLabel(key=role){const pocket=$('pocket').value;return roleLabel(key)+(pocket==='all'?'':' ＋ '+('23456789TJQKA'[Number(pocket)-2]).repeat(2));}
function renderRoles(){const wrap=$('roles');wrap.replaceChildren();function button(key,label,count,parent=wrap,sub=false){const b=document.createElement('button');b.className='role-button'+(key===role?' active':'')+(sub?' role-sub':'');b.dataset.role=key;b.setAttribute('aria-pressed',String(key===role));const title=document.createElement('div');title.textContent=label;const n=document.createElement('span');n.textContent=nf.format(count);b.append(title,n);b.onclick=()=>{role=key;renderRoles();scheduleRefresh();};parent.append(b);}
 button('all','全てのハンド',summary.total);for(let cat=8;cat>=0;cat--){const list=summary.roles.filter(r=>r.cat===cat),count=list.reduce((a,b)=>a+b.count,0);const group=document.createElement('div');group.className='role-group';button('cat:'+cat,PLO.CAT[cat],count,group);for(const r of list)button(r.key,r.label,r.count,group,true);wrap.append(group);}}
function renderBlockers(){const wrap=$('blockers');wrap.replaceChildren();for(const r of summary.ranks){const label=document.createElement('label');label.className='blocker';const b=document.createElement('b');b.textContent=r.label;const select=document.createElement('select');select.dataset.rank=r.value;select.setAttribute('aria-label',r.label+'のブロッカー');for(const [v,t] of [['all','指定なし'],['yes','あり'],['no','なし'],['one','ちょうど1枚'],['two','2枚以上']])select.add(new Option(t,v));select.onchange=scheduleRefresh;label.append(b,select);wrap.append(label);}}
function filter(){return {role,pocket:$('pocket').value,fd:$('fd').value,sd:$('sd').value,bdfd:$('bdfd').value,bdsd:$('bdsd').value,clean:document.querySelector('input[name=clean]:checked').value,blockers:[...document.querySelectorAll('#blockers select')].filter(s=>s.value!=='all').map(s=>({rank:Number(s.dataset.rank),mode:s.value}))};}
function resetFilters(){$('pocket').value='all';for(const k of ['fd','sd','bdfd','bdsd'])$(k).value='all';document.querySelector('input[name=clean][value=""]').checked=true;document.querySelectorAll('#blockers select').forEach(s=>s.value='all');}
function syncAvailability(){const street=summary.street,clean=document.querySelector('input[name=clean]:checked').value;for(const k of ['fd','sd'])$(k).disabled=street===5||!!clean;for(const k of ['bdfd','bdsd'])$(k).disabled=street!==3||clean==='all';for(const r of document.querySelectorAll('input[name=clean]'))r.disabled=street===5;$('analyze-bdsd').disabled=street!==3;$('bdsd-hand').disabled=street!==3;const note=$('street-note');note.hidden=street===3;note.textContent=street===4?'ターン：バックドアは対象外。現在の通常ドローを判定します。':'リバー：ドローは対象外。完成役とブロッカーを判定します。';$('export-all').textContent=street===5?'完成役をまとめて保存':'分類をまとめて保存';}
async function analyze(text){
 refreshVersion++;invalidateTransfer();clearBdsdReport();
 let parsed;try{parsed=PLO.parseBoard(text);}catch(e){showError(e);return false;}
 const generation=++analyzeVersion;
 clearTimeout(timer);refreshVersion++;$('error').hidden=true;$('analyze').disabled=true;$('progress').hidden=false;$('progress-bar').value=0;$('progress-pct').textContent='0%';$('progress-text').textContent='ボードを解析しています';$('workspace').setAttribute('aria-busy','true');pendingCards=parsed.map(PLO.card);$('board-input').value=pendingCards.join(' ');renderCards();initWorker();
 try{const prepared=await request('prepare',{board:pendingCards.join(' ')});if(generation!==analyzeVersion)return false;summary=prepared;role=summary.roles.find(r=>r.count>0)?.key||'all';resetFilters();renderRoles();renderBlockers();syncAvailability();$('street').textContent=({3:'FLOP',4:'TURN',5:'RIVER'})[summary.street];$('legal-count').textContent=nf.format(summary.total)+' 合法ハンド';$('result-board').textContent=summary.board.map(c=>c[0]+symbols[c[1]]).join('  ');$('progress').hidden=true;$('workspace').setAttribute('aria-busy','false');await refresh();return true;}
 catch(e){if(generation===analyzeVersion){showError(e);$('progress').hidden=true;}return false;}finally{if(generation===analyzeVersion)$('analyze').disabled=false;}
}
function scheduleRefresh(){refreshVersion++;invalidateTransfer();if(!summary)return;clearTimeout(timer);timer=setTimeout(refresh,130);}
async function refresh(){invalidateTransfer();const version=++refreshVersion;syncAvailability();const f=filter();$('role-title').textContent=selectionLabel();$('syntax-format').textContent='生成・照合中';$('copy-syntax').disabled=true;$('download-syntax').disabled=true;$('export-all').disabled=true;try{
 const result=await request('syntax',{filter:f});if(version!==refreshVersion)return;lastResult=result;renderResult(result);
 const base={role:f.role,pocket:f.pocket,blockers:f.blockers},breakdown=await request('query',{filter:base});if(version!==refreshVersion)return;renderBreakdowns(base,breakdown);$('export-all').disabled=false;setTransfer(f,result);
 }catch(e){if(version===refreshVersion){showError(e);$('syntax-format').textContent='生成できませんでした';}}
}
function renderResult(r){$('match-count').textContent=nf.format(r.count);$('match-percent').textContent=(r.count/r.total*100).toFixed(2)+'%';$('syntax').value=r.syntax.length>30000?r.syntax.slice(0,30000)+'\n…（表示のみ省略。コピー・保存は全文）':r.syntax;$('syntax').placeholder=r.count?'':'該当するハンドがありません。条件を変更してください。';$('syntax-format').textContent=r.count?'条件構文':'該当なし';$('syntax-meta').textContent=r.count?`${nf.format(r.syntax.length)}文字 · このボード専用${r.syntax.length>5000?' · 長い式は.txt保存を推奨':''}`:'空集合のsyntaxは生成しません';$('copy-syntax').disabled=!r.count;$('download-syntax').disabled=!r.count;$('out-range').textContent=summary.street<5&&r.count?`手札全体 SD ${r.outs[0]}–${r.outs[1]} outs / nut ${r.nutOuts[0]}–${r.nutOuts[1]}`:'';
 const wrap=$('examples');wrap.replaceChildren();for(const example of r.examples){const box=document.createElement('div');box.className='example';const cs=document.createElement('div');cs.className='mini-cards';for(const c of example.cards.slice().reverse()){const el=document.createElement('span');el.className='mini '+suitClass[c[1]];el.textContent=c[0]+symbols[c[1]];cs.append(el);}box.append(cs);if(summary.street<5){const p=document.createElement('p');p.textContent=`手札全体 SD ${example.outs} / nut ${example.nutOuts}`;box.append(p);if(summary.street===3){const b=document.createElement('button');b.className='text-button';b.textContent='BDSD詳細';b.onclick=()=>{$('bdsd-hand').value=example.cards.join(' ');inspectBdsd();$('bdsd-section').scrollIntoView({behavior:'smooth',block:'start'});};box.append(b);}}wrap.append(box);}if(!r.count)wrap.innerHTML='<p class="empty-state">該当ハンドなし</p>';
}
function renderBreakdowns(base,result){rows=[];const wrap=$('breakdowns');wrap.replaceChildren();if(summary.street===5){wrap.innerHTML='<p class="no-draws">リバーではドローを分類しません。左の完成役と上のポケットペア・ブロッカー条件を使えます。</p>';return;}
 const note=document.createElement('p');note.className='muted';note.style.marginBottom='10px';note.textContent='選択した完成役・ポケットペア・ブロッカーを基準にした内訳です。追加ドロー条件を付ける前の件数を表示。SDの2枚組分類・BDSD分類は重複するため合計できません。BDSDはフロップで通常SDがあるハンドを除外。4・8は2枚の組、9+は3枚以上の組です。';wrap.append(note);
 const groups=[['フラッシュドロー','fd',['fd','fdNut','fdSecond','fdThird','fdLow','fdTriple',...(summary.street===4?['dualFd']:[]),'none']],['ストレートドロー','sd',['sd','wrap','nutGut','nutOpen','nonGut','nonOpen','none']]];if(summary.street===3)groups.push(['バックドアフラッシュ','bdfd',['bdfd','bdNut','bdSecond','bdLow','bdTriple','none']]);if(summary.street===3)groups.push(['バックドアストレート','bdsd',['bdsd','bdsd9','bdsd8','bdsd4','bdsdOther','none']]);groups.push(['ドローなし','clean',['regular','all']]);
 for(const [title,key,values] of groups){const group=document.createElement('div');group.className='breakdown-group';const heading=document.createElement('div');heading.className='breakdown-title';heading.textContent=title;group.append(heading);for(const value of values){let count,label;if(key==='clean'){count=result.counts[value==='all'?'allNone':'regularNone'];label=value==='all'?'BDFD・BDSDも含めて全てなし':'通常SD・FDなし';}else if(value==='none'){count=result.count-result.counts[key];label=key.toUpperCase()+'なし';}else{count=result.counts[value];label=labels[value];}const item={label:selectionLabel()+' / '+label,filter:{...base,[key]:value},count,key,value};rows.push(item);const row=document.createElement('div');row.className='breakdown-row'+(!count?' zero':'');const text=document.createElement('span');text.textContent=label;const number=document.createElement('span');number.className='row-count';number.textContent=nf.format(count);const actions=document.createElement('div');actions.className='row-actions';const view=document.createElement('button');view.textContent='表示';view.disabled=!count;view.onclick=()=>{resetDrawFilters();if(key==='clean')document.querySelector(`input[name=clean][value="${value}"]`).checked=true;else $(key).value=value;refresh();$('syntax').scrollIntoView({behavior:'smooth',block:'center'});};const copy=document.createElement('button');copy.textContent='コピー';copy.disabled=!count;copy.onclick=async()=>{copy.disabled=true;copy.textContent='…';try{const r=await request('syntax',{filter:item.filter});await copyText(r.syntax);}catch(e){toast(e.message);}finally{copy.disabled=false;copy.textContent='コピー';}};actions.append(view,copy);row.append(text,number,actions);group.append(row);}wrap.append(group);}
}
function resetDrawFilters(){for(const k of ['fd','sd','bdfd','bdsd'])$(k).value='all';document.querySelector('input[name=clean][value=""]').checked=true;}
async function copyText(text){if(!text){toast('該当する構文がありません');return;}try{await navigator.clipboard.writeText(text);}catch{const t=document.createElement('textarea');t.value=text;t.style.position='fixed';t.style.left='-9999px';document.body.append(t);t.select();const ok=document.execCommand('copy');t.remove();if(!ok)throw Error('コピーできませんでした。.txt保存をご利用ください。');}toast('syntaxをコピーしました');}
function saveFile(name,text,type='text/plain;charset=utf-8'){const url=URL.createObjectURL(new Blob([text],{type})),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
$('board-form').onsubmit=e=>{e.preventDefault();analyze($('board-input').value);};document.querySelectorAll('.preset').forEach(b=>b.onclick=()=>analyze(b.dataset.board));for(const key of ['fd','sd','bdfd','bdsd','pocket'])$(key).onchange=scheduleRefresh;
document.querySelectorAll('input[name=clean]').forEach(r=>r.onchange=()=>{if(r.value){$('fd').value='all';$('sd').value='all';if(r.value==='all'){$('bdfd').value='all';$('bdsd').value='all';}}scheduleRefresh();});
$('reset').onclick=()=>{resetFilters();refresh();};$('help-button').onclick=()=>$('help-dialog').showModal();$('close-help').onclick=()=>$('help-dialog').close();$('close-cards').onclick=()=>$('card-dialog').close();$('remove-card').onclick=()=>{pendingCards=pendingCards.slice(0,pickerSlot);$('board-input').value=pendingCards.join(' ');renderCards();$('card-dialog').close();};
$('copy-syntax').onclick=()=>copyText(lastResult?.syntax).catch(e=>toast(e.message));$('download-syntax').onclick=()=>saveFile('PLO_'+summary.board.join('')+'_'+role.replaceAll(':','-')+'.txt',lastResult.syntax);
$('export-all').onclick=async()=>{const b=$('export-all');b.disabled=true;b.textContent='まとめています…';try{const f=filter();const list=summary.street===5?summary.roles.map(r=>({label:selectionLabel(r.key),filter:{role:r.key,pocket:f.pocket,blockers:f.blockers}})):[{label:selectionLabel()+' / 現在の複合条件',filter:f},...rows];const out=await request('export',{rows:list});saveFile('PLO_'+summary.board.join('')+'_ranges.json',JSON.stringify({app:'PLO Syntax Lab',board:summary.board,game:'PLO4',note:'各syntaxを個別にコピーして使用。Monker実機での受理・抽出結果は未確認。',ranges:out},null,2),'application/json');toast('分類別のsyntaxを保存しました');}catch(e){toast(e.message);}finally{b.disabled=false;b.textContent=summary.street===5?'完成役をまとめて保存':'分類をまとめて保存';}};
// Optional WebMCP surface: same validated actions and visible state as the UI.
if(document.modelContext?.registerTool){const life=new AbortController();const tools=[{name:'analyze_plo_board',description:'ボードを解析し、画面に完成役とMonker用syntaxを表示する。',inputSchema:{type:'object',properties:{board:{type:'string'}},required:['board'],additionalProperties:false},annotations:{readOnlyHint:false},execute:async input=>{PLO.parseBoard(input.board);const ok=await analyze(input.board);if(!ok)throw Error('解析できませんでした');return {board:summary.board,total:summary.total,roles:summary.roles};}},{name:'read_plo_result',description:'現在表示中のボード・条件・件数・構文形式を読む。',inputSchema:{type:'object',properties:{},additionalProperties:false},annotations:{readOnlyHint:true},execute:()=>({board:summary?.board,filter:summary?filter():null,count:lastResult?.count,format:lastResult?.format,syntaxLength:lastResult?.syntax.length})}];for(const t of tools){try{Promise.resolve(document.modelContext.registerTool(t,{signal:life.signal})).catch(()=>{});}catch{}}window.addEventListener('pagehide',()=>life.abort(),{once:true});}
let transferSelection=null,transferBusy=false;
const connected=location.protocol==='http:'&&location.hostname==='127.0.0.1'&&/^\/[a-f0-9]{32}\/(?:lab\/)?$/.test(location.pathname);
if(!connected){$('send-comparison').textContent='比較条件を保存';$('transfer-note').textContent='CSV・Excelなしで生成できます。保存した条件JSONを比較アプリの「条件JSONを読み込む」で追加できます。';}
function invalidateTransfer(){transferSelection=null;$('send-comparison').disabled=true;$('save-selection').disabled=true;}
function describeFilter(f){
 const parts=[selectionLabel()];for(const k of ['fd','sd','bdfd','bdsd'])if(f[k]!=='all')parts.push($(k).selectedOptions[0].textContent);
 if(f.clean)parts.push(f.clean==='all'?'FD・SD・BDFD・BDSDなし':'通常FD・SDなし');
 const modes={yes:'あり',no:'なし',one:'ちょうど1枚',two:'2枚以上'};
 for(const b of f.blockers)parts.push('23456789TJQKA'[b.rank-2]+' '+modes[b.mode]);return parts.join(' / ');
}
function setTransfer(f,result){
 if(!result.count){invalidateTransfer();return;}
 const label=describeFilter(f);$('comparison-label').value=label;
 transferSelection={app:'PLO Syntax Lab',game:'PLO4',board:[...summary.board],ranges:[{label,filter:structuredClone(f),count:result.count,syntax:result.syntax}]};
 $('send-comparison').disabled=transferBusy;$('save-selection').disabled=false;
}
function selectionSnapshot(){if(!transferSelection)throw Error('生成が完了してから追加してください。');const data=structuredClone(transferSelection);data.ranges[0].label=$('comparison-label').value.trim()||data.ranges[0].label;return data;}
function saveSelection(data){saveFile('PLO_'+data.board.join('')+'_selection.json',JSON.stringify(data,null,2),'application/json');}
$('save-selection').onclick=()=>{try{saveSelection(selectionSnapshot());}catch(e){toast(e.message);}};
$('send-comparison').onclick=async()=>{
 if(transferBusy)return;let data;try{data=selectionSnapshot();}catch(e){toast(e.message);return;}
 if(!connected){saveSelection(data);return;}transferBusy=true;$('send-comparison').disabled=true;
 try{const response=await fetch(new URL('selection',location.href),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});const reply=await response.json();if(!response.ok)throw Error(reply.error||'条件を追加できませんでした。');toast(reply.message);}
 catch(e){toast('追加できません：'+e.message+' 比較アプリを開き直すか、条件JSONを保存してください。');}
 finally{transferBusy=false;$('send-comparison').disabled=!transferSelection;}
};

// Per-hand report, separate from the range filter and its export.
let bdsdReport=null,bdsdVersion=0;
const bdsdNames={madeStraight:'ストレート完成',sd:'通常のストレートドロー',bdsd:'全てのBDSD',bdsd9:'BDSD 9+',bdsd8:'BDSD 8',bdsd4:'BDSD 4',bdsdOther:'その他',none:'なし'};
function clearBdsdReport(){bdsdVersion++;bdsdReport=null;$('analyze-bdsd').disabled=summary?.street!==3;$('bdsd-report').replaceChildren();$('save-bdsd').disabled=true;}
function bdsdText(parent,tag,text,className){const el=document.createElement(tag);el.textContent=text;if(className)el.className=className;parent.append(el);return el;}
function witnessText(w){return w?`手札 ${w.hand.join(' ')} ＋ ボード ${w.board.join(' ')} → ${'23456789TJQKA'[w.high-2]}ハイ`:'';}
function renderBdsdReport(report){
 const wrap=$('bdsd-report');wrap.replaceChildren();
 bdsdText(wrap,'h3',`主判定：${bdsdNames[report.primary]}${report.provisional?'（暫定）':''}`);
 bdsdText(wrap,'p',`ボード ${report.board.join(' ')} ／ 手札 ${report.hand.join(' ')}`,'muted');
 if(report.provisional)bdsdText(wrap,'p','手札の残り2枚が不明です。以下は既知2枚だけで仮計算した結果で、枚数は確定値ではありません。残り2枚に通常SDがあればBDSD対象外となり、ブロッカーでも結果が変わります。','bdsd-warning');
 if(report.excludedAtFlop){
  bdsdText(wrap,'p',report.regular?'フロップで通常のストレートドローがあるため、このハンド全体をBDSDから除外します。':'フロップで既にストレートが完成しているため、BDSD対象外です。','bdsd-warning');
  if(report.flopMade)bdsdText(wrap,'p',witnessText(report.flopMade));
  if(report.normalTurns.length){
   bdsdText(wrap,'p','1枚で完成するターン：'+report.normalTurns.map(t=>t.card).join(' '));
   const d=document.createElement('details');wrap.append(d);bdsdText(d,'summary','通常SDの完成例');
   for(const t of report.normalTurns)bdsdText(d,'p',`ターン ${t.card}：${witnessText(t.self)}`);
  }return;
 }
 if(!report.provisional)bdsdText(wrap,'p',`全${report.remainingCards}枚のターンを個別に判定。各ターン後は44枚のリバーを検証。同じランクは同じ結果になるためまとめて表示します。`,'muted');
 bdsdText(wrap,'p','4・8は固定した手札2枚の組ごとに判定。9+は3枚以上の組み合わせで成立するアウツを重複なく数えます。残りの手札もブロッカーとして反映します。','muted');
 for(const key of ['bdsd9','bdsd8','bdsd4','bdsdOther']){
  const turns=report.turns.filter(t=>t.groups.some(g=>g.category===key)),section=bdsdText(wrap,'section','','bdsd-group');
  bdsdText(section,'h4',bdsdNames[key]);
  if(!turns.length){bdsdText(section,'p','該当なし','muted');continue;}
  bdsdText(section,'p',`成立ターン：${turns.map(t=>t.rank).join('・')} ／ 物理ターンカード合計 ${turns.reduce((n,t)=>n+t.turnCount,0)}枚${report.provisional?'（暫定）':''}`);
  for(const t of turns)for(const group of t.groups.filter(g=>g.category===key)){
   const d=document.createElement('details');section.append(d);
   bdsdText(d,'summary',`ターン ${t.rank} ／ 手札の組 ${group.ranks.join('')}（${group.size}枚） ／ 有効リバー ${group.outCount}枚${report.provisional?'（暫定）':''}`);
   bdsdText(d,'p',`使用する手札${group.size}枚の組：${group.hands.map(cs=>cs.join(' ')).join(' ／ ')}`);
   bdsdText(d,'p',`成立ターンカード：${t.turnCards.join(' ')}（${t.turnCount}枚）`);
   bdsdText(d,'p',`以下はターン ${t.representativeTurn}、手札の組 ${group.hands[0].join(' ')} の場合。${group.size>=3?'各ストレートには、この組のうち必ず2枚だけを使います。':''}`,'muted');
   const ul=document.createElement('ul');d.append(ul);
   for(const out of group.valid){
    const li=bdsdText(ul,'li',`リバー ${out.cards.join(' ')}（${out.count}枚）：${'23456789TJQKA'[out.high-2]}ハイ。${out.opponentHigh===out.high?'相手の同率ストレートは可。':'相手の上位ストレートなし。'}`);
    bdsdText(li,'p',witnessText(out.backdoor),'muted');
   }
  }
 }
 const excluded=document.createElement('details');wrap.append(excluded);bdsdText(excluded,'summary','除外したカード・判定理由');
 bdsdText(excluded,'h4','ターンで相手に上位ストレートが成立可能');let turnExclusions=0;
 for(const t of report.turns)for(const group of t.excludedGroups.filter(g=>g.status==='opponentOnTurn')){
  turnExclusions++;bdsdText(excluded,'p',`ターン ${t.turnCards.join(' ')} ／ 手札の組 ${group.hands.map(cs=>cs.join(' ')).join(' ／ ')}：相手 ${witnessText(group.opponent)}。この組での将来最高：${group.maxFutureHigh?'23456789TJQKA'[group.maxFutureHigh-2]+'ハイ':'完成なし'}。`);
 }
 if(!turnExclusions)bdsdText(excluded,'p','該当なし','muted');
 bdsdText(excluded,'h4','リバーで相手に上位ストレートが成立可能');let riverExclusions=0;
 for(const t of report.turns)for(const group of [...t.groups,...t.excludedGroups])for(const out of group.excluded){
  riverExclusions++;bdsdText(excluded,'p',`ターン ${t.turnCards.join(' ')} → リバー ${out.cards.join(' ')} ／ 手札の組 ${group.ranks.join('')}：自分 ${'23456789TJQKA'[out.high-2]}ハイより相手 ${'23456789TJQKA'[out.opponentHigh-2]}ハイが上位。`);
  bdsdText(excluded,'p',`例（ターン ${t.representativeTurn}）：自分 ${witnessText(out.self)} ／ 相手 ${witnessText(out.opponent)}`,'muted');
 }
 if(!riverExclusions)bdsdText(excluded,'p','該当なし','muted');
 const none=report.turns.filter(t=>t.status==='noOuts');if(none.length){bdsdText(excluded,'h4','有効な組み合わせなし');bdsdText(excluded,'p',none.flatMap(t=>t.turnCards).join(' '));}
}
async function inspectBdsd(){
 const version=++bdsdVersion;$('analyze-bdsd').disabled=true;$('save-bdsd').disabled=true;
 try{const report=await request('bdsdDetails',{hand:$('bdsd-hand').value});if(version!==bdsdVersion)return;bdsdReport=report;renderBdsdReport(report);$('save-bdsd').disabled=false;}
 catch(e){if(version===bdsdVersion){bdsdReport=null;$('bdsd-report').replaceChildren();bdsdText($('bdsd-report'),'p',e.message,'error');}}
 finally{if(version===bdsdVersion)$('analyze-bdsd').disabled=summary?.street!==3;}
}
$('bdsd-form').onsubmit=e=>{e.preventDefault();inspectBdsd();};
$('bdsd-hand').oninput=clearBdsdReport;
$('save-bdsd').onclick=()=>{if(bdsdReport)saveFile('PLO_'+bdsdReport.board.join('')+'_'+bdsdReport.hand.join('')+'_BDSD.json',JSON.stringify(bdsdReport,null,2),'application/json');};

const requestedBoard=new URLSearchParams(location.search).get('board');if(requestedBoard){try{PLO.parseBoard(requestedBoard);$('board-input').value=requestedBoard;}catch{}}
renderCards();analyze($('board-input').value);


