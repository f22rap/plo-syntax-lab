import contracts from '../src/mcp/contracts.cjs';
import domain from '../src/mcp/domain.cjs';
import calculations from '../src/mcp/compute.cjs';
import comparison from '../src/linux/compare.cjs';
import engine from '../src/lab/engine.js';
import {Store} from './store.mjs';
const {catalog,validate,ApiError}=contracts,{VERSION,definitions,boardInfo}=domain,{compute}=calculations;
const MiB=1024*1024,encoder=new TextEncoder(),bytes=value=>encoder.encode(value).length;
const terminal=state=>['succeeded','failed','cancelled'].includes(state);
const uuid=()=>crypto.randomUUID(),iso=value=>new Date(value).toISOString();
export const limits=Object.freeze({csvBytesPerFile:2*MiB,csvBytesPerCompare:6*MiB,maxRanges:20,
 maxRequestsPerGeneration:10,maxConcurrentJobs:1,jobTimeoutSeconds:25,synchronousTimeoutSeconds:25,
 inlineCsvBytes:256*1024,responseBytes:256*1024,resourceResponseBytes:8*MiB,retainedResultsBytes:8*MiB,
 ttlSeconds:3600,maxRowsPerPage:10,maxDatasetRows:50000});
let calculating=false;
export async function sha256(text){return [...new Uint8Array(await crypto.subtle.digest('SHA-256',encoder.encode(text)))].map(n=>n.toString(16).padStart(2,'0')).join('');}
function applicationError(error){if(error instanceof ApiError)return error;if(['INVALID_BOARD','INVALID_HAND','INVALID_FILTER','INVALID_SYNTAX','CSV_VALIDATION_ERROR','JOB_CANCELLED','JOB_TIMEOUT','INTERNAL_ERROR'].includes(error?.code))return new ApiError(error.code,error.message,error.details||{});return new ApiError('STORAGE_UNAVAILABLE','保存サービスを利用できません。少し待って再実行してください。',{},true);}
export class CloudService {
 constructor(env,owner,{origin,defer,now=Date.now,sourceCommit=null}={}){
  this.now=now;this.origin=origin;this.defer=defer||(()=>{});this.store=owner?new Store(env.DB,env.FILES,owner,now):null;
  this.engine={sourceCommit,classificationVersion:VERSION,game:'PLO4'};
 }
 expiry(){return this.now()+limits.ttlSeconds*1000;}
 envelope(data){return {ok:true,apiVersion:'1.0.0',engine:this.engine,data,error:null,warnings:this.engine.sourceCommit?[]:['ビルド元コミットが不明です。']};}
 checked(name,data){const result=this.envelope(data);validate(name,'output',result);if(bytes(JSON.stringify({content:[{type:'text',text:JSON.stringify(result)}],structuredContent:result,isError:false}))>limits.responseBytes)throw new ApiError('OUTPUT_TOO_LARGE','limit・条件数を減らすかincludeSyntax=falseを指定してください。');return result;}
 async call(name,args={},options={}){
  try{if(bytes(JSON.stringify(args))>4*MiB)throw new ApiError('LIMIT_EXCEEDED','ツール引数は4MiBまでです。');validate(name,'input',args);if(!['plo_capabilities','plo_definitions'].includes(name)&&!this.store)throw new ApiError('PERMISSION_DENIED','サインインが必要です。');return this.checked(name,await this.dispatch(name,args,options));}
  catch(error){const e=applicationError(error);return {ok:false,apiVersion:'1.0.0',engine:this.engine,data:null,error:{code:e.code,message:e.message,retryable:e.retryable,details:e.details},warnings:[]};}
 }
 async calculate(action,input,signal){
  if(calculating)throw new ApiError('SERVER_BUSY','別の計算が実行中です。少し待って再実行してください。',{},true);
  calculating=true;try{return await compute(action,input,{deadline:Date.now()+limits.synchronousTimeoutSeconds*1000,signal});}finally{calculating=false;}
 }
 page(args){const limit=args.limit??limits.maxRowsPerPage;if(limit>limits.maxRowsPerPage)throw new ApiError('LIMIT_EXCEEDED','クラウド版は1ページ10件までです。');return {offset:args.offset??0,limit};}
 checkBoard(record,board){if(record.boardKey!==board.boardKey)throw new ApiError('BOARD_MISMATCH','IDのボードが入力ボードと一致しません。');if(record.classificationVersion&&record.classificationVersion!==VERSION)throw new ApiError('ENGINE_VERSION_MISMATCH','条件のエンジン版が一致しません。');}
 async dispatch(name,a,{signal}={}){
  if(name==='plo_capabilities')return {supportedTransports:['streamable_http'],supportedProtocolVersions:['2025-11-25'],operations:catalog.tools.map(t=>t.name),scopes:['plo:analyze','plo:datasets','plo:compare'],sdkCompatibilityTested:true,limits};
  if(name==='plo_definitions')return {definitions:(a.topics||Object.keys(definitions)).map(topic=>({topic,text:topic==='limits'?'クラウド：CSV単体2MiB・50,000行、比較合計6MiB、条件20件、生成10件、ページ10件。同時比較1件、25秒、IDは1時間。':definitions[topic]})),examples:[{board:'Ts9h3d',filter:{sd:'nutGut'},syntax:'(KQ,KJ)'},{board:'As9h3d',filter:{bdfd:'bdNut'},syntax:'(Kss,Ahh,Add)'}]};
  if(name==='plo_analyze_board'){const board=boardInfo(a.board),r=await this.calculate('analyze',a,signal);return {...board,street:{3:'flop',4:'turn',5:'river'}[r.street],totalLegalHands:r.total,roles:r.roles.map(x=>({key:x.key,label:x.label,category:x.cat,count:x.count})),ranks:r.ranks};}
  if(name==='plo_generate_ranges')return this.generate(a,signal);
  if(name==='plo_match_hands'){const board=boardInfo(a.board);let syntax=a.syntax;if(a.conditionId){const c=await this.store.get(a.conditionId,'condition');this.checkBoard(c,board);syntax=c.syntax;}return {...board,matches:await this.calculate('match',{...a,syntax},signal)};}
  if(name==='plo_register_dataset')return this.register(a,signal);
  if(name==='plo_list_datasets'){const key=a.board?boardInfo(a.board).boardKey:null,{offset,limit}=this.page(a),all=(await this.store.list('dataset')).filter(d=>!key||d.boardKey===key);return {offset,nextOffset:offset+limit<all.length?offset+limit:null,datasets:all.slice(offset,offset+limit).map(d=>this.datasetMetadata(d))};}
  if(name==='plo_start_compare')return this.startCompare(a);
  if(name==='plo_get_job')return this.getJob(a);
  if(name==='plo_cancel_job'){const j=await this.finish(a.jobId,'cancelled',new ApiError('JOB_CANCELLED','計算を中止しました。'));return {jobId:j.jobId,state:j.state,cancelRequested:j.state==='cancelled'};}
  if(name==='plo_export_result')return this.export(a);
  throw new ApiError('UNKNOWN_TOOL','未定義のツールです。');
 }
 async generate(a,signal){
  if(a.mode==='custom'&&a.requests.length>limits.maxRequestsPerGeneration)throw new ApiError('LIMIT_EXCEEDED','クラウド版は1生成10条件までです。');
  const board=boardInfo(a.board),page=a.mode==='defaults'?this.page(a):{offset:0,limit:a.requests.length};
  const all=await this.calculate('generate',a,signal),rows=[],records=[];
  for(const row of all.slice(page.offset,page.offset+page.limit)){
   if(row.syntax.length>50000)throw new ApiError('LIMIT_EXCEEDED','syntaxは50,000文字までです。');
   const conditionId=row.count?uuid():null,expiresAt=row.count?this.expiry():null;
   if(conditionId)records.push({...row,conditionId,boardKey:board.boardKey,classificationVersion:VERSION,expiresAt,sizeBytes:bytes(JSON.stringify(row))});
   rows.push({conditionId,cell:row.cell||null,label:row.label,filter:row.filter||null,count:row.count,syntax:row.count&&a.includeSyntax===false?null:row.syntax,status:row.status||(row.count?'MATCHES':row.problem==='このボードでは該当なし'?'EMPTY':'UNSUPPORTED'),reason:row.reason||row.problem||null,expiresAt:expiresAt?iso(expiresAt):null,examples:(row.examples||[]).slice(0,a.examplesLimit??0)});
  }
  const data={...board,mode:a.mode,totalRows:all.length,offset:page.offset,nextOffset:page.offset+page.limit<all.length?page.offset+page.limit:null,rows};
  this.checked('plo_generate_ranges',data);
  if(records.length){const r=await this.store.db.batch(records.map(c=>this.store.insertion('condition',c.conditionId,c,{count:512})));if(r.some(x=>x.meta.changes!==1)){await this.store.db.batch(records.map(c=>this.store.query('DELETE FROM plo_records WHERE owner=? AND id=?',this.store.owner,c.conditionId)));throw new ApiError('LIMIT_EXCEEDED','条件の保持上限に達しました。');}}
  return data;
 }
 datasetMetadata(d){return {datasetId:d.datasetId,name:d.name,label:d.label,boardKey:d.boardKey,rowCount:d.rowCount,sizeBytes:d.sizeBytes,totalWeight:d.totalWeight,expiresAt:iso(d.expiresAt)};}
 async register(a,signal){
  const board=boardInfo(a.board);if(/[\\/]/.test(a.name)||!a.name.trim()||!a.label.trim())throw new ApiError('INVALID_ARGUMENT','ファイル名・ラベルが不正です。');
  let text,upload;
  if(a.source.kind==='inline'){text=a.source.csvText;if(bytes(text)>limits.inlineCsvBytes)throw new ApiError('LIMIT_EXCEEDED','inline CSVは256KiBまでです。');}
  else if(a.source.kind==='upload'){upload=await this.store.get(a.source.uploadId,'upload');text=await (await this.store.blob(upload.blobKey)).text();}
  else throw new ApiError('PERMISSION_DENIED','クラウド版はPCのパスを読み取れません。CSVをアップロードしてください。');
  const sizeBytes=bytes(text);if(sizeBytes>limits.csvBytesPerFile)throw new ApiError('LIMIT_EXCEEDED','クラウド版のCSVは2MiBまでです。');
  const parsed=await this.calculate('dataset',{board:a.board,file:{name:a.name,label:a.label,text}},signal);
  if(parsed.rows.length>limits.maxDatasetRows)throw new ApiError('LIMIT_EXCEEDED','クラウド版はCSV単体50,000行までです。');
  const id=uuid(),record={...board,datasetId:id,name:a.name,label:a.label,rowCount:parsed.rows.length,totalWeight:parsed.totalWeight,sizeBytes,expiresAt:this.expiry(),blobKey:upload?.blobKey||this.store.owner+'/csv/'+id};
  if(upload){if(!await this.store.consumeUpload(id,record,upload))throw new ApiError('ID_NOT_FOUND','アップロードは使用済み・期限切れか、CSVの保持上限に達しています。');}
  else await this.persistBlob('dataset',id,record,text,{count:32});
  return this.datasetMetadata(record);
 }
 async upload(name,text){
  if(!this.store)throw new ApiError('PERMISSION_DENIED','サインインが必要です。');
  if(!name||name.length>256||/[\\/]/.test(name)||!name.toLowerCase().endsWith('.csv'))throw new ApiError('INVALID_ARGUMENT','CSVのファイル名を指定してください。');
  const sizeBytes=bytes(text);if(sizeBytes>limits.csvBytesPerFile)throw new ApiError('LIMIT_EXCEEDED','CSVは2MiBまでです。');
  const uploadId=uuid(),expiresAt=this.expiry(),record={uploadId,name,sizeBytes,expiresAt,blobKey:this.store.owner+'/csv/'+uploadId};
  await this.persistBlob('upload',uploadId,record,text,{count:8});
  return {uploadId,sizeBytes,expiresAt:iso(expiresAt)};
 }
 async startCompare(a){
  const board=boardInfo(a.board),conditionIds=a.conditionIds||[],includeAll=a.includeAll??true;
  if(conditionIds.length>limits.maxRanges)throw new ApiError('LIMIT_EXCEEDED','クラウド版は1比較20条件までです。');
  if(!conditionIds.length&&!includeAll)throw new ApiError('INVALID_SELECTION','条件または全ハンドを選択してください。');
  const signature=await sha256(JSON.stringify([board.boardKey,a.datasetIds,conditionIds,includeAll]));
  const resume=j=>{if(j.signature!==signature)throw new ApiError('REQUEST_KEY_CONFLICT','同じrequestKeyを異なる入力で使えません。');if(j.state==='queued')this.defer(this.runJob(j.jobId));return {jobId:j.jobId,state:j.state,expiresAt:iso(j.expiresAt)};};
  await this.store.expireRequest(a.requestKey);
  const previous=await this.store.request(a.requestKey);if(previous)return resume(previous);
  for(const old of await this.store.list('job'))if(!terminal(old.state)&&old.deadline<this.now())await this.finish(old.jobId,'failed',new ApiError('JOB_TIMEOUT','比較が時間制限を超えました。'));
  const datasets=await Promise.all(a.datasetIds.map(id=>this.store.get(id,'dataset'))),conditions=await Promise.all(conditionIds.map(id=>this.store.get(id,'condition')));
  for(const r of [...datasets,...conditions])this.checkBoard(r,board);
  if(new Set(datasets.map(d=>d.name)).size!==datasets.length)throw new ApiError('INVALID_SELECTION','同名のCSVを比較できません。');
  if(datasets.reduce((n,d)=>n+d.sizeBytes,0)>limits.csvBytesPerCompare)throw new ApiError('LIMIT_EXCEEDED','比較CSVは合計6MiBまでです。');
  const jobId=uuid(),j={jobId,...board,datasetIds:a.datasetIds,conditionIds,includeAll,signature,requestKey:a.requestKey,state:'queued',expiresAt:this.expiry(),deadline:this.now()+30000,sizeBytes:0};
  const r=await this.store.db.batch([this.store.insertion('job',jobId,j,{count:16,requestKey:a.requestKey,signature,state:'queued',activeJob:true,refs:[...a.datasetIds,...conditionIds]}),this.store.pinStatement([...a.datasetIds,...conditionIds],j.deadline+10000,jobId)]);
  if(r[0].meta.changes!==1){const duplicate=await this.store.request(a.requestKey);if(duplicate)return resume(duplicate);throw new ApiError('SERVER_BUSY','クラウド版は同時比較1件、保持16件までです。',{},true);}
  return resume(j);
 }
 async runJob(jobId){
  let resultKey;
  try{
   let job=await this.store.get(jobId,'job');if(job.state!=='queued')return;
   if(!await this.store.replace(jobId,'job',{...job,state:'running'},job.revision))return;
   const deadline=Date.now()+limits.jobTimeoutSeconds*1000;
   while(calculating){if(Date.now()>deadline)throw new ApiError('JOB_TIMEOUT','計算の待機が時間制限を超えました。');await new Promise(r=>setTimeout(r,20));}
   calculating=true;
   let flat=[],sources;
   try{
    sources=await Promise.all(job.datasetIds.map(id=>this.store.get(id,'dataset',{retained:true})));
    const datasets=[];for(const d of sources){const text=await (await this.store.blob(d.blobKey)).text();datasets.push(comparison.parseDataset(engine.parseBoard(job.boardKey),{name:d.name,label:d.label,text}));}
    const conditions=await Promise.all(job.conditionIds.map(id=>this.store.get(id,'condition',{retained:true})));
    const ranges=[...(job.includeAll?[null]:[]),...conditions];
    for(const condition of ranges){
     job=await this.store.get(jobId,'job');if(terminal(job.state))return;
     if(Date.now()>deadline||this.now()>job.deadline)throw new ApiError('JOB_TIMEOUT','比較が時間制限を超えました。');
     flat.push(...comparison.compare({board:job.boardKey,files:sources.map(d=>({name:d.name,label:d.label})),ranges:condition?[{cell:condition.cell||condition.conditionId,label:condition.label,syntax:condition.syntax}]:[],all:!condition},{datasets,percentAsString:true}));
    }
   }finally{calculating=false;}
   const rows=flat.map((r,i)=>({conditionId:job.includeAll&&i===0?null:job.conditionIds[i-(job.includeAll?1:0)],cell:r.cell,label:r.label,syntax:r.syntax,totalWeight:r.total_weight,status:r.status,sources:sources.map((d,k)=>{const key='csv'+(k+1);return {datasetId:d.datasetId,label:r[key+'_label'],sourceRows:r[key+'_source_rows'],matchedHands:r[key+'_matched_hands'],weightSum:r[key+'_weight_sum'],percent:r[key+'_percent']};})}));
   const text=JSON.stringify({flat,rows}),sizeBytes=bytes(text);
   if(sizeBytes+await this.store.byteSum('job')>limits.retainedResultsBytes)throw new ApiError('LIMIT_EXCEEDED','比較結果の保持上限8MiBに達しました。');
   resultKey=this.store.owner+'/results/'+jobId;await this.store.files.put(resultKey,text);
   const final=await this.finish(jobId,'succeeded',null,{resultKey,sizeBytes,rowCount:rows.length});
   if(final.state!=='succeeded')await this.store.files.delete(resultKey);
  }catch(error){try{const final=await this.finish(jobId,'failed',applicationError(error));if(resultKey&&final.state!=='succeeded')await this.store.files.delete(resultKey);}catch{console.error('PLO cloud: job state could not be saved');}}
 }
 async finish(jobId,state,error,extra={}){
  for(let attempt=0;attempt<5;attempt++){
   const j=await this.store.get(jobId,'job');if(terminal(j.state))return j;
   const next={...j,...extra,state,...(error?{failure:{code:error.code,message:error.message,retryable:!!error.retryable,details:error.details||{}}}:{})};
   if(await this.store.replace(jobId,'job',next,j.revision)){try{await this.store.release(next);}catch{console.error('PLO cloud: input pins will expire automatically');}return next;}
  }
  throw new ApiError('SERVER_BUSY','ジョブ状態が更新中です。再取得してください。',{},true);
 }
 async getJob(a){
  let j=await this.store.get(a.jobId,'job');if(!terminal(j.state)&&this.now()>j.deadline)j=await this.finish(a.jobId,'failed',new ApiError('JOB_TIMEOUT','比較が時間制限を超えました。'));
  const {offset,limit}=this.page(a);let result=null;
  if(j.state==='succeeded'){const body=JSON.parse(await (await this.store.blob(j.resultKey)).text());result={boardKey:j.boardKey,totalRows:body.rows.length,offset,nextOffset:offset+limit<body.rows.length?offset+limit:null,rows:body.rows.slice(offset,offset+limit),warnings:[]};}
  return {jobId:j.jobId,state:j.state,progress:{stage:j.state==='queued'?'queued':j.state==='running'?'comparison':'done',percent:terminal(j.state)?100:null},failure:j.failure||null,expiresAt:iso(j.expiresAt),result};
 }
 fileMetadata(f){return {fileId:f.fileId,fileName:f.fileName,mimeType:f.mimeType,sizeBytes:f.sizeBytes,sha256:f.sha256,expiresAt:iso(f.expiresAt),downloadUrl:new URL('/files/'+f.fileId,this.origin).href,resourceUri:'plo-result://'+f.fileId};}
 async export(a){
  const job=await this.store.get(a.jobId,'job');if(job.state!=='succeeded')throw new ApiError('INVALID_SELECTION','成功したジョブだけを出力できます。');
  const cached=(await this.store.list('file')).find(f=>f.jobId===a.jobId&&f.format===a.format);if(cached)return this.fileMetadata(cached);
  const result=JSON.parse(await (await this.store.blob(job.resultKey)).text());
  const text=a.format==='csv'?comparison.resultCsv(result.flat):JSON.stringify({apiVersion:'1.0.0',engine:this.engine,boardKey:job.boardKey,rows:result.rows},null,2);
  if(bytes(JSON.stringify({contents:[{uri:'plo-result://'+uuid(),mimeType:'application/json',text}]}))>limits.resourceResponseBytes)throw new ApiError('LIMIT_EXCEEDED','成果物は通信サイズ8MiBまでです。');
  const fileId=uuid(),record={fileId,jobId:job.jobId,format:a.format,fileName:'PLO_'+job.boardKey+'.'+a.format,mimeType:a.format==='csv'?'text/csv':'application/json',sizeBytes:bytes(text),sha256:await sha256(text),expiresAt:this.expiry(),blobKey:this.store.owner+'/exports/'+fileId};
  await this.persistBlob('file',fileId,record,text,{count:32},{httpMetadata:{contentType:record.mimeType}});
  return this.fileMetadata(record);
 }
 async persistBlob(kind,id,record,text,options,r2Options){
  await this.store.files.put(record.blobKey,text,r2Options);
  try{if(!await this.store.insert(kind,id,record,options))throw new ApiError('LIMIT_EXCEEDED','ファイルの保持上限に達しました。');}
  catch(error){
   // Do not delete a published blob after an ambiguous database acknowledgement.
   try{await this.store.get(id,kind);}catch(readError){if(readError.code==='ID_NOT_FOUND')await this.store.files.delete(record.blobKey);}
   throw error;
  }
 }
 async readResource(uri){const match=/^plo-result:\/\/([a-f0-9-]{36})$/.exec(uri);if(!match)throw new ApiError('ID_NOT_FOUND','結果リソースが見つかりません。');const f=await this.store.get(match[1],'file');return {contents:[{uri,mimeType:f.mimeType,text:new TextDecoder('utf-8',{ignoreBOM:true}).decode(await (await this.store.blob(f.blobKey)).arrayBuffer())}]};}
}
