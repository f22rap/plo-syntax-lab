import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Miniflare,convertV4MiniflareOptions} from 'miniflare';
import {Client} from '@modelcontextprotocol/sdk/client/index.js';
import {StreamableHTTPClientTransport} from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import Module from 'node:module';
import engine from '../../src/lab/engine.js';
const folder=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),root=path.resolve(folder,'..');
// Match the build's dependency roots when directly testing shared CommonJS contracts.
process.env.NODE_PATH=[path.join(folder,'node_modules'),path.join(root,'node_modules'),process.env.NODE_PATH].filter(Boolean).join(path.delimiter);Module._initPaths();
const {CloudService,sha256}=await import('../service.mjs'),{Store}=await import('../store.mjs');
let hosted=false;try{hosted=!!JSON.parse(await fs.readFile(path.join(root,'.openai/hosting.json'),'utf8')).project_id;}catch{}
const output=path.join(root,hosted?'dist/server':'dist/cloud/server');
const runtime=new Miniflare(convertV4MiniflareOptions({modules:true,modulesRoot:output,scriptPath:path.join(output,'index.js'),compatibilityDate:'2026-10-06',d1Databases:['DB'],r2Buckets:['FILES']}));
let checks=0;const eq=(a,b,message)=>{assert.deepEqual(a,b,message);checks++;};
const pause=ms=>new Promise(r=>setTimeout(r,ms));
const clients=[];
const headers={'Content-Type':'application/json','Accept':'application/json,text/event-stream'};
const request=(url,options={})=>runtime.dispatchFetch('http://plo.test'+url,options);
async function clientFor(identity){
 const client=new Client({name:'plo-cloud-tests',version:'1'});
 const transport=new StreamableHTTPClientTransport(new URL('http://plo.test/mcp'),{fetch:async(input,init)=>{
  const r=new Request(input,init),h=new Headers(r.headers);if(identity)h.set('oai-authenticated-user-id',identity);
  return runtime.dispatchFetch(r.url,{method:r.method,headers:h,body:['GET','HEAD'].includes(r.method)?undefined:await r.arrayBuffer(),signal:r.signal});
 }});await client.connect(transport);clients.push(client);return client;
}
async function good(client,name,args={}){const r=await client.callTool({name,arguments:args});assert.equal(r.isError,false,JSON.stringify(r));eq(JSON.parse(r.content[0].text),r.structuredContent);return r.structuredContent.data;}
async function bad(client,name,args,code){const r=await client.callTool({name,arguments:args});eq(r.isError,true);eq(r.structuredContent.error.code,code);}
const register=(client,name,text,board='Ts9h3d')=>good(client,'plo_register_dataset',{board,name,label:name,source:{kind:'inline',csvText:text}});
async function waitJob(client,jobId){const end=Date.now()+20000;while(Date.now()<end){const j=await good(client,'plo_get_job',{jobId});if(['succeeded','failed','cancelled'].includes(j.state))return j;await pause(25);}throw Error('Cloud job did not finish');}
try{
 const db=await runtime.getD1Database('DB'),bucket=await runtime.getR2Bucket('FILES');
 for(const file of (await fs.readdir(path.join(folder,'drizzle'))).filter(f=>f.endsWith('.sql')).sort()){
  const sql=await fs.readFile(path.join(folder,'drizzle',file),'utf8');for(const statement of sql.split('--> statement-breakpoint'))await db.exec(statement.trim().replaceAll('\n',' '));
 }
 const anonymous=await clientFor(null),a=await clientFor('test-user-a'),b=await clientFor('test-user-b');
 eq((await anonymous.listTools()).tools.length,11);
 const caps=await good(anonymous,'plo_capabilities');eq(caps.supportedTransports,['streamable_http']);eq(caps.limits.maxRowsPerPage,10);eq(caps.limits.csvBytesPerFile,2*1024*1024);
 eq((await good(anonymous,'plo_definitions',{topics:['sd','bdfd']})).definitions.length,2);
 eq((await request('/mcp',{method:'POST',headers,body:JSON.stringify({jsonrpc:'2.0',id:3,method:'tools/call',params:{name:'plo_list_datasets',arguments:{}}})})).status,401);
 eq((await request('/uploads',{method:'POST',headers:{'Content-Type':'text/csv'},body:'hand,weight'})).status,401);
 eq((await request('/mcp',{method:'POST',headers:{...headers,Origin:'https://other.invalid'},body:'{}'})).status,403);
 eq((await request('/mcp')).status,405);
 const board=await good(a,'plo_analyze_board',{board:'Ts9h3d'});eq(board.totalLegalHands,211876);eq(board.boardKey,'3d9hTs');
 await bad(a,'plo_analyze_board',{board:'AsAs7s'},'INVALID_BOARD');
 const ranges=(await good(a,'plo_generate_ranges',{board:'Ts9h3d',mode:'custom',requests:['nutGut','nutOpen','nonGut','nonOpen'].map(sd=>({label:sd,filter:{sd}}))})).rows;
 eq(ranges.map(r=>r.syntax),['(KQ,KJ)','QJ','(Q8,J7,86,76)','(J8,87)']);
 const bdfd=(await good(a,'plo_generate_ranges',{board:'As9h3d',mode:'custom',requests:[{label:'N',filter:{bdfd:'bdNut'}},{label:'2N',filter:{bdfd:'bdSecond'}}]})).rows;
 eq(bdfd.map(r=>r.syntax),['(Kss,Ahh,Add)','(Qss:!ks,Khh:!ah,Kdd:!ad)']);
 const flush=(await good(a,'plo_generate_ranges',{board:'As9s3d',mode:'custom',requests:[{label:'N',filter:{fd:'fdNut'}},{label:'2N',filter:{fd:'fdSecond'}}]})).rows;eq(flush.map(r=>r.syntax),['Kss','Qss:!ks']);
 const defaults=await good(a,'plo_generate_ranges',{board:'JsTd7c',mode:'defaults',offset:180,limit:5});eq(defaults.totalRows,185);eq(defaults.rows.length,5);
 await bad(a,'plo_generate_ranges',{board:'Ts9h3d',mode:'defaults',limit:11},'LIMIT_EXCEEDED');
 await bad(a,'plo_generate_ranges',{board:'Ts9h3d',mode:'custom',requests:Array.from({length:11},()=>({label:'x',filter:{}}))},'LIMIT_EXCEEDED');
 const match=await good(a,'plo_match_hands',{board:'9hTs3d',conditionId:ranges[0].conditionId,hands:['KsQhAsAd','QsJhAsAd']});eq(match.matches.map(h=>h.matched),[true,false]);
 await bad(b,'plo_match_hands',{board:'Ts9h3d',conditionId:ranges[0].conditionId,hands:['KsQhAsAd']},'ID_NOT_FOUND');
 await bad(a,'plo_match_hands',{board:'AsKd7s',conditionId:ranges[0].conditionId,hands:['KsQhAcAd']},'BOARD_MISMATCH');
 const csv='hand,weight\nKsQhAsAd,0.1\nKsJhAcAh,0.2\n';
 const d1=await register(a,'check.csv',csv),d2=await register(a,'bet.csv','combo,weight\nKsQhAsAd,0.3\n'),d3=await register(a,'raise.csv','hand,weight\nKsQhAsAd,0.0000000000000000000000000001\n');
 eq(d1.totalWeight,'0.3');eq((await good(a,'plo_list_datasets')).datasets.length,3);eq((await good(b,'plo_list_datasets')).datasets.length,0);
 await bad(a,'plo_register_dataset',{board:'Ts9h3d',name:'bad.csv',label:'bad',source:{kind:'inline',csvText:'hand,weight\nKsQhAsAd,0.2\nAdAsQhKs,0.1\n'}},'CSV_VALIDATION_ERROR');
 await bad(a,'plo_register_dataset',{board:'Ts9h3d',name:'local.csv',label:'x',source:{kind:'local_file',path:'/tmp/private.csv'}},'PERMISSION_DENIED');
 const identity={'oai-authenticated-user-id':'test-user-a'};
 const uploadResponse=await request('/uploads',{method:'POST',headers:{...identity,'Content-Type':'text/csv','X-Plo-File-Name':'upload.csv'},body:csv});eq(uploadResponse.status,201);const upload=await uploadResponse.json();
 const uploadArgs={board:'Ts9h3d',name:'upload.csv',label:'Uploaded',source:{kind:'upload',uploadId:upload.uploadId}};
 await bad(b,'plo_register_dataset',uploadArgs,'ID_NOT_FOUND');
 eq((await good(a,'plo_register_dataset',uploadArgs)).totalWeight,'0.3');await bad(a,'plo_register_dataset',uploadArgs,'ID_NOT_FOUND');
 eq((await request('/uploads',{method:'POST',headers:{...identity,'Content-Type':'text/csv','X-Plo-File-Name':'large.csv'},body:'x'.repeat(2*1024*1024+1)})).status,413);
 eq((await request('/uploads',{method:'POST',headers:{...identity,'Content-Type':'application/json'},body:'{}'})).status,415);
 // Exercise the actual 50,000-row boundary using unique, board-legal hands.
 const deck=Array.from({length:52},(_,i)=>i).filter(i=>!engine.parseBoard('Ts9h3d').includes(i)),legal=[];
 outer:for(let i=0;i<deck.length-3;i++)for(let j=i+1;j<deck.length-2;j++)for(let k=j+1;k<deck.length-1;k++)for(let l=k+1;l<deck.length;l++){legal.push([deck[i],deck[j],deck[k],deck[l]].map(engine.card).join('')+',1');if(legal.length===50001)break outer;}
 async function uploadedRows(rows,name){const response=await request('/uploads',{method:'POST',headers:{...identity,'Content-Type':'text/csv','X-Plo-File-Name':name},body:'hand,weight\n'+rows.join('\n')+'\n'});eq(response.status,201);return {board:'Ts9h3d',name,label:name,source:{kind:'upload',uploadId:(await response.json()).uploadId}};}
 eq((await good(a,'plo_register_dataset',await uploadedRows(legal.slice(0,50000),'boundary.csv'))).rowCount,50000);
 await bad(a,'plo_register_dataset',await uploadedRows(legal,'excess-rows.csv'),'LIMIT_EXCEEDED');
 const args={board:'Ts9h3d',datasetIds:[d1.datasetId,d2.datasetId],conditionIds:[ranges[0].conditionId],includeAll:true,requestKey:'two'};
 const first=await good(a,'plo_start_compare',args);eq((await good(a,'plo_start_compare',{...args,board:'3d9hTs'})).jobId,first.jobId);
 await bad(a,'plo_start_compare',{...args,includeAll:false},'REQUEST_KEY_CONFLICT');
 const done=await waitJob(a,first.jobId);eq(done.state,'succeeded');eq(done.result.rows[1].sources.map(s=>s.percent),['50.00000000','50.00000000']);
 const three=await good(a,'plo_start_compare',{...args,datasetIds:[d1.datasetId,d2.datasetId,d3.datasetId],requestKey:'three'}),t=await waitJob(a,three.jobId);eq(t.state,'succeeded');eq(t.result.rows[0].totalWeight,'0.6000000000000000000000000001');eq(t.result.rows[0].sources[0].percent,'49.99999999');
 const out=await good(a,'plo_export_result',{jobId:first.jobId,format:'csv'}),resource=await a.readResource({uri:out.resourceUri});assert.ok(resource.contents[0].text.startsWith('\uFEFF'));checks++;
 const filePath=new URL(out.downloadUrl).pathname;const download=await request(filePath,{headers:identity});eq(download.status,200);eq(new TextDecoder('utf-8',{ignoreBOM:true}).decode(await download.arrayBuffer()),resource.contents[0].text);
 eq((await request(filePath,{headers:{'oai-authenticated-user-id':'test-user-b'}})).status,404);eq((await request(filePath)).status,401);
 eq((await a.listResources()).resources.length,1);eq((await b.listResources()).resources.length,0);
 const jsonOut=await good(a,'plo_export_result',{jobId:first.jobId,format:'json'});eq(JSON.parse((await a.readResource({uri:jsonOut.resourceUri})).contents[0].text).rows[0].totalWeight,'0.6');
 eq((await good(a,'plo_cancel_job',{jobId:first.jobId})).state,'succeeded');
 await bad(b,'plo_get_job',{jobId:first.jobId},'ID_NOT_FOUND');
 const z1=await register(a,'zero1.csv','hand,weight\nKsQhAsAd,0\n'),z2=await register(a,'zero2.csv','hand,weight\nKsQhAsAd,0\n');
 const zero=await good(a,'plo_start_compare',{board:'Ts9h3d',datasetIds:[z1.datasetId,z2.datasetId],requestKey:'zero'});const z=await waitJob(a,zero.jobId);eq(z.result.rows[0].status,'ZeroTotal');eq(z.result.rows[0].sources.map(s=>s.percent),[null,null]);
 // Recreate both HTTP client and service; state belongs to storage, not connections.
 const reconnected=await clientFor('test-user-a');eq((await good(reconnected,'plo_get_job',{jobId:first.jobId})).state,'succeeded');
 const owner=await sha256('test-user-a'),service=new CloudService({DB:db,FILES:bucket},owner,{origin:'http://plo.test',defer:()=>{}});
 async function queued(name,deadline=Date.now()+30000){const jobId=crypto.randomUUID(),record={jobId,boardKey:'3d9hTs',normalizedBoard:['Ts','9h','3d'],datasetIds:[d1.datasetId,d2.datasetId],conditionIds:[],includeAll:true,state:'queued',requestKey:name,signature:'test',expiresAt:Date.now()+3600000,deadline,sizeBytes:0};assert.ok(await service.store.insert('job',jobId,record,{requestKey:name,state:'queued',count:16}));return record;}
 const cancel=await queued('cancel');const c=await service.call('plo_cancel_job',{jobId:cancel.jobId});eq(c.data.state,'cancelled');await service.runJob(cancel.jobId);eq((await service.store.get(cancel.jobId,'job')).state,'cancelled');
 const race=await queued('race'),error={code:'JOB_TIMEOUT',message:'timeout',details:{}};const outcomes=await Promise.all([service.finish(race.jobId,'cancelled',error),service.finish(race.jobId,'failed',error)]);eq(outcomes[0].state,outcomes[1].state);eq((await service.store.get(race.jobId,'job')).revision,1);
 const timed=await queued('timed',Date.now()-1);eq((await service.call('plo_get_job',{jobId:timed.jobId})).data.failure.code,'JOB_TIMEOUT');
 const fork=new CloudService({DB:db,FILES:bucket},owner,{origin:'http://plo.test'});eq((await fork.call('plo_get_job',{jobId:first.jobId})).data.state,'succeeded');
 const expiredId=crypto.randomUUID();await service.store.insert('condition',expiredId,{conditionId:expiredId,expiresAt:Date.now()-1});eq((await service.call('plo_match_hands',{board:'Ts9h3d',conditionId:expiredId,hands:['KsQhAsAd']})).error.code,'ID_NOT_FOUND');
 await service.store.query('UPDATE plo_records SET retain_until=? WHERE owner=? AND id=?',Date.now()+10000,owner,expiredId).run();eq((await service.call('plo_match_hands',{board:'Ts9h3d',conditionId:expiredId,hands:['KsQhAsAd']})).error.code,'ID_NOT_FOUND');eq((await service.store.get(expiredId,'condition',{retained:true})).conditionId,expiredId);
 const quota=new Store(db,bucket,'quota-user');const success=await Promise.all([quota.insert('condition','one',{expiresAt:Date.now()+10000},{count:1}),quota.insert('condition','two',{expiresAt:Date.now()+10000},{count:1})]);eq(success.filter(Boolean).length,1);
 // An older completed job must not unpin a newly started comparison.
 const successor=await queued('successor');await service.store.pinStatement([d1.datasetId],Date.now()+40000,successor.jobId).run();await service.store.release(cancel);eq((await service.store.query('SELECT retain_until FROM plo_records WHERE owner=? AND id=?',owner,d1.datasetId).first()).retain_until>Date.now(),true);await service.finish(successor.jobId,'cancelled',error);
 // A storage failure after terminal CAS must preserve the published result.
 const pinFailure=await queued('release-failure');service.store.release=async()=>{throw Error('simulated D1 release failure');};eq((await service.finish(pinFailure.jobId,'succeeded',null,{resultKey:'fixture',rowCount:0})).state,'succeeded');
 const failedStore=new CloudService({DB:db,FILES:bucket},await sha256('write-failure'),{origin:'http://plo.test'});failedStore.store.insert=async()=>{throw Error('simulated failed insert');};
 await assert.rejects(failedStore.upload('failed.csv',csv));eq((await bucket.list({prefix:failedStore.store.owner+'/csv/'})).objects.length,0);
 const page=await request('/',{headers:identity});eq(page.status,200);const html=await page.text();assert.match(html,/id="csv"/);assert.match(html,/plo_cloud_register_upload/);eq(page.headers.get('cache-control'),'private, no-store');checks+=2;
 console.log(JSON.stringify({status:'passed',checks,tools:11,transport:'official Streamable HTTP client',runtime:'workerd',storage:'D1 + R2',isolation:'passed'}));
}finally{await Promise.allSettled(clients.map(c=>c.close()));await runtime.dispose();}
