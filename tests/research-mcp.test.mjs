import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {setTimeout as sleep} from 'node:timers/promises';
import {Client} from '@modelcontextprotocol/client';
import {StdioClientTransport} from '@modelcontextprotocol/client/stdio';
import {root} from '../mcp/runtime.mjs';
import {researchToolNames} from '../mcp/research-tools.mjs';
import {makeDirectoryFixture,directoryScenario} from './directory-fixture.mjs';
import {checkConnection} from '../mcp/check-connection.mjs';

test('Research MCP: real SDK transport, shared settings, immutable plans, numerical results, cancellation and scope',{timeout:180000},async()=>{
 const base=await fs.mkdtemp(path.join(process.env.PLO_MCP_TEST_ROOT||path.join(root,'test-results'),'research-mcp-'));
 const input=path.join(base,'input'),output=path.join(base,'output'),history=path.join(base,'history');await fs.mkdir(input);
 const configFile=path.join(base,'config.json');await fs.writeFile(configFile,JSON.stringify({inputRoots:[input],outputRoot:output,historyRoot:history,timeoutSeconds:120}));
 let client,transport;
 async function connect(){client=new Client({name:'plo-research-integration',version:'1'});const args=process.platform==='win32'?[path.join(root,'mcp/server.mjs'),'--config',configFile]:[path.join(root,'tests/research-harness.mjs'),configFile];transport=new StdioClientTransport({command:process.execPath,args,env:process.env,stderr:'pipe',cwd:root});transport.stderr.on('data',d=>process.stderr.write(d));await client.connect(transport);}
 async function call(name,args={}){const r=await client.callTool({name,arguments:args});if(r.isError)throw Error(r.content.map(c=>c.text||'').join('\n'));return r.structuredContent||JSON.parse(r.content[0].text);}
 async function terminal(job){for(let n=0;n<2000;n++){const j=await call('research_get_job',{job_id:job.job_id});if(!['queued','running'].includes(j.state))return j;await sleep(15);}throw Error('Timed out waiting for job.');}
 async function finish(job){const j=await terminal(job);if(j.state!=='completed')throw Error(j.error||j.state);return j.result;}
 const settings={Trials:1000,Seed:7123,GroupBy:'pair'};
 const condition=(id,syntax)=>({condition_id:id,label:id,enabled:true,kind:'literal',syntax,by_board:{}});
 let set={set_id:'user-set',revision:0,name:'User syntax',conditions:[condition('all','*'),condition('none','KK')]};
 let catalog={catalog_id:'registered-boards',revision:0,name:'Synthetic Ahi sample',scenario:{game:'PLO4',players:2,hero_position:'BB',villain_position:'BTN',effective_stack_bb:'100',pot_type:'SRP',action_history:'BTN open, BB call',solution_context:'synthetic only',equity_reference:'Before both actions'},spots:[]};
 for(const [id,board,c,b] of [['one','AsKd7c',.01,.03],['two','Ah8h3c',.75,.25],['nonace','KsQd7c',.4,.6]]){
  const csv=async(name,weight,hand='AcAd4c5d')=>{const p=path.join(input,id+'_'+name+'.csv');await fs.writeFile(p,'hand,frequency,percent,weight,ev,ev_diff,variations\n'+hand+',1,100,'+weight+',1.24,.29,1\n');return {path:p};};
  catalog.spots.push({spot_id:id,board,node:'BB first',hero_actions:[{action_id:'check',label:'Check',kind:'check',pot_fraction:'',source:await csv('check',c)},{action_id:'bet',label:'Bet 33%',kind:'bet',pot_fraction:'.33',source:await csv('bet',b)}],opponent_sources:[await csv('villain1',.25,'KcKh6h9d'),await csv('villain2',.75,'JdTh6c9s')],opponent_percent_unit:false,all_actions_common_scale:true,opponent_common_scale:true});
 }
 await connect();
 try{
  const names=(await client.listTools()).tools.map(t=>t.name);assert.equal(names.length,30);for(const name of researchToolNames)assert.ok(names.includes(name));for(const name of ['start_comparison','get_result','get_artifact'])assert.ok(names.includes(name));
  const diagnostic=await checkConnection({args:process.platform==='win32'?[path.join(root,'mcp/server.mjs'),'--config',configFile]:[path.join(root,'tests/research-harness.mjs'),configFile]});assert.equal(diagnostic.tool_count,30);assert.equal(diagnostic.mcp_connection,'passed');assert.equal(diagnostic.ai_host_connection,'not_tested');
  assert.equal((await call('research_list_workspace')).workspace_revision,0);
  set=(await call('research_save_condition_set',{condition_set:set,expected_workspace_revision:0})).condition_set;assert.equal(set.revision,1);assert.equal(set.conditions[0].syntax,'*');
  await assert.rejects(()=>call('research_save_condition_set',{condition_set:set,expected_workspace_revision:0}),/updated|更新|changed/);
  catalog=(await call('research_save_catalog',{catalog,expected_workspace_revision:1})).catalog;assert.equal(catalog.revision,1);assert.equal(catalog.spots[0].opponent_hands,2);assert.match(catalog.spots[0].hero_actions[0].source.sha256,/^[a-f0-9]{64}$/);
  const workspace=await call('research_list_workspace',{limit:1});assert.equal(workspace.workspace_revision,2);assert.equal(workspace.catalogs.items[0].spot_count,3);
  const listed=await call('research_list_spots',{catalog_id:catalog.catalog_id,limit:1});assert.equal(listed.total,2);assert.equal(listed.next_offset,1);assert.equal((await call('research_list_spots',{catalog_id:catalog.catalog_id,ahi_only:false})).total,3);
  assert.deepEqual((await call('research_get_condition_set',{set_id:set.set_id})).condition_set,set);
  const request=()=>({catalog_id:catalog.catalog_id,catalog_revision:catalog.revision,set_id:set.set_id,set_revision:set.revision,settings});
  const plan=await finish(await call('research_validate',request()));assert.equal(plan.target_spots,2);assert.equal(plan.estimated_trials,4000);assert.equal(plan.resolved.one[0].Syntax,'*');assert.equal(plan.features.two.Suit,'ツートーン');
  const started=await call('research_start',{plan_id:plan.plan_id,request_key:'first'});assert.equal((await call('research_start',{plan_id:plan.plan_id,request_key:'first'})).job_id,started.job_id);
  const result=await finish(started);assert.equal(result.completed_spots,2);assert.equal(result.filtered_spots,1);assert.equal(result.status_counts.completed,2);assert.equal(result.status_counts.no_matching_hands,2);
  const rows=await call('research_get_result',{result_id:result.result_id,view:'board'});assert.equal(rows.total,4);const one=rows.items.find(r=>r.SpotId==='one'&&r.ConditionId==='all'),two=rows.items.find(r=>r.SpotId==='two'&&r.ConditionId==='all');assert.equal(one.Actions[0].Frequency,25);assert.equal(two.Actions[0].Frequency,75);assert.ok(one.Equity.Equity>0&&one.Equity.Equity<100);assert.equal(rows.items.find(r=>r.ConditionId==='none').Equity,null);assert.equal(rows.items.find(r=>r.ConditionId==='none').Actions[0].Frequency,null);
  const group=await call('research_get_result',{result_id:result.result_id,view:'category'});const check=group.items.find(x=>x.ConditionId==='all'&&x.ActionKey==='check');assert.equal(check.FrequencyMean,50);assert.equal(check.PairedCount,2);assert.equal(check.FrequencyMin,25);assert.equal(check.FrequencyMax,75);
  const meta=await call('research_get_result',{result_id:result.result_id,view:'metadata'});assert.equal(meta.settings.Trials,1000);assert.deepEqual(meta.condition_set,set);assert.equal(meta.catalog.spots[0].hero_actions[0].source.sha256,catalog.spots[0].hero_actions[0].source.sha256);
  assert.equal((await call('research_get_result',{result_id:result.result_id,limit:1})).next_offset,1);assert.equal((await call('research_get_result',{result_id:result.result_id,spot_id:'one'})).total,2);
  for(const format of ['csv','json','svg','markdown']){const a=await call('research_export',{result_id:result.result_id,format,grouped:true});const text=(await call('get_artifact',{artifact_id:a.artifact_id})).text;assert.ok(text.length>20);assert.equal((await client.readResource({uri:a.uri})).contents[0].text,text);if(format==='json')assert.equal(JSON.parse(text).Rows.length,4);if(format==='svg')assert.match(text,/same paired observations/);}
  const bad=structuredClone(set);bad.conditions[0].syntax='(';await assert.rejects(()=>call('research_save_condition_set',{condition_set:bad,expected_workspace_revision:2}));assert.equal((await call('research_list_workspace')).workspace_revision,2);
  const partial=structuredClone(set);partial.conditions[0]={...condition('all',''),kind:'per_board',by_board:{AsKd7c:'*'}};set=(await call('research_save_condition_set',{condition_set:partial,expected_workspace_revision:2})).condition_set;
  await assert.rejects(()=>finishCall('research_validate',request()),/未設定/);
  const pp=await finish(await call('research_validate',{...request(),settings:{...settings,AllowMissingDefinitions:true}}));const pr=await finish(await call('research_start',{plan_id:pp.plan_id,request_key:'partial'}));assert.equal(pr.status,'completed_with_gaps');assert.equal(pr.status_counts.missing_definition,1);
  await assert.rejects(()=>call('research_start',{plan_id:pp.plan_id,request_key:'first'}),/different inputs/);
  // A previously validated plan may not silently adopt edited user definitions.
  await assert.rejects(()=>finishCall('research_start',{plan_id:plan.plan_id,request_key:'stale-set'}),/changed/);
  set=(await call('research_save_condition_set',{condition_set:{...set,conditions:[condition('all','*'),condition('none','KK')]},expected_workspace_revision:3})).condition_set;
  const changedPlan=await finish(await call('research_validate',request()));const source=catalog.spots[0].hero_actions[0].source.path,original=await fs.readFile(source,'utf8');await fs.appendFile(source,'\n');await assert.rejects(()=>finishCall('research_start',{plan_id:changedPlan.plan_id,request_key:'changed-source'}),/変更/);await fs.writeFile(source,original);
  const outside=path.join(base,'outside.csv');await fs.writeFile(outside,original);const badCatalog=structuredClone(catalog);badCatalog.spots[0].hero_actions[0].source.path=outside;await assert.rejects(()=>call('research_save_catalog',{catalog:badCatalog,expected_workspace_revision:4}),/outside/);
  const alias=path.join(input,'alias.csv');try{await fs.symlink(outside,alias);badCatalog.spots[0].hero_actions[0].source.path=alias;await assert.rejects(()=>call('research_save_catalog',{catalog:badCatalog,expected_workspace_revision:4}),/outside|canonical/);}catch(e){if(e.code!=='EPERM')throw e;}
  await assert.rejects(()=>call('research_get_result',{result_id:'../outside'}));await assert.rejects(()=>call('research_validate',{...request(),settings:{Trials:2000001}}));
  // A missing opponent is explicit, optional frequency data is not EQ zero.
  const noOpponent=structuredClone(catalog);noOpponent.spots[1].opponent_sources=[];catalog=(await call('research_save_catalog',{catalog:noOpponent,expected_workspace_revision:4})).catalog;
  await assert.rejects(()=>finishCall('research_validate',request()),/相手/);
  const fp=await finish(await call('research_validate',{...request(),settings:{...settings,AllowFrequencyOnly:true}}));const fr=await finish(await call('research_start',{plan_id:fp.plan_id,request_key:'frequency-only'}));assert.equal(fr.status_counts.frequency_only,1);
  const fg=await call('research_get_result',{result_id:fr.result_id,view:'category'});const fc=fg.items.find(x=>x.ActionKey==='check'&&x.ConditionId==='all');assert.equal(fc.FrequencyMean,50);assert.equal(fc.PairedFrequency,25);assert.equal(fc.PairedCount,1);
  // Saved results are snapshots and are readable after server restart.
  await client.close();await connect();assert.equal((await call('research_get_result',{result_id:result.result_id,view:'metadata'})).condition_set.revision,1);assert.ok((await call('research_list_history')).items.some(x=>x.result_id===result.result_id));await assert.rejects(()=>call('research_start',{plan_id:plan.plan_id,request_key:'first'}),/Unknown plan/);
  // Cancel during numerical work (not just a queued job).
  const cp=await finish(await call('research_validate',{...request(),settings:{...settings,Trials:2000000,AllowFrequencyOnly:true}}));const cj=await call('research_start',{plan_id:cp.plan_id,request_key:'cancel'});for(let i=0;i<100;i++){const j=await call('research_get_job',{job_id:cj.job_id});if(j.progress?.Message?.includes('計算中'))break;await sleep(10);}await call('research_cancel_job',{job_id:cj.job_id});const cancelled=await terminal(cj);assert.equal(cancelled.state,'cancelled');assert.ok(cancelled.result?.result_id);assert.equal(cancelled.result.status,'cancelled');assert.ok(cancelled.result.completed_spots<2);assert.equal((await call('research_get_result',{result_id:cancelled.result.result_id,view:'metadata'})).status,'cancelled');
  // The user's directory profile is imported through real MCP into the shared C# store.
  const tree=path.join(input,'COvsBTN');await makeDirectoryFixture(tree);
  const scanJob=await finish(await call('research_scan_directory',{directory:tree}));assert.equal(scanJob.board_count,2);assert.equal(scanJob.non_ahi_count,2);
  const nodes=await call('research_get_directory_scan',{scan_id:scanJob.scan_id});assert.equal(nodes.total,10);const node=nodes.items.find(n=>n.folder==='vs_check');
  const preview=await call('research_get_directory_scan',{scan_id:scanJob.scan_id,view:'spots',node_id:node.node_id,limit:1});assert.equal(preview.next_offset,1);assert.equal(preview.items[0].spot.opponent_sources.length,2);
  const importArgs={scan_id:scanJob.scan_id,node_id:node.node_id,catalog_id:'directory-check',name:'Imported Ahi',scenario:directoryScenario,confirm_profile:'plo-post-tree-v1',all_actions_common_scale:true,expected_workspace_revision:(await call('research_list_workspace')).workspace_revision};
  const imported=await finish(await call('research_import_directory',importArgs));assert.equal(imported.spot_count,2);
  await assert.rejects(()=>finishCall('research_import_directory',{...importArgs,expected_workspace_revision:imported.workspace_revision}),/revision conflict/);
  const directoryPlan=await finish(await call('research_validate',{catalog_id:imported.catalog_id,catalog_revision:imported.catalog_revision,set_id:set.set_id,set_revision:set.revision,settings}));
  const dr=await finish(await call('research_start',{plan_id:directoryPlan.plan_id,request_key:'directory'}));assert.equal(dr.completed_spots,2);
  const di=(await call('research_get_result',{result_id:dr.result_id,condition_id:'all'})).items;assert.ok(Math.abs(di[0].Actions.find(a=>a.Key==='bet:0.5').Frequency-200/3)<1e-10);assert.ok(di.every(r=>r.Equity.Equity>=0));
  for(const folder of ['vs_donkraiseAI','vs_L4']){
   const n=nodes.items.find(n=>n.folder===folder),sc=n.position==='CO'?{...directoryScenario,hero_position:'CO',villain_position:'BTN'}:directoryScenario;
   const im=await finish(await call('research_import_directory',{...importArgs,node_id:n.node_id,catalog_id:'directory-'+folder,scenario:sc,expected_workspace_revision:(await call('research_list_workspace')).workspace_revision}));
   const p=await finish(await call('research_validate',{catalog_id:im.catalog_id,catalog_revision:im.catalog_revision,set_id:set.set_id,set_revision:set.revision,settings}));const r=await finish(await call('research_start',{plan_id:p.plan_id,request_key:folder}));
   const row=(await call('research_get_result',{result_id:r.result_id,condition_id:'all'})).items[0];assert.equal(row.Status,'completed');
   if(folder==='vs_L4')assert.equal(row.Actions.find(a=>a.Key==='call').Frequency,100);else {assert.ok(Math.abs(row.Actions.find(a=>a.Key==='raise:allin').Frequency-100/3)<1e-10);const art=await call('research_export',{result_id:r.result_id,format:'svg'});assert.match((await call('get_artifact',{artifact_id:art.artifact_id})).text,/All-in/);}
  }
  // A valid-looking but malformed CSV fails native row validation atomically.
  const broken=path.join(tree,'AsKd7c/IP/vs_check/AsKd7c_BTN_C_C.csv');await fs.appendFile(broken,'badhand,1,100,1,0,0,1\n');
  const rev=(await call('research_list_workspace')).workspace_revision;
  await assert.rejects(()=>finishCall('research_import_directory',{...importArgs,catalog_id:'changed',expected_workspace_revision:rev}),/changed after preview/);
  const badScan=await finish(await call('research_scan_directory',{directory:tree}));await assert.rejects(()=>finishCall('research_import_directory',{...importArgs,scan_id:badScan.scan_id,catalog_id:'malformed',expected_workspace_revision:rev}));assert.equal((await call('research_list_workspace')).workspace_revision,rev);
  console.log('Research MCP integration passed: '+base);
  async function callJob(args){return call('research_validate',args);}
  async function finishCall(name,args){return finish(await call(name,args));}
 }finally{await client.close();}
});
