'use strict';
const {parentPort,workerData}=require('node:worker_threads');
const P=require('../src/lab/engine.js');
const defaults=require('../src/defaults.js');
const catalog=require('../src/default-labels.json');
(async()=>{
 const e=new P.Engine();
 const summary=await e.prepare(P.parseBoard(workerData.board),p=>parentPort.postMessage({progress:p}));
 let result;
 if(workerData.action==='schema')result=summary;
 else if(workerData.action==='defaults')result=await defaults(P,e,catalog);
 else if(workerData.action==='generate'){
  const f=workerData.filter;
  if(f.role&&f.role!=='all'&&!/^cat:[0-8]$/.test(f.role)&&!summary.roles.some(r=>r.key===f.role))throw Error('Unknown role for this board. Use get_filter_schema.');
  if((f.blockers||[]).some(b=>!e.ranks.includes(b.rank)))throw Error('Blockers must be ranks on the board.');
  if(e.board.length!==3&&f.bdfd&&!['all','none'].includes(f.bdfd))throw Error('BDFD is flop-only.');
  if(e.board.length===5&&[f.fd,f.sd].some(v=>v&&!['all','none'].includes(v)))throw Error('Draws are unavailable on the river.');
  result=e.syntax(f);
 } else throw Error('Unknown engine action.');
 parentPort.postMessage({result,board:summary.board.join('')});
})().catch(e=>parentPort.postMessage({error:e.message}));
