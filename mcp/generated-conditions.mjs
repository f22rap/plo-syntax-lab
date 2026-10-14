import fs from 'node:fs';
import path from 'node:path';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {Worker} from 'node:worker_threads';
import {randomUUID} from 'node:crypto';
const require=createRequire(import.meta.url),R=require('../src/research-recipes.cjs');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const files=['src/lab/engine.js','src/defaults.js','src/default-labels.json','src/default-presets.json','src/research-recipes.cjs','src/research-generation.cjs','mcp/generated-condition-worker.cjs','mcp/generated-conditions.mjs'];
const versions={resolver_version:'generated-conditions-v1',ruleset:'plo4-flop-v1',files:Object.fromEntries(files.map(p=>[p,R.hash(fs.readFileSync(path.join(root,p),'utf8'))]))};
versions.engine_hash=R.hash(versions.files);
versions.preset_registry_hash=versions.files['src/default-presets.json'];
function verifySources(){for(const p of files)if(R.hash(fs.readFileSync(path.join(root,p),'utf8'))!==versions.files[p])throw Error('生成処理が起動後に変更されました。再起動してください。');}
const clone=x=>structuredClone(x);
function runWorker(board,condition_set,{signal,timeoutSeconds,progress}){
 if(signal?.aborted)return Promise.reject(Error('Cancelled.'));
 return new Promise((resolve,reject)=>{
  const worker=new Worker(new URL('./generated-condition-worker.cjs',import.meta.url),{workerData:{board,condition_set}});let finished=false;
  const finish=(error,result)=>{if(finished)return;finished=true;clearTimeout(timer);signal?.removeEventListener('abort',abort);void worker.terminate();error?reject(error):resolve(result);};
  const abort=()=>finish(Error('Cancelled.'));
  const timer=setTimeout(()=>finish(Error('Syntax generation timed out.')),timeoutSeconds*1000);
  signal?.addEventListener('abort',abort,{once:true});if(signal?.aborted){abort();return;}
  worker.on('error',e=>finish(e));worker.on('exit',()=>{if(!finished)finish(Error('Generation worker exited without a result.'));});
  worker.on('message',m=>{
   if(m.progress){try{progress(m.progress);}catch(e){finish(e);}}
   else if(m.error)finish(Error(m.error));else finish(null,m.result);
  });
 });
}
export const listPresets=R.listPresets,createPresetSet=R.createPresetSet,normalizeSet=R.normalizeSet;
export function verifyGenerationVersion(expected){verifySources();if(expected&&expected!==versions.engine_hash)throw Error('生成版が変更されています。再検証してください。');}
export function researchSnapshot(preview,originalSet,{allowMissing=false}={}){
 if(preview.status==='cancelled')throw Error('Cancelled.');
 if(R.stable(R.normalizeSet(originalSet))!==R.stable(preview.condition_set))throw Error('生成した条件が保存定義と一致しません。');
 const conditions=originalSet.conditions.filter(c=>c.enabled);
 if(preview.rows.length!==preview.boards.length*conditions.length||new Set(preview.rows.map(r=>r.board+':'+r.condition_id)).size!==preview.rows.length)throw Error('生成結果が不完全です。');
 for(const r of preview.rows){
  const c=conditions.find(c=>c.condition_id===r.condition_id);
  if(!c||!preview.boards.includes(r.board)||c.label!==r.label||c.kind!==r.kind||R.conditionHash(c)!==r.recipe_hash)throw Error('生成結果と条件の対応が不正です。');
  if(!['ready','empty_condition'].includes(r.status)&&!(allowMissing&&r.status==='missing_definition'&&c.kind==='per_board'))throw Error(`${r.board} / ${r.label}: ${r.status} / ${r.reason}`);
 }
 verifyGenerationVersion(preview.versions.engine_hash);
 const fields=['condition_id','label','board','kind','recipe_hash','status','reason_code','reason','syntax','legal_hand_count','verified'];
 const payload_json=JSON.stringify({schema_version:1,condition_set:originalSet,boards:preview.boards,versions:preview.versions,rows:preview.rows.map(r=>Object.fromEntries(fields.map(k=>[k,r[k]??null])))});
 if(Buffer.byteLength(payload_json)>12*1024*1024)throw Error('生成結果が12MiBを超えました。対象ボードか条件数を減らしてください。');
 return {schema_version:1,payload_json,sha256:R.hash(payload_json)};
}
export class ConditionGenerator {
 constructor({timeoutSeconds=300,maxCacheBytes=16777216}={}){
  if(!Number.isInteger(timeoutSeconds)||timeoutSeconds<1||timeoutSeconds>3600)throw Error('Invalid generation timeout.');
  if(!Number.isSafeInteger(maxCacheBytes)||maxCacheBytes<0||maxCacheBytes>67108864)throw Error('Invalid cache limit.');
  this.timeoutSeconds=timeoutSeconds;this.maxCacheBytes=maxCacheBytes;this.cache=new Map();this.cacheBytes=0;
 }
 remember(key,row){
  if(!row.verified||!['ready','empty_condition'].includes(row.status))return;
  const saved=clone(row);delete saved.condition_id;delete saved.label;delete saved.elapsed_ms;
  const bytes=Buffer.byteLength(JSON.stringify(saved));if(bytes>this.maxCacheBytes)return;
  const old=this.cache.get(key);if(old){this.cacheBytes-=old.bytes;this.cache.delete(key);}
  while(this.cache.size&&(this.cacheBytes+bytes>this.maxCacheBytes||this.cache.size>=2048)){const first=this.cache.keys().next().value;this.cacheBytes-=this.cache.get(first).bytes;this.cache.delete(first);}
  this.cache.set(key,{row:saved,bytes});this.cacheBytes+=bytes;
 }
 async generate({condition_set,boards},{signal,progress=()=>{}}={}){
  verifySources();const set=R.normalizeSet(condition_set),conditions=set.conditions.filter(c=>c.enabled);
  if(!conditions.length)throw Error('使用する条件を1個以上選んでください。');
  if(!Array.isArray(boards)||!boards.length||boards.length>200)throw Error('ボードは1〜200個指定してください。');
  const canonical=[...new Set(boards.map(R.canonicalBoard))];if(canonical.length*conditions.length>10000)throw Error('ボード×条件は10,000行までです。');
  const start=Date.now(),preview={schema_version:1,kind:'generated_condition_preview',preview_id:randomUUID(),condition_set:set,boards:canonical,versions:clone(versions),rows:[],status:'ready',statistics:{target_boards:canonical.length,completed_boards:0,board_analyses:0,cache_hits:0,elapsed_ms:0}};
  preview.input_fingerprint=R.hash({condition_set:set,boards:canonical,engine_hash:versions.engine_hash});
  for(const board of canonical){
   if(signal?.aborted){preview.status='cancelled';break;}
   const keys=new Map(conditions.map(c=>[c.condition_id,R.hash({board,recipe_hash:R.conditionHash(c),engine_hash:versions.engine_hash})]));
   const found=new Map(),missing=[];
   for(const c of conditions){
    const key=keys.get(c.condition_id),cached=this.cache.get(key);
    if(cached){this.cache.delete(key);this.cache.set(key,cached);found.set(c.condition_id,{...clone(cached.row),condition_id:c.condition_id,label:c.label,elapsed_ms:0,cache_hit:true});preview.statistics.cache_hits++;}
    else missing.push(c);
   }
   if(missing.length){
    try{
     preview.statistics.board_analyses++;
     const result=await runWorker(board,{...set,conditions:missing},{signal,timeoutSeconds:this.timeoutSeconds,progress:p=>progress({board,board_index:preview.statistics.completed_boards+1,board_count:canonical.length,...p})});
     if(signal?.aborted){preview.status='cancelled';break;}
     for(const row of result.rows){found.set(row.condition_id,{...row,cache_hit:false});this.remember(keys.get(row.condition_id),row);}
    }catch(e){
     if(signal?.aborted){preview.status='cancelled';break;}
     for(const c of missing)found.set(c.condition_id,{condition_id:c.condition_id,label:c.label,board,kind:c.kind,recipe_hash:R.conditionHash(c),status:'generation_failed',reason_code:'generation_failed',reason:e.message,syntax:'',legal_hand_count:null,verified:false,cache_hit:false});
    }
   }
   for(const c of conditions){const row=found.get(c.condition_id);if(!row)throw Error('生成結果の行が不足しています。');preview.rows.push(row);}
   preview.statistics.completed_boards++;progress({stage:'board_complete',board,done:preview.statistics.completed_boards,total:canonical.length});
  }
  verifySources();
  if(preview.status!=='cancelled'&&preview.rows.some(r=>!['ready','empty_condition'].includes(r.status)))preview.status='blocked';
  preview.statistics.elapsed_ms=Date.now()-start;
  preview.statistics.ready=preview.rows.filter(r=>r.status==='ready').length;
  preview.statistics.empty=preview.rows.filter(r=>r.status==='empty_condition').length;
  preview.statistics.blocked=preview.rows.filter(r=>!['ready','empty_condition'].includes(r.status)).length;
  preview.snapshot_hash=R.hash({input_fingerprint:preview.input_fingerprint,status:preview.status,rows:preview.rows.map(({elapsed_ms,cache_hit,...r})=>r)});
  return preview;
 }
}
