import fs from 'node:fs/promises';
import path from 'node:path';
import {randomUUID} from 'node:crypto';
import {createRequire} from 'node:module';
import {root,hash,fileHash,within,inputFile,native,engine,checkAbort,sourceCommit} from './runtime.mjs';
import {chart,report,metricNote} from './artifacts.mjs';
const require=createRequire(import.meta.url),P=require('../src/lab/engine.js');
export const VERSION=require('../package.json').version;
const boardOf=text=>{const cards=P.parseBoard(text.replace(/;/g,''));return cards.slice(0,3).sort((a,b)=>a-b).concat(cards.slice(3)).map(P.card).join('');};
const publicJob=j=>({job_id:j.id,state:j.state,progress:j.progress,result:j.result,error:j.error});

export class Service {
 constructor(config){this.config=config;this.datasets=new Map();this.conditions=new Map();this.defaults=new Map();this.schemas=new Map();this.jobs=new Map();this.artifacts=new Map();this.queue=Promise.resolve();this.closed=false;}
 async init(){
  const files=['src/lab/engine.js','src/defaults.js','src/default-labels.json','src/SyntaxMatcher.cs','src/compare-runtime.ps1','src/CompareCommands.cs','src/Core.cs','src/Execution.cs','src/History.cs','src/LabSelection.cs','mcp/NativeHost.cs','dist/PloMcp.Native.exe'];
  this.versions={mcp:VERSION,source_commit:await sourceCommit(),files:Object.fromEntries(await Promise.all(files.map(async f=>[f,await fileHash(path.join(root,f))])))};
  return this;
 }
 status(){return {name:'PLO Syntax Lab',version:VERSION,input_roots:this.config.inputRoots,output_root:this.config.outputRoot,history_root:this.config.historyRoot,history_delete_enabled:this.config.allowHistoryDelete,metric:metricNote,monker_verified:false,versions:this.versions};}
 job(kind,run){
  if(this.closed)throw Error('Server is closing.');
  if([...this.jobs.values()].filter(j=>['queued','running','committing'].includes(j.state)).length>=20)throw Error('Too many pending jobs.');
  for(const [id,j] of this.jobs){if(this.jobs.size<200)break;if(['completed','failed','cancelled'].includes(j.state))this.jobs.delete(id);}
  const j={id:randomUUID(),kind,state:'queued',progress:{message:'Waiting'},controller:new AbortController()};this.jobs.set(j.id,j);
  this.queue=this.queue.catch(()=>{}).then(async()=>{
   if(j.controller.signal.aborted){j.state='cancelled';return;}
   j.state='running';
   try{j.result=await run(j);j.state='completed';j.progress={message:'Completed',percent:100};}
   catch(e){j.state=j.controller.signal.aborted?'cancelled':'failed';j.error=e.message;}
  });return publicJob(j);
 }
 getJob(id){const j=this.jobs.get(id);if(!j)throw Error('Unknown job_id (jobs are session-scoped).');return publicJob(j);}
 cancelJob(id){const j=this.jobs.get(id);if(!j)throw Error('Unknown job_id.');if(j.state==='committing')return {...publicJob(j),note:'Result is being saved; wait for completion.'};if(['queued','running'].includes(j.state)){j.controller.abort();j.progress={message:'Cancelling'};if(j.state==='queued')j.state='cancelled';}return publicJob(j);}
 async close(){this.closed=true;for(const j of this.jobs.values())if(['queued','running'].includes(j.state))j.controller.abort();await this.queue;}
 addCondition(board,row,filter=null){
  const id='condition_'+hash(JSON.stringify({board,cell:row.cell,label:row.label,syntax:row.syntax,filter})).slice(0,24);
  const item={condition_id:id,board,label:row.label,cell:row.cell||id,count:row.count,syntax:row.syntax||'',available:row.count>0,problem:row.problem||(row.count===0?'このボードでは該当なし':null),filter};
  if(!this.conditions.has(id)&&this.conditions.size>=5000)throw Error('Condition cache is full. Restart the server.');
  this.conditions.set(id,item);return item;
 }
 listConditions({board,query='',offset=0,limit=50}){
  board=boardOf(board);
  if(this.defaults.has(board)){
   const q=query.replace(/\s/g,'').toLowerCase();const all=this.defaults.get(board).filter(r=>(r.label+r.cell).replace(/\s/g,'').toLowerCase().includes(q));
   return {board,total:all.length,offset,conditions:all.slice(offset,offset+limit),next_offset:offset+limit<all.length?offset+limit:null};
  }
  const pending=[...this.jobs.values()].find(j=>j.kind==='defaults:'+board&&['queued','running'].includes(j.state));if(pending)return publicJob(pending);
  return this.job('defaults:'+board,async j=>{
   const result=await engine({action:'defaults',board},{signal:j.controller.signal,timeoutSeconds:this.config.timeoutSeconds,progress:p=>j.progress=p});
   const rows=result.result.map(r=>this.addCondition(board,r));
   if(this.defaults.size>=6)this.defaults.clear();this.defaults.set(board,rows);
   return {board,total:rows.length,next_call:'list_conditions with the same board'};
  });
 }
 filterSchema(board){
  board=boardOf(board);if(this.schemas.has(board))return this.schemas.get(board);
  const pending=[...this.jobs.values()].find(j=>j.kind==='schema:'+board&&['queued','running'].includes(j.state));if(pending)return publicJob(pending);
  return this.job('schema:'+board,async j=>{
   const {result}=await engine({action:'schema',board},{signal:j.controller.signal,timeoutSeconds:this.config.timeoutSeconds,progress:p=>j.progress=p});
   const schema={...result,role_keys:['all',...P.CAT.map((_,i)=>'cat:'+i),...result.roles.map(r=>r.key)],note:'Use these role keys with generate_syntax. Its tool schema lists draw, pocket and blocker options.'};
   if(this.schemas.size>=6)this.schemas.clear();this.schemas.set(board,schema);return schema;
  });
 }
 generate({board,label,filter}){
  board=boardOf(board);
  return this.job('generate',async j=>{
   const {result}=await engine({action:'generate',board,filter},{signal:j.controller.signal,timeoutSeconds:this.config.timeoutSeconds,progress:p=>j.progress=p});
   return this.addCondition(board,{label,syntax:result.syntax,count:result.count},filter);
  });
 }
 async listDatasets({query='',offset=0,limit=50}){
  const found=[],visited=new Set();let truncated=false;
  const walk=async(dir,depth)=>{
   if(depth>24||visited.size>=10000||found.length>=2000){truncated=true;return;}
   const real=await fs.realpath(dir);if(visited.has(real))return;visited.add(real);
   if(!this.config.inputRoots.some(r=>within(r,real)))return;
   for(const entry of await fs.readdir(real,{withFileTypes:true})){
    if(entry.isSymbolicLink())continue;
    const full=path.join(real,entry.name);
    if(entry.isDirectory())await walk(full,depth+1);
    else if(entry.isFile()&&entry.name.toLowerCase().endsWith('.csv')&&full.toLowerCase().includes(query.toLowerCase())){
     if(found.length>=2000){truncated=true;return;}
     try{const safe=await inputFile(this.config,full);found.push({path:safe,bytes:(await fs.stat(safe)).size});}catch{/* Out-of-scope links and oversized files are not listed. */}
    }
   }
  };
  for(const dir of this.config.inputRoots)await walk(dir,0);
  found.sort((a,b)=>a.path.localeCompare(b.path));
  return {total:found.length,truncated,files:found.slice(offset,offset+limit),next_offset:offset+limit<found.length?offset+limit:null};
 }
 async register({path:file,label}){
  const safe=await inputFile(this.config,file),sha256=await fileHash(safe);
  const handle=await fs.open(safe,'r');let header;
  try{const b=Buffer.alloc(16384),{bytesRead}=await handle.read(b,0,b.length,0);header=b.subarray(0,bytesRead).toString('utf8').replace(/^\uFEFF/,'').split(/\r?\n/)[0].split(',').map(s=>s.trim().replace(/^"|"$/g,'').toLowerCase());}finally{await handle.close();}
  if(!header.includes('weight')||(!header.includes('hand')&&!header.includes('combo')))throw Error('CSV requires hand/combo and weight columns.');
  const dataset={dataset_id:'dataset_'+hash(safe+'\0'+sha256+'\0'+label).slice(0,24),path:safe,label,sha256,bytes:(await fs.stat(safe)).size};
  if(this.datasets.size>=500&&!this.datasets.has(dataset.dataset_id))throw Error('Dataset registry is full. Restart server.');
  this.datasets.set(dataset.dataset_id,dataset);return {...dataset,validation:'Header checked. Full row and board validation occurs during comparison.'};
 }
 compare({board,dataset_ids,condition_ids=[],include_all=true}){
  board=boardOf(board);
  const datasets=dataset_ids.map(id=>{const d=this.datasets.get(id);if(!d)throw Error('Register the CSV first: '+id);return {...d};});
  if(new Set(datasets.map(d=>d.path.toLowerCase())).size!==datasets.length)throw Error('The same CSV cannot be selected twice.');
  const conditions=condition_ids.map(id=>{const c=this.conditions.get(id);if(!c)throw Error('Generate or list conditions first: '+id);if(c.board!==board)throw Error('Condition belongs to a different board.');if(!c.available)throw Error('Unavailable condition: '+c.label);return {...c};});
  if(new Set(condition_ids).size!==condition_ids.length)throw Error('Duplicate conditions.');
  if(!include_all&&!conditions.length)throw Error('Select conditions or include_all.');
  return this.job('compare',async j=>{
   const signal=j.controller.signal;
   j.progress={message:'Checking source files'};
   for(const d of datasets){checkAbort(signal);await inputFile(this.config,d.path);if(await fileHash(d.path)!==d.sha256)throw Error('CSV changed since registration. Register it again: '+d.label);}
   // Read-only inputs; verify again before saving to detect changes during computation.
   j.progress={message:'Comparing CSV weights'};
   const bundle=await native({action:'compare',board,paths:datasets.map(d=>d.path),labels:datasets.map(d=>d.label),ranges:conditions,includeAll:include_all},{signal,timeoutSeconds:this.config.timeoutSeconds});
   for(const d of datasets){checkAbort(signal);await inputFile(this.config,d.path);if(await fileHash(d.path)!==d.sha256)throw Error('CSV changed during comparison. Result was not saved.');}
   checkAbort(signal);j.state='committing';j.progress={message:'Saving history'};
   const provenance={versions:this.versions,datasets,conditions,include_all,metric:'Each CSV matching weight sum / combined matching weight sum * 100',monker_verified:false};
   const saved=await native({action:'save',historyRoot:this.config.historyRoot,...bundle,provenance},{timeoutSeconds:30});
   return {result_id:saved.id,board,saved_utc:saved.saved_utc,row_count:bundle.rows.length,next_call:'get_result'};
  });
 }
 async getResult(id,{offset=0,limit=50,include_command=false}={}){
  const result=await native({action:'get',historyRoot:this.config.historyRoot,id},{timeoutSeconds:30});
  return {result_id:result.id,board:result.board,saved_utc:result.saved_utc,total:result.rows.length,rows:result.rows.slice(offset,offset+limit),next_offset:offset+limit<result.rows.length?offset+limit:null,provenance:result.provenance,metric:metricNote,decimal_encoding:'Exact decimal strings; null percent means zero denominator.',...(include_command?{command:result.command}:{})};
 }
 async history({query='',offset=0,limit=50}){
  const result=await native({action:'list',historyRoot:this.config.historyRoot},{timeoutSeconds:30});
  const q=query.toLowerCase(),entries=result.entries.filter(e=>[e.board,...e.labels,...e.conditions].join(' ').toLowerCase().includes(q));
  return {total:entries.length,unreadable:result.unreadable,entries:entries.slice(offset,offset+limit),next_offset:offset+limit<entries.length?offset+limit:null};
 }
 async deleteHistory({result_id,confirm}){
  if(!this.config.allowHistoryDelete)throw Error('History deletion is disabled in the local configuration.');
  if(confirm!==result_id)throw Error('confirm must equal the exact result_id to delete.');
  return native({action:'delete',id:result_id,historyRoot:this.config.historyRoot},{timeoutSeconds:30});
 }
 async export({result_id,format,narrative=''}){
  const result=await native({action:'get',id:result_id,historyRoot:this.config.historyRoot},{timeoutSeconds:30});
  const formats={csv:['text/csv',Buffer.from(result.csvBase64,'base64')],svg:['image/svg+xml',chart(result)],markdown:['text/markdown',report(result,narrative)],powershell:['text/plain','\uFEFF'+result.command],json:['application/json',JSON.stringify({result_id:result.id,board:result.board,saved_utc:result.saved_utc,rows:result.rows,provenance:result.provenance,metric:metricNote},null,2)]};
  const [mimeType,content]=formats[format],extension={markdown:'md',powershell:'ps1'}[format]||format;
  const id=randomUUID(),file=path.join(this.config.outputRoot,id+'.'+extension);
  if(await fs.realpath(this.config.outputRoot)!==this.config.outputRoot)throw Error('Output root changed.');
  await fs.writeFile(file,content,{flag:'wx'});
  const artifact={artifact_id:id,result_id,path:file,uri:'plo://artifacts/'+id,mimeType,bytes:Buffer.byteLength(content)};
  this.artifacts.set(id,artifact);return artifact;
 }
 async readArtifact(id){
  const a=this.artifacts.get(id);if(!a)throw Error('Unknown artifact_id (artifacts are session-scoped).');
  const real=await fs.realpath(a.path);if(!within(this.config.outputRoot,real))throw Error('Artifact is outside output root.');
  if((await fs.stat(real)).size>2*1024*1024)throw Error('Artifact is too large to return inline; use its exported file.');
  return {...a,text:await fs.readFile(real,'utf8')};
 }
}
