import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {setTimeout as sleep} from 'node:timers/promises';
import {Client} from '@modelcontextprotocol/client';
import {StdioClientTransport} from '@modelcontextprotocol/client/stdio';
import {root} from '../mcp/runtime.mjs';

test('MCP: real stdio tools, engine, native comparison, artifacts and history',{timeout:600000},async t=>{
 const testRoot=process.env.PLO_MCP_TEST_ROOT||path.join(root,'test-results');await fs.mkdir(testRoot,{recursive:true});
 const base=await fs.mkdtemp(path.join(testRoot,'mcp-'));
 const input=path.join(base,'input'),output=path.join(base,'output'),history=path.join(base,'history');await fs.mkdir(input);
 const configFile=path.join(base,'config.json');
 const config={inputRoots:[input],outputRoot:output,historyRoot:history,allowHistoryDelete:false,timeoutSeconds:180};await fs.writeFile(configFile,JSON.stringify(config));
 let client,transport;
 async function connect(){
  client=new Client({name:'plo-mcp-test',version:'1.0'});
  transport=new StdioClientTransport({command:process.execPath,args:[path.join(root,'mcp','server.mjs'),'--config',configFile],cwd:root,stderr:'pipe',env:process.env});
  transport.stderr.on('data',data=>process.stderr.write(data));await client.connect(transport);
 }
 async function call(name,args={}){const r=await client.callTool({name,arguments:args});if(r.isError)throw Error(r.content.map(c=>c.text||'').join('\n'));return r.structuredContent||JSON.parse(r.content[0].text);}
 async function finish(job){if(!job.job_id)return job;const until=Date.now()+180000;while(Date.now()<until){const j=await call('get_job',{job_id:job.job_id});if(j.state==='completed')return j.result;if(['failed','cancelled'].includes(j.state))throw Error(j.error||j.state);await sleep(100);}throw Error('Job timeout');}
 async function register(name,text,label=name){const p=path.join(input,name+'.csv');await fs.writeFile(p,text);return call('register_dataset',{path:p,label});}
 const atext='hand,frequency,weight\r\nJhJdAsKc,1,0.2\r\nJhJd9h8h,1,0.9\r\nThTsAhKh,1,0.3\r\n';
 await connect();
 try{
  const tools=await client.listTools();assert.equal(tools.tools.length,15);
  const status=await call('get_status');assert.equal(status.history_delete_enabled,false);assert.equal(status.monker_verified,false);assert.match(status.versions.source_commit,/^[a-f0-9]{40}$/);
  await finish(await call('list_conditions',{board:'JsTd7c'}));
  const conditions=await call('list_conditions',{board:'JsTd7c',limit:185});assert.equal(conditions.total,185);
  const top=conditions.conditions.find(c=>c.cell==='L49'),middle=conditions.conditions.find(c=>c.cell==='L61');assert.equal(top.syntax,'JJ!(98)');assert.equal(middle.count,3094);
  assert.equal((await call('list_conditions',{board:'JsTd7c',query:'Tsetall'})).conditions[0].condition_id,top.condition_id);
  assert.ok(conditions.conditions.some(c=>!c.available&&c.problem));
  const schema=await finish(await call('get_filter_schema',{board:'JsTd7c'}));assert.ok(schema.role_keys.includes('set:11'));
  const generated=await finish(await call('generate_syntax',{board:'JsTd7c',label:'生成トップセット',filter:{role:'set:11'}}));assert.equal(generated.syntax,top.syntax);assert.equal(generated.count,3103);
  await assert.rejects(()=>call('generate_syntax',{board:'JsTd7c',label:'bad',filter:{fd:'typo'}}));
  const invalidRole=await call('generate_syntax',{board:'JsTd7c',label:'bad',filter:{role:'not-a-role'}});await assert.rejects(()=>finish(invalidRole),/Unknown role/);
  for(const [board,key,value,syntax] of [
   ['Ts9h3d','sd','nutGut','(KQ,KJ)'],['Ts9h3d','sd','nutOpen','QJ'],
   ['Ts9h3d','sd','nonGut','(Q8,J7,86,76)'],['Ts9h3d','sd','nonOpen','(J8,87)'],
   ['As9h3d','bdfd','bdNut','(Kss,Ahh,Add)'],['As9h3d','bdfd','bdSecond','(Qss:!ks,Khh:!ah,Kdd:!ad)'],
   ['As9s3d','fd','fdNut','Kss'],['As9s3d','fd','fdSecond','Qss:!ks']
  ])assert.equal((await finish(await call('generate_syntax',{board,label:value,filter:{[key]:value}}))).syntax,syntax);
  assert.equal((await call('list_conditions',{board:'7cJsTd',query:'Tsetall'})).conditions[0].condition_id,top.condition_id);
  const a=await register("A's",atext,'Check 日本語');
  const b=await register('B','combo,weight\r\nJhJdAsKc,0.6\r\nJhJd9h8h,0.1\r\nThTsAhKh,0.1\r\n','Bet <script>');
  const c=await register('C','hand,weight\r\nJhJdAsKc,0.2\r\n','Raise');
  assert.equal((await call('list_datasets')).total,3);
  const comparison={board:'JsTd7c',dataset_ids:[a.dataset_id,b.dataset_id],condition_ids:[top.condition_id,middle.condition_id],include_all:true};
  const saved=await finish(await call('start_comparison',{...comparison,board:'7cJsTd'}));
  const result=await call('get_result',{result_id:saved.result_id});assert.equal(result.rows.length,3);assert.equal(result.rows[1].sources[0].weight_sum,'0.2');assert.equal(result.rows[1].sources[0].percent,'25.00');assert.equal(result.rows[2].sources[0].percent,'75.00');assert.equal(result.rows[0].sources[0].weight_sum,'1.4');assert.equal(result.provenance.datasets[0].sha256,a.sha256);
  const three=await finish(await call('start_comparison',{...comparison,dataset_ids:[a.dataset_id,b.dataset_id,c.dataset_id],condition_ids:[generated.condition_id],include_all:false}));
  const threeResult=await call('get_result',{result_id:three.result_id});assert.equal(threeResult.rows.length,1);assert.equal(Number(threeResult.rows[0].sources[2].percent),20);
  for(const format of ['csv','svg','json','markdown','powershell']){
   const artifact=await call('export_result',{result_id:saved.result_id,format});assert.ok((await fs.stat(artifact.path)).size>0);
   const inline=await call('get_artifact',{artifact_id:artifact.artifact_id});assert.ok(inline.text.length>0);
   const resource=await client.readResource({uri:artifact.uri});assert.equal(resource.contents[0].text,inline.text);
   if(format==='svg'){assert.ok(inline.text.includes('Bet &lt;script&gt;'));assert.ok(!inline.text.includes('Bet <script>'));}
   if(format==='powershell')assert.ok(inline.text.includes('PloSyntax.SyntaxMatcher'));
  }
  const report=await call('save_report',{result_id:saved.result_id,narrative:'トップセットのCheck比率は25%です。'});assert.match(await fs.readFile(report.path,'utf8'),/25%/);
  const before=(await call('list_history')).total;assert.equal(before,2);
  const compareCancel=await call('start_comparison',comparison);
  await call('cancel_job',{job_id:compareCancel.job_id});await assert.rejects(()=>finish(compareCancel),/Cancelled|cancelled/);
  const cancelled=await call('generate_syntax',{board:'AsKd7s5h',label:'cancel',filter:{role:'all'}});await call('cancel_job',{job_id:cancelled.job_id});await assert.rejects(()=>finish(cancelled),/Cancelled|cancelled/);
  assert.equal((await call('list_history')).total,before);
  const zero1=await register('zero1','hand,weight\nJhJdAsKc,0\n'),zero2=await register('zero2','hand,weight\nThTsAhKh,0\n');
  const zero=await finish(await call('start_comparison',{...comparison,dataset_ids:[zero1.dataset_id,zero2.dataset_id]}));
  assert.ok((await call('get_result',{result_id:zero.result_id})).rows.every(r=>r.sources.every(s=>s.percent===null)));
  const bad=await register('bad','hand,weight\nJhJdAsKc,2\n');
  await assert.rejects(()=>finishCall('start_comparison',{...comparison,dataset_ids:[bad.dataset_id,b.dataset_id]}),/invalid weight/);
  const dup=await register('duplicate','hand,weight\nJhJdAsKc,0.1\nKcAsJdJh,0.2\n');
  await assert.rejects(()=>finishCall('start_comparison',{...comparison,dataset_ids:[dup.dataset_id,b.dataset_id]}),/duplicate hand/);
  const collision=await register('collision','hand,weight\nJsJdAsKc,0.1\n');
  await assert.rejects(()=>finishCall('start_comparison',{...comparison,dataset_ids:[collision.dataset_id,b.dataset_id]}),/board card/);
  await assert.rejects(()=>call('start_comparison',{...comparison,dataset_ids:[a.dataset_id,a.dataset_id]}),/same CSV/);
  await assert.rejects(()=>call('start_comparison',{...comparison,board:'AsKd7s'}),/different board/);
  await fs.appendFile(a.path,'AhAdKsQc,0.1\n');
  await assert.rejects(()=>finishCall('start_comparison',comparison),/changed since registration/);
  const outside=path.join(base,'outside.csv');await fs.writeFile(outside,atext);
  await assert.rejects(()=>call('register_dataset',{path:outside,label:'outside'}),/outside/);
  await assert.rejects(()=>call('delete_history',{result_id:saved.result_id,confirm:saved.result_id}),/disabled/);
  assert.equal((await call('list_history')).total,3);
  const preciseValue='0.1234567890123456789012345678';
  const precise=await register('precise','hand,weight\nJhJdAsKc,'+preciseValue+'\n');
  const preciseRun=await finish(await call('start_comparison',{board:'JsTd7c',dataset_ids:[precise.dataset_id,b.dataset_id],condition_ids:[],include_all:true}));
  assert.equal((await call('get_result',{result_id:preciseRun.result_id})).rows[0].sources[0].weight_sum,preciseValue);
  await assert.rejects(()=>call('start_comparison',{...comparison,condition_ids:[conditions.conditions.find(r=>!r.available).condition_id]}),/Unavailable/);
  for(const board of ['AsKd7s','AsKd7s5h','AsKd7s5h9c']){
   const condition=await finish(await call('generate_syntax',{board,label:'AA',filter:{role:'set:14',...(board.length===6?{fd:'fdSecond'}:{})}}));
   assert.ok(condition.count>0);if(board.length===6)assert.equal(condition.count,27);
  }
  await client.close();
  config.allowHistoryDelete=true;await fs.writeFile(configFile,JSON.stringify(config));await connect();
  assert.equal((await call('get_result',{result_id:saved.result_id})).rows[1].sources[0].weight_sum,'0.2');
  await assert.rejects(()=>call('delete_history',{result_id:saved.result_id,confirm:three.result_id}),/exact/);
  await call('delete_history',{result_id:saved.result_id,confirm:saved.result_id});
  assert.equal((await call('list_history')).total,3);assert.ok((await fs.stat(a.path)).isFile());assert.ok((await fs.stat(report.path)).isFile());
  console.log('MCP integration passed: '+base);
  async function finishCall(name,args){return finish(await call(name,args));}
 }finally{await client.close();}
});
