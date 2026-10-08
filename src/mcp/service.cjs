'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),{Worker}=require('node:worker_threads');
const {randomUUID,createHash}=require('node:crypto'),{spawnSync}=require('node:child_process');
const P=require('../lab/engine.js'),C=require('../linux/compare.cjs'),{catalog,validate,ApiError}=require('./contracts.cjs');
const VERSION='2026-10-08-pair-draws-v1', MiB=1024*1024, terminal=s=>['succeeded','failed','cancelled'].includes(s);
const definitions={
 roles:'PLO4は手札2枚＋ボード3枚の最強役。role.keyはplo_analyze_boardから取得する。',
 fd:'ボードに対象スート2枚、手札に2枚以上。ナッツ・セカンドはボードを除いた最高・第2位のカード。',
 bdfd:'フロップ限定。ボードに対象スート1枚、手札に2枚以上。As9h3dのナッツ=(Kss,Ahh,Add)、セカンド=(Qss:!ks,Khh:!ah,Kdd:!ad)。',
 sd:'nutGut/nonGut/nutOpen/nonOpenは2枚組条件で重複する。T93は順に(KQ,KJ)、(Q8,J7,86,76)、QJ、(J8,87)。残り2枚のブロッカーで分類を昇格させない。全SD・ラップ・実アウト数は4枚ハンド全体。完成ストレートのリドローは除く。',
 syntax:'ランク・スート・除外・和集合・積集合で表す。生成式は全合法ハンドで内部照合する。Monker実機での受理・抽出一致は未確認。空集合は空文字で全ハンドではない。',
 csv:'hand/comboとweight列。重複・衝突・不正weightは拒否。weightは0〜1、小数28桁。各CSV該当weight÷選択CSV該当weight合計×100。比率は8桁切り捨て、合計0はnull。',
 limits:'CSV単体30MiB、1比較合計30MiB、inline256KiB、条件500件、1ページ最大100件。IDの有効期限は1時間。'
};
function boardInfo(text){let cs;try{cs=P.parseBoard(text);}catch(e){throw new ApiError('INVALID_BOARD',e.message);}return {normalizedBoard:cs.map(P.card),boardKey:cs.slice(0,3).sort((a,b)=>a-b).concat(cs.slice(3)).map(P.card).join('')};}
function version(){
 try{const v=require('./build-info.json').sourceCommit;if(/^[a-f0-9]{40}$/.test(v))return v;}catch{}
 const r=spawnSync('git',['rev-parse','HEAD'],{cwd:path.resolve(__dirname,'../..'),encoding:'utf8',timeout:1000,windowsHide:true});
 return /^[a-f0-9]{40}$/.test((r.stdout||'').trim())?r.stdout.trim():null;
}
class Service {
 constructor(options={}) {
  // Match fs.promises.realpath's native normalization, including Windows 8.3 aliases.
  this.options=options;this.allowReadRoots=(options.allowReadRoots||[]).map(root=>require('node:fs').realpathSync.native(root));this.now=options.now||Date.now;
  this.ttl=options.ttlMs??3600000;this.jobTimeout=options.jobTimeoutMs??300000;this.queueTimeout=options.queueTimeoutMs??30000;
  this.conditions=new Map();this.datasets=new Map();this.jobs=new Map();this.files=new Map();this.requests=new Map();this.active=new Set();this.queue=[];this.closed=false;
  this.engine={sourceCommit:options.sourceCommit||version(),classificationVersion:VERSION,game:'PLO4'};
  this.sweep=setInterval(()=>this.cleanup(),60000);this.sweep.unref();
 }
 expiry(){return this.now()+this.ttl;}
 iso(value){return new Date(value).toISOString();}
 tools(){return catalog.tools;}
 cleanup(){
  const now=this.now();
  for(const map of [this.conditions,this.datasets,this.files])for(const [id,v]of map)if(v.expiresAt<=now&&!v.pins)map.delete(id);
  for(const [id,j]of this.jobs)if(j.expiresAt<=now&&terminal(j.state)){this.jobs.delete(id);this.requests.delete(j.requestKey);}
 }
 get(map,id){const v=map.get(id);if(!v||(v.expiresAt<=this.now()&&!v.pins&&!(!terminal(v.state)&&v.state)))throw new ApiError('ID_NOT_FOUND','IDが見つからないか有効期限が切れています。');return v;}
 async call(name,args={},options={}) {
  this.cleanup();let result;
  try {
   if(this.closed)throw new ApiError('SERVER_BUSY','サーバーは終了処理中です。',{},true);
   if(Buffer.byteLength(JSON.stringify(args))>4*MiB)throw new ApiError('LIMIT_EXCEEDED','ツール引数は4MiBまでです。');
   validate(name,'input',args);
   const data=await this.dispatch(name,args,options);
   result={ok:true,apiVersion:'1.0.0',engine:this.engine,data,error:null,warnings:this.engine.sourceCommit?[]:['ビルド元コミットが不明です。ソース版またはビルド情報付き配布物を使ってください。']};
   const bytes=Buffer.byteLength(JSON.stringify({content:[{type:'text',text:JSON.stringify(result)}],structuredContent:result,isError:false}));if(bytes>256*1024)throw new ApiError('OUTPUT_TOO_LARGE','ページサイズ・条件数を減らすかincludeSyntax=falseを指定してください。');
   validate(name,'output',result);
  }catch(e){if(name==='plo_generate_ranges'&&result?.ok)for(const row of result.data.rows)if(row.conditionId)this.conditions.delete(row.conditionId);const api=e instanceof ApiError?e:new ApiError('INTERNAL_ERROR','内部処理に失敗しました。');result={ok:false,apiVersion:'1.0.0',engine:this.engine,data:null,error:{code:api.code,message:api.message,retryable:api.retryable,details:api.details},warnings:[]};}
  return result;
 }
 run(action,input,{signal,timeout=30000}={}) {
  if(signal?.aborted)return Promise.reject(new ApiError('JOB_CANCELLED','計算を中止しました。'));
  if(this.active.size>=2)return Promise.reject(new ApiError('SERVER_BUSY','別の計算が実行中です。',{},true));
  return new Promise((resolve,reject)=>{
   const worker=new Worker(path.join(__dirname,'worker.cjs'),{workerData:{action,input},resourceLimits:{maxOldGenerationSizeMb:512}});this.active.add(worker);
   let done=false;
   const finish=(err,result)=>{if(done)return;done=true;clearTimeout(timer);signal?.removeEventListener('abort',cancel);this.active.delete(worker);void worker.terminate();err?reject(err):resolve(result);queueMicrotask(()=>this.drain());};
   const cancel=()=>finish(new ApiError('JOB_CANCELLED','計算を中止しました。'));
   const timer=setTimeout(()=>finish(new ApiError('JOB_TIMEOUT','計算が時間制限を超えました。')),timeout);
   signal?.addEventListener('abort',cancel,{once:true});if(signal?.aborted)cancel();
   worker.once('message',v=>finish(v.error?new ApiError(v.error.code,v.error.message,v.error.details):null,v.result));
   worker.once('error',()=>finish(new ApiError('INTERNAL_ERROR','計算workerに異常が発生しました。')));
   worker.once('exit',()=>{if(!done)finish(new ApiError('INTERNAL_ERROR','計算workerが終了しました。'));});
  });
 }
 async dispatch(name,a,{signal}={}) {
  if(name==='plo_capabilities')return {supportedTransports:['stdio'],supportedProtocolVersions:['2025-11-25'],operations:catalog.tools.map(t=>t.name),scopes:['plo:analyze','plo:datasets','plo:compare'],sdkCompatibilityTested:true,limits:{csvBytesPerFile:30*MiB,csvBytesPerCompare:30*MiB,maxRanges:500,maxRequestsPerGeneration:100,maxConcurrentJobs:2,jobTimeoutSeconds:300,synchronousTimeoutSeconds:30,inlineCsvBytes:256*1024,responseBytes:256*1024,resourceResponseBytes:8*MiB,retainedResultsBytes:64*MiB,ttlSeconds:3600}};
  if(name==='plo_definitions')return {definitions:(a.topics||Object.keys(definitions)).map(topic=>({topic,text:definitions[topic]})),examples:[{board:'Ts9h3d',filter:{sd:'nutGut'},syntax:'(KQ,KJ)'},{board:'As9h3d',filter:{bdfd:'bdNut'},syntax:'(Kss,Ahh,Add)'}]};
  if(name==='plo_analyze_board') {
   const b=boardInfo(a.board),r=await this.run('analyze',a,{signal});return {...b,street:{3:'flop',4:'turn',5:'river'}[r.street],totalLegalHands:r.total,roles:r.roles.map(x=>({key:x.key,label:x.label,category:x.cat,count:x.count})),ranks:r.ranks};
  }
  if(name==='plo_generate_ranges') {
   const b=boardInfo(a.board),rows=await this.run('generate',a,{signal}),offset=a.mode==='defaults'?(a.offset??0):0,limit=a.mode==='defaults'?(a.limit??25):rows.length;
   const selected=rows.slice(offset,offset+limit),result=[];
   if(this.conditions.size+selected.filter(row=>row.count).length>4096)throw new ApiError('LIMIT_EXCEEDED','条件の登録上限に達しました。');
   if(selected.some(row=>row.syntax.length>50000))throw new ApiError('LIMIT_EXCEEDED','生成されたsyntaxが50,000文字を超えました。');
   for(const row of selected){
    const status=row.status||(row.count?'MATCHES':row.problem==='このボードでは該当なし'?'EMPTY':'UNSUPPORTED');
    let conditionId=null,expiresAt=null;
    if(row.count){conditionId=randomUUID();const r={...row,boardKey:b.boardKey,expiresAt:this.expiry(),classificationVersion:VERSION,pins:0};this.conditions.set(conditionId,r);expiresAt=this.iso(r.expiresAt);}
    result.push({conditionId,cell:row.cell||null,label:row.label,filter:row.filter||null,count:row.count,syntax:row.count&&a.includeSyntax===false?null:row.syntax,status,reason:row.reason||row.problem||null,expiresAt,examples:(row.examples||[]).slice(0,a.examplesLimit??0)});
   }
   return {...b,mode:a.mode,totalRows:rows.length,offset,nextOffset:offset+limit<rows.length?offset+limit:null,rows:result};
  }
  if(name==='plo_match_hands') {
   const b=boardInfo(a.board);let syntax=a.syntax;
   if(a.conditionId){const c=this.get(this.conditions,a.conditionId);this.checkBoard(c,b);syntax=c.syntax;}
   return {...b,matches:await this.run('match',{...a,syntax},{signal})};
  }
  if(name==='plo_register_dataset')return this.register(a,signal);
  if(name==='plo_list_datasets') {
   const key=a.board?boardInfo(a.board).boardKey:null,ds=[...this.datasets.values()].filter(d=>!key||d.boardKey===key),offset=a.offset??0,limit=a.limit??25;
   return {offset,nextOffset:offset+limit<ds.length?offset+limit:null,datasets:ds.slice(offset,offset+limit).map(d=>this.datasetMetadata(d))};
  }
  if(name==='plo_start_compare')return this.startCompare(a);
  if(name==='plo_get_job') {
   const j=this.get(this.jobs,a.jobId),offset=a.offset??0,limit=a.limit??25;
   return {jobId:j.jobId,state:j.state,progress:{stage:j.state==='queued'?'queued':j.state==='running'?'comparison':'done',percent:terminal(j.state)?100:null},failure:j.failure||null,expiresAt:this.iso(j.expiresAt),result:j.state==='succeeded'?{boardKey:j.boardKey,totalRows:j.rows.length,offset,nextOffset:offset+limit<j.rows.length?offset+limit:null,rows:j.rows.slice(offset,offset+limit),warnings:[]}:null};
  }
  if(name==='plo_cancel_job') {
   const j=this.get(this.jobs,a.jobId);if(terminal(j.state))return {jobId:j.jobId,state:j.state,cancelRequested:j.state==='cancelled'};
   j.controller.abort();if(j.state==='queued')this.finishJob(j,'cancelled',new ApiError('JOB_CANCELLED','計算を中止しました。'));
   return {jobId:j.jobId,state:j.state,cancelRequested:true};
  }
  if(name==='plo_export_result')return this.export(a);
  throw new ApiError('UNKNOWN_TOOL','未定義のツールです。');
 }
 checkBoard(record,b){if(record.boardKey!==b.boardKey)throw new ApiError('BOARD_MISMATCH','IDのボードが入力ボードと一致しません。');if(record.classificationVersion&&record.classificationVersion!==VERSION)throw new ApiError('ENGINE_VERSION_MISMATCH','条件のエンジン版が一致しません。');}
 datasetMetadata(d){return {datasetId:d.datasetId,name:d.name,label:d.label,boardKey:d.boardKey,rowCount:d.rows.length,sizeBytes:d.sizeBytes,totalWeight:d.totalWeight,expiresAt:this.iso(d.expiresAt)};}
 async register(a,signal) {
  const b=boardInfo(a.board);if(/[\\/]/.test(a.name)||!a.name.trim()||!a.label.trim())throw new ApiError('INVALID_ARGUMENT','ファイル名・ラベルが不正です。');
  let text;
  if(a.source.kind==='inline'){text=a.source.csvText;if(Buffer.byteLength(text)>256*1024)throw new ApiError('LIMIT_EXCEEDED','inline CSVは256KiBまでです。');}
  else if(a.source.kind==='local_file')text=await this.readLocal(a.source.path);
  else throw new ApiError('PERMISSION_DENIED','ローカルMCPではupload入力を提供していません。');
  const sizeBytes=Buffer.byteLength(text);if(sizeBytes>30*MiB)throw new ApiError('LIMIT_EXCEEDED','CSVは30MiBまでです。');
  const parsed=await this.run('dataset',{board:a.board,file:{name:a.name,label:a.label,text}},{signal});
  if([...this.datasets.values()].reduce((n,d)=>n+d.sizeBytes,0)+sizeBytes>100*MiB||this.datasets.size>=64)throw new ApiError('LIMIT_EXCEEDED','CSVの保持上限に達しました。');
  const d={...parsed,...b,datasetId:randomUUID(),sizeBytes,expiresAt:this.expiry(),pins:0};this.datasets.set(d.datasetId,d);return this.datasetMetadata(d);
 }
 async readLocal(requested) {
  try {
   const real=await fs.realpath(requested),roots=this.allowReadRoots;
   const inside=root=>{const rel=path.relative(root,real);return rel===''||(!rel.startsWith('..'+path.sep)&&rel!=='..'&&!path.isAbsolute(rel));};
   if(!roots.some(inside))throw new ApiError('PERMISSION_DENIED','許可フォルダー外のファイルです。');
   const handle=await fs.open(real,require('node:fs').constants.O_RDONLY|(require('node:fs').constants.O_NOFOLLOW||0));
   try {
    if(await fs.realpath(requested)!==real)throw new ApiError('PERMISSION_DENIED','ファイルの参照先が変わりました。');
    const stat=await handle.stat();if(!stat.isFile())throw new ApiError('PERMISSION_DENIED','通常のファイルを指定してください。');
    if(stat.size>30*MiB)throw new ApiError('LIMIT_EXCEEDED','CSVは30MiBまでです。');
    const chunks=[];let size=0;for await(const chunk of handle.createReadStream()){size+=chunk.length;if(size>30*MiB)throw new ApiError('LIMIT_EXCEEDED','CSVは30MiBまでです。');chunks.push(chunk);}
    try{return new TextDecoder('utf-8',{fatal:true}).decode(Buffer.concat(chunks));}catch{throw new ApiError('CSV_VALIDATION_ERROR','CSVはUTF-8が必要です。');}
   }finally{await handle.close();}
  }catch(e){if(e instanceof ApiError)throw e;throw new ApiError('PERMISSION_DENIED','指定ファイルを読み取れません。');}
 }
 startCompare(a) {
  const b=boardInfo(a.board),conditionIds=a.conditionIds||[],includeAll=a.includeAll??true;
  if(!conditionIds.length&&!includeAll)throw new ApiError('INVALID_SELECTION','条件または全ハンドを選択してください。');
  const signature=createHash('sha256').update(JSON.stringify([b.boardKey,a.datasetIds,conditionIds,includeAll])).digest('hex');
  const previous=this.requests.get(a.requestKey);if(previous){if(previous.signature!==signature)throw new ApiError('REQUEST_KEY_CONFLICT','同じrequestKeyを異なる入力で使えません。');const j=this.get(this.jobs,previous.jobId);return {jobId:j.jobId,state:j.state,expiresAt:this.iso(j.expiresAt)};}
  const datasets=a.datasetIds.map(id=>this.get(this.datasets,id)),conditions=conditionIds.map(id=>this.get(this.conditions,id));
  for(const r of [...datasets,...conditions])this.checkBoard(r,b);
  if(new Set(datasets.map(d=>d.name)).size!==datasets.length)throw new ApiError('INVALID_SELECTION','同名のCSVを比較できません。');
  if(datasets.reduce((n,d)=>n+d.sizeBytes,0)>30*MiB)throw new ApiError('LIMIT_EXCEEDED','比較するCSVは合計30MiBまでです。');
  if(this.jobs.size>=32||this.queue.length>=8)throw new ApiError('SERVER_BUSY','ジョブの保持・待機上限に達しました。',{},true);
  const jobId=randomUUID(),j={jobId,...b,state:'queued',expiresAt:this.expiry(),requestKey:a.requestKey,controller:new AbortController(),datasets,conditions,conditionIds,includeAll};
  for(const record of [...datasets,...conditions])record.pins++;this.jobs.set(jobId,j);this.requests.set(a.requestKey,{signature,jobId});this.queue.push(j);
  j.queueTimer=setTimeout(()=>{if(j.state==='queued')this.finishJob(j,'failed',new ApiError('JOB_TIMEOUT','待ち行列が時間制限を超えました。'));},this.queueTimeout);
  queueMicrotask(()=>this.drain());return {jobId,state:'queued',expiresAt:this.iso(j.expiresAt)};
 }
 drain(){
  if(this.closed)return;
  while(this.active.size<2&&this.queue.length){const j=this.queue.shift();if(j.state!=='queued')continue;clearTimeout(j.queueTimer);j.state='running';
   const input={board:j.boardKey,files:j.datasets.map(d=>({name:d.name,label:d.label})),datasets:j.datasets.map(d=>({name:d.name,label:d.label,rows:d.rows})),ranges:j.conditions.map((c,i)=>({cell:c.cell||j.conditionIds[i],label:c.label,syntax:c.syntax})),all:j.includeAll};
   this.run('compare',input,{signal:j.controller.signal,timeout:this.jobTimeout}).then(flat=>{
    const rows=flat.map((r,i)=>({conditionId:j.includeAll&&i===0?null:j.conditionIds[i-(j.includeAll?1:0)],cell:r.cell,label:r.label,syntax:r.syntax,totalWeight:r.total_weight,status:r.status,sources:j.datasets.map((d,k)=>{const key='csv'+(k+1);return {datasetId:d.datasetId,label:r[key+'_label'],sourceRows:r[key+'_source_rows'],matchedHands:r[key+'_matched_hands'],weightSum:r[key+'_weight_sum'],percent:r[key+'_percent']};})}));
    const resultBytes=Buffer.byteLength(JSON.stringify([flat,rows]));if([...this.jobs.values()].reduce((n,item)=>n+(item.resultBytes||0),0)+resultBytes>64*MiB){this.finishJob(j,'failed',new ApiError('LIMIT_EXCEEDED','比較結果の保持上限に達しました。条件数を減らしてください。'));return;}
    j.flat=flat;j.rows=rows;j.resultBytes=resultBytes;
    this.finishJob(j,'succeeded');
   },e=>this.finishJob(j,e.code==='JOB_CANCELLED'?'cancelled':'failed',e));
  }
 }
 finishJob(j,state,error){if(terminal(j.state))return;clearTimeout(j.queueTimer);this.queue=this.queue.filter(item=>item!==j);j.state=state;if(error)j.failure={code:error.code,message:error.message,retryable:!!error.retryable,details:error.details||{}};for(const record of [...j.datasets,...j.conditions])record.pins--;j.datasets=[];j.conditions=[];}
 export(a){
  const j=this.get(this.jobs,a.jobId);if(j.state!=='succeeded')throw new ApiError('INVALID_SELECTION','成功したジョブだけを出力できます。');
  const existing=[...this.files.values()].find(f=>f.jobId===j.jobId&&f.format===a.format);if(existing)return this.fileMetadata(existing);
  const text=a.format==='csv'?C.resultCsv(j.flat):JSON.stringify({apiVersion:'1.0.0',engine:this.engine,boardKey:j.boardKey,rows:j.rows},null,2);
  // Stay below the official stdio client's default 10MiB JSON-RPC read buffer.
  if(Buffer.byteLength(JSON.stringify({contents:[{uri:'plo-result://'+randomUUID(),mimeType:'application/json',text}]}))>8*MiB)throw new ApiError('LIMIT_EXCEEDED','結果リソースの通信上限8MiBを超えました。比較条件数を減らしてください。');
  const sizeBytes=Buffer.byteLength(text);if([...this.files.values()].reduce((n,f)=>n+f.sizeBytes,0)+sizeBytes>64*MiB)throw new ApiError('LIMIT_EXCEEDED','成果物の保持上限に達しました。');
  const f={fileId:randomUUID(),jobId:j.jobId,format:a.format,fileName:'PLO_'+j.boardKey+'.'+a.format,mimeType:a.format==='csv'?'text/csv':'application/json',text,sizeBytes,sha256:createHash('sha256').update(text).digest('hex'),expiresAt:this.expiry()};this.files.set(f.fileId,f);return this.fileMetadata(f);
 }
 fileMetadata(f){return {fileId:f.fileId,fileName:f.fileName,mimeType:f.mimeType,sizeBytes:f.sizeBytes,sha256:f.sha256,expiresAt:this.iso(f.expiresAt),downloadUrl:null,resourceUri:'plo-result://'+f.fileId};}
 readResource(uri){const match=/^plo-result:\/\/([a-f0-9-]{36})$/.exec(uri);if(!match)throw new ApiError('ID_NOT_FOUND','結果リソースが見つかりません。');const f=this.get(this.files,match[1]);return {contents:[{uri,mimeType:f.mimeType,text:f.text}]};}
 async close(){this.closed=true;clearInterval(this.sweep);for(const j of this.jobs.values())if(!terminal(j.state)){j.controller.abort();this.finishJob(j,'cancelled',new ApiError('JOB_CANCELLED','サーバーを終了しました。'));}await Promise.all([...this.active].map(w=>w.terminate()));this.active.clear();}
}
module.exports={Service,boardInfo};
