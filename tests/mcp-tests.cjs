'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path');
const {createRequire}=require('node:module'),{Service}=require('../src/mcp/service.cjs'),{validate}=require('../src/mcp/contracts.cjs');
const mcpRequire=createRequire(require.resolve('../src/mcp/package.json'));
const {Client}=mcpRequire('@modelcontextprotocol/sdk/client/index.js');
const {StdioClientTransport}=mcpRequire('@modelcontextprotocol/sdk/client/stdio.js');
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
let checks=0;
function eq(a,b,msg){assert.deepEqual(a,b,msg);checks++;}
async function good(service,name,args={}){const r=await service.call(name,args);validate(name,'output',r);assert.equal(r.ok,true,JSON.stringify(r.error));checks++;return r.data;}
async function bad(service,name,args,code){const r=await service.call(name,args);validate(name,'output',r);eq(r.ok,false);eq(r.error.code,code);return r;}
async function waitJob(service,jobId){const deadline=Date.now()+15000;while(Date.now()<deadline){const j=await good(service,'plo_get_job',{jobId});if(['succeeded','failed','cancelled'].includes(j.state))return j;await pause(15);}throw Error('Job did not finish');}
const register=(service,name,text,board='Ts9h3d')=>good(service,'plo_register_dataset',{board,name,label:name,source:{kind:'inline',csvText:text}});
(async()=>{
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'plo-mcp-tests-')),allowed=path.join(temp,'allowed');await fs.mkdir(allowed);
 const service=new Service({allowReadRoots:[allowed]}),isolated=new Service();
 try {
  const caps=await good(service,'plo_capabilities');eq(caps.operations.length,11);eq(caps.supportedProtocolVersions,['2025-11-25']);
  eq((await good(service,'plo_definitions',{topics:['sd','bdfd']})).definitions.length,2);
  const b=await good(service,'plo_analyze_board',{board:'Ts9h3d'});eq(b.totalLegalHands,211876);eq(b.boardKey,'3d9hTs');
  await bad(service,'plo_analyze_board',{board:'AsAs7s'},'INVALID_BOARD');
  await bad(service,'plo_analyze_board',{board:'Ts9h3d',command:'calc'},'INVALID_ARGUMENT');
  const ranges=(await good(service,'plo_generate_ranges',{board:'Ts9h3d',mode:'custom',requests:[{label:'nutGut',filter:{sd:'nutGut'}},{label:'nutOpen',filter:{sd:'nutOpen'}},{label:'nonGut',filter:{sd:'nonGut'}},{label:'nonOpen',filter:{sd:'nonOpen'}}]})).rows;
  eq(ranges.map(r=>r.syntax),['(KQ,KJ)','QJ','(Q8,J7,86,76)','(J8,87)']);eq(ranges[0].examples,[]);
  const fd=(await good(service,'plo_generate_ranges',{board:'As9h3d',mode:'custom',requests:[{label:'N',filter:{bdfd:'bdNut'}},{label:'2N',filter:{bdfd:'bdSecond'}}]})).rows;
  eq(fd.map(r=>r.syntax),['(Kss,Ahh,Add)','(Qss:!ks,Khh:!ah,Kdd:!ad)']);
  const flush=(await good(service,'plo_generate_ranges',{board:'As9s3d',mode:'custom',requests:[{label:'N',filter:{fd:'fdNut'}},{label:'2N',filter:{fd:'fdSecond'}}]})).rows;
  eq(flush.map(r=>r.syntax),['Kss','Qss:!ks']);
  const defaults=await good(service,'plo_generate_ranges',{board:'JsTd7c',mode:'defaults',offset:180,limit:5});eq(defaults.totalRows,185);eq(defaults.rows.length,5);eq(defaults.nextOffset,null);
  await bad(service,'plo_generate_ranges',{board:'Ts9h3d',mode:'custom',requests:[{label:'x',filter:{role:'set:99'}}]},'INVALID_FILTER');
  await bad(service,'plo_generate_ranges',{board:'Ts9h3d',mode:'custom',requests:[{label:'x',filter:{clean:'regular',sd:'nutGut'}}]},'INVALID_FILTER');
  const empty=await good(service,'plo_generate_ranges',{board:'AsKd7s',mode:'custom',requests:[{label:'none',filter:{sd:'nonGut'}}]});eq([empty.rows[0].count,empty.rows[0].syntax,empty.rows[0].conditionId],[0,'',null]);
  const unsupported=await good(service,'plo_generate_ranges',{board:'AsKd7s2h',mode:'custom',requests:[{label:'bd',filter:{bdfd:'bdNut'}}]});eq(unsupported.rows[0].status,'UNSUPPORTED');
  const matched=await good(service,'plo_match_hands',{board:'9h3dTs',conditionId:ranges[0].conditionId,hands:['KsQhAsAd','QsJhAsAd']});eq(matched.matches.map(h=>h.matched),[true,false]);assert.ok(matched.matches[0].pairDraws.some(p=>p.classification==='nutGut'));checks++;
  await bad(service,'plo_match_hands',{board:'Ts9h3d',syntax:'??',hands:['KsQhAsAd']},'INVALID_SYNTAX');
  await bad(service,'plo_match_hands',{board:'Ts9h3d',syntax:'KQ',hands:['KsQhTsAd']},'INVALID_HAND');
  await bad(isolated,'plo_match_hands',{board:'Ts9h3d',conditionId:ranges[0].conditionId,hands:['KsQhAsAd']},'ID_NOT_FOUND');
  const csv='hand,weight\nKsQhAsAd,0.1\nKsJhAcAh,0.2\n';
  const d1=await register(service,'check.csv',csv),d2=await register(service,'bet.csv','combo,weight\nKsQhAsAd,0.3\n'),d3=await register(service,'raise.csv','hand,weight\nKsQhAsAd,0.0000000000000000000000000001\n');
  eq(d1.totalWeight,'0.3');eq((await good(service,'plo_list_datasets',{})).datasets.length,3);
  await bad(service,'plo_register_dataset',{board:'Ts9h3d',name:'bad.csv',label:'bad',source:{kind:'inline',csvText:'hand,weight\nKsQhAsAd,0.2\nAdAsQhKs,0.1\n'}},'CSV_VALIDATION_ERROR');
  await bad(service,'plo_register_dataset',{board:'Ts9h3d',name:'bad.csv',label:'bad',source:{kind:'inline',csvText:'hand,weight\nKsQhAsAd,1.1\n'}},'CSV_VALIDATION_ERROR');
  await bad(service,'plo_start_compare',{board:'AsKd7s',datasetIds:[d1.datasetId,d2.datasetId],requestKey:'wrong'},'BOARD_MISMATCH');
  const args={board:'Ts9h3d',datasetIds:[d1.datasetId,d2.datasetId],conditionIds:[ranges[0].conditionId],includeAll:true,requestKey:'two'};
  const job=await good(service,'plo_start_compare',args),same=await good(service,'plo_start_compare',{...args,board:'9hTs3d'});eq(job.jobId,same.jobId);
  await bad(service,'plo_start_compare',{...args,includeAll:false},'REQUEST_KEY_CONFLICT');
  const done=await waitJob(service,job.jobId);eq(done.state,'succeeded');eq(done.result.rows[1].sources.map(s=>s.weightSum),['0.3','0.3']);eq(done.result.rows[1].sources.map(s=>s.percent),['50.00000000','50.00000000']);
  const three=await good(service,'plo_start_compare',{...args,datasetIds:[d1.datasetId,d2.datasetId,d3.datasetId],requestKey:'three'}),done3=await waitJob(service,three.jobId);eq(done3.result.rows[0].totalWeight,'0.6000000000000000000000000001');eq(done3.result.rows[0].sources[0].percent,'49.99999999');
  const out=await good(service,'plo_export_result',{jobId:job.jobId,format:'csv'});const resource=service.readResource(out.resourceUri);assert.ok(resource.contents[0].text.startsWith('\uFEFF'));assert.match(resource.contents[0].text,/50\.00000000/);checks+=2;
  const json=await good(service,'plo_export_result',{jobId:job.jobId,format:'json'});eq(JSON.parse(service.readResource(json.resourceUri).contents[0].text).rows[1].totalWeight,'0.6');
  const z1=await register(service,'zero1.csv','hand,weight\nKsQhAsAd,0\n'),z2=await register(service,'zero2.csv','hand,weight\nKsQhAsAd,0\n');
  const zero=await good(service,'plo_start_compare',{board:'Ts9h3d',datasetIds:[z1.datasetId,z2.datasetId],requestKey:'zero'});const zd=await waitJob(service,zero.jobId);eq(zd.result.rows[0].status,'ZeroTotal');eq(zd.result.rows[0].sources.map(s=>s.percent),[null,null]);
  const local=path.join(allowed,'local.csv');await fs.writeFile(local,csv);
  eq((await good(service,'plo_register_dataset',{board:'Ts9h3d',name:'local.csv',label:'local',source:{kind:'local_file',path:local}})).totalWeight,'0.3');
  const outside=path.join(temp,'outside.csv');await fs.writeFile(outside,csv);
  await bad(service,'plo_register_dataset',{board:'Ts9h3d',name:'outside.csv',label:'outside',source:{kind:'local_file',path:outside}},'PERMISSION_DENIED');
  if(process.platform!=='win32'){const link=path.join(allowed,'link.csv');await fs.symlink(outside,link);await bad(service,'plo_register_dataset',{board:'Ts9h3d',name:'link.csv',label:'link',source:{kind:'local_file',path:link}},'PERMISSION_DENIED');}
  // Occupy both worker slots, then cancel a queued job deterministically.
  const busy1=service.call('plo_analyze_board',{board:'AsKd7s'}),busy2=service.call('plo_analyze_board',{board:'JsTd7c'});
  const cancel=await good(service,'plo_start_compare',{...args,requestKey:'cancel'});eq((await good(service,'plo_cancel_job',{jobId:cancel.jobId})).state,'cancelled');eq((await waitJob(service,cancel.jobId)).state,'cancelled');await Promise.all([busy1,busy2]);
  const running=await good(service,'plo_start_compare',{...args,requestKey:'running-cancel'});eq((await good(service,'plo_get_job',{jobId:running.jobId})).state,'running');eq((await good(service,'plo_cancel_job',{jobId:running.jobId})).cancelRequested,true);eq((await waitJob(service,running.jobId)).state,'cancelled');
  const expiring=new Service({ttlMs:1});try{const d=await register(expiring,'a.csv',csv);await pause(5);eq((await good(expiring,'plo_list_datasets',{})).datasets.length,0);await bad(expiring,'plo_start_compare',{board:'Ts9h3d',datasetIds:[d.datasetId,d2.datasetId],requestKey:'old'},'ID_NOT_FOUND');}finally{await expiring.close();}
  const timed=new Service({jobTimeoutMs:1});try{const t1=await register(timed,'a.csv',csv),t2=await register(timed,'b.csv',csv);const t=await good(timed,'plo_start_compare',{board:'Ts9h3d',datasetIds:[t1.datasetId,t2.datasetId],requestKey:'timeout'});const j=await waitJob(timed,t.jobId);eq(j.state,'failed');eq(j.failure.code,'JOB_TIMEOUT');}finally{await timed.close();}
 }finally{await service.close();await isolated.close();await fs.rm(temp,{recursive:true,force:true});}
 // Real official SDK client -> child process -> JSON-RPC -> tools/resources.
 const client=new Client({name:'plo-mcp-tests',version:'1.0.0'}),transport=new StdioClientTransport({command:process.execPath,args:[path.resolve(__dirname,'../src/mcp/stdio.cjs')],stderr:'pipe'});
 let stderr='';transport.stderr?.on('data',chunk=>stderr+=chunk);
 try{
  await client.connect(transport);const tools=await client.listTools();eq(tools.tools.length,11);
  const call=async(name,args={})=>{const r=await client.callTool({name,arguments:args});assert.equal(r.isError,false,JSON.stringify(r));eq(JSON.parse(r.content[0].text),r.structuredContent);return r.structuredContent.data;};
  eq((await call('plo_capabilities')).supportedTransports,['stdio']);
  eq((await call('plo_definitions',{topics:['sd']})).definitions[0].topic,'sd');
  eq((await call('plo_analyze_board',{board:'Ts9h3d'})).totalLegalHands,211876);
  const g=await call('plo_generate_ranges',{board:'Ts9h3d',mode:'custom',requests:[{label:'gut',filter:{sd:'nutGut'}}]});eq(g.rows[0].syntax,'(KQ,KJ)');
  eq((await call('plo_match_hands',{board:'Ts9h3d',conditionId:g.rows[0].conditionId,hands:['KsQhAsAd']})).matches[0].matched,true);
  const invalid=await client.callTool({name:'plo_analyze_board',arguments:{board:'AsAs7s'}});eq(invalid.isError,true);eq(invalid.structuredContent.error.code,'INVALID_BOARD');
  const ds=async name=>call('plo_register_dataset',{board:'Ts9h3d',name,label:name,source:{kind:'inline',csvText:'hand,weight\nKsQhAsAd,0.2\n'}});
  const d1=await ds('check.csv'),d2=await ds('bet.csv'),j=await call('plo_start_compare',{board:'Ts9h3d',datasetIds:[d1.datasetId,d2.datasetId],conditionIds:[g.rows[0].conditionId],requestKey:'stdio'});
  eq((await call('plo_list_datasets')).datasets.length,2);
  let done;for(let i=0;i<100;i++){done=await call('plo_get_job',{jobId:j.jobId});if(done.state==='succeeded')break;await pause(15);}eq(done.state,'succeeded');
  const file=await call('plo_export_result',{jobId:j.jobId,format:'csv'});const resource=await client.readResource({uri:file.resourceUri});assert.match(resource.contents[0].text,/check\.csv/);checks++;
  eq((await call('plo_cancel_job',{jobId:j.jobId})).state,'succeeded');
  eq((await client.listResources()).resources.length,1);eq(stderr,'','No unexpected stderr or protocol contamination');
 }finally{await client.close();await transport.close();}
 console.log(JSON.stringify({status:'passed',checks,tools:11,stdio:'official SDK client passed',resources:'passed'}));
})().catch(e=>{console.error(e);process.exitCode=1;});
