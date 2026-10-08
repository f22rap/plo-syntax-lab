'use strict';
const {parentPort,workerData} = require('node:worker_threads');
const P = require('../lab/engine.js'), C = require('../linux/compare.cjs');
const {action,input} = workerData;
function fail(code,message,details={}) { const e=new Error(message);Object.assign(e,{code,details});throw e; }
function board(text) { try { return P.parseBoard(text); } catch(e) { fail('INVALID_BOARD',e.message); } }
function canonical(cards) { return cards.slice(0,3).sort((a,b)=>a-b).concat(cards.slice(3)); }
(async()=>{
 const b=board(input.board);
 if(action==='dataset') {
  try { const dataset=C.parseDataset(b,input.file);return {...dataset,totalWeight:C.format(dataset.rows.reduce((n,r)=>n+r.weight,0n))}; }
  catch(e) { const line=/, 行(\d+):/.exec(e.message);fail('CSV_VALIDATION_ERROR',e.message,{file:input.file.name,...(line?{row:Number(line[1])}:{})}); }
 }
 if(action==='compare') {
  try { return C.compare({...input,board:canonical(b).map(P.card).join('')},{datasets:input.datasets,percentAsString:true}); }
  catch(e) { fail('CSV_VALIDATION_ERROR',e.message); }
 }
 const engine=new P.Engine();await engine.prepare(b);
 if(action==='analyze')return engine.summary();
 if(action==='generate') {
  if(input.mode==='defaults') return require('../defaults.js')(P,engine,require('../default-labels.json'));
  const cache=new Map();
  return input.requests.map(request=>{
   const f={role:'all',fd:'all',sd:'all',bdfd:'all',clean:'',blockers:[],...request.filter};
   if(f.pocket==null)delete f.pocket;
   if(!/^cat:[0-8]$/.test(f.role)&&f.role!=='all'&&!engine.roleIndex.has(f.role))fail('INVALID_FILTER','このボードに存在しない役です。',{role:f.role});
   const names=new Set();for(const blocker of f.blockers){if(names.has(blocker.rank))fail('INVALID_FILTER','同じランクのブロッカー指定は1つだけです。');names.add(blocker.rank);}
   const wanted=k=>f[k]!=='all';
   if(f.clean&&(['fd','sd'].some(k=>wanted(k)&&f[k]!=='none')||(f.clean==='all'&&wanted('bdfd')&&f.bdfd!=='none')))fail('INVALID_FILTER','ドローなし条件と個別ドローが矛盾しています。');
   let reason=null;
   if(b.length===5&&['fd','sd','bdfd'].some(wanted))reason='リバーにドロー条件はありません。';
   else if(b.length!==3&&wanted('bdfd'))reason='BDFDはフロップ限定です。';
   if(reason)return {label:request.label,filter:f,count:0,syntax:'',status:'UNSUPPORTED',reason,examples:[]};
   try {const key=JSON.stringify(f);let result=cache.get(key);if(!result){result=engine.syntax(f);cache.set(key,result);}return {label:request.label,filter:f,...result,examples:result.examples.map(x=>x.cards.join('')),status:result.count?'MATCHES':'EMPTY',reason:result.count?null:'このボードでは該当なし'};}
   catch(e){fail('INTERNAL_ERROR','条件の生成または照合に失敗しました。');}
  });
 }
 if(action==='match') {
  let test;try{test=P.compile(input.syntax);}catch(e){fail('INVALID_SYNTAX','syntaxを解析できません。');}
  return input.hands.map(text=>{
   const cards=text.match(/../g).map(c=>'23456789TJQKA'.indexOf(c[0])*4+'shdc'.indexOf(c[1]));
   if(new Set(cards).size!==4||cards.some(c=>b.includes(c)))fail('INVALID_HAND','手札内の重複またはボードとの衝突です。',{hand:text});
   const h=cards.slice().sort((a,b)=>a-b),packed=h[0]|h[1]<<6|h[2]<<12|h[3]<<18,i=engine.hands.indexOf(packed),role=engine.roles[engine.handRoles[i]];
   const made=P.combos(h,2).some(pair=>engine.pairs[pair[0]*52+pair[1]].sh);
   const pairDraws=made?[]:P.combos(h,2).flatMap(pair=>{const p=engine.pairs[pair[0]*52+pair[1]];return ['nutGut','nonGut','nutOpen','nonOpen'].filter(k=>p.drawFlags&P.F[k]).map(classification=>({cards:pair.map(P.card),classification}));});
   return {hand:text,matched:test(h),role:{key:role.key,label:role.label,category:role.cat},pairDraws,outs:engine.outs[i],nutOuts:engine.nutOuts[i]};
  });
 }
 fail('INTERNAL_ERROR','未知の処理です。');
})().then(result=>parentPort.postMessage({result})).catch(e=>parentPort.postMessage({error:{code:e.code||'INTERNAL_ERROR',message:e.code?e.message:'計算処理に失敗しました。',details:e.details||{}}}));
