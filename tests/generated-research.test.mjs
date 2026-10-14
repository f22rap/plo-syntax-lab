import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {setTimeout as sleep} from 'node:timers/promises';
import {createRequire} from 'node:module';
import {Client} from '@modelcontextprotocol/client';
import {StdioClientTransport} from '@modelcontextprotocol/client/stdio';
import {root} from '../mcp/runtime.mjs';
const R=createRequire(import.meta.url)('../src/research-recipes.cjs');

test('Generated research: saved presets → board syntax → real native CSV/EQ → restartable archive', {timeout:180000},async()=>{
 const testRoot=process.env.PLO_MCP_TEST_ROOT||path.join(root,'test-results/generated-research');await fs.mkdir(testRoot,{recursive:true});
 const base=await fs.mkdtemp(path.join(testRoot,'integration-'));
 const input=path.join(base,'input'),output=path.join(base,'output'),history=path.join(base,'history');for(const dir of [input,output,history])await fs.mkdir(dir);
 const config=path.join(base,'config.json');await fs.writeFile(config,JSON.stringify({inputRoots:[input],outputRoot:output,historyRoot:history,timeoutSeconds:120}));
 let client,transport;
 async function connect(){client=new Client({name:'generated-research-test',version:'1'});transport=new StdioClientTransport({command:process.execPath,args:[path.join(root,process.platform==='win32'?'mcp/server.mjs':'tests/research-harness.mjs'),...(process.platform==='win32'?['--config']:[]),config],env:process.env,cwd:root,stderr:'pipe'});transport.stderr.on('data',d=>process.stderr.write(d));await client.connect(transport);}
 async function call(name,args={}){const r=await client.callTool({name,arguments:args});if(r.isError)throw Error(r.content.map(c=>c.text||'').join('\n'));return r.structuredContent||JSON.parse(r.content[0].text);}
 async function terminal(job){for(let i=0;i<5000;i++){const j=await call('research_get_job',{job_id:job.job_id});if(!['queued','running'].includes(j.state))return j;await sleep(10);}throw Error('Job timed out');}
 async function finish(job){const j=await terminal(job);assert.equal(j.state,'completed',j.error);return j.result;}
 const literal=(id,syntax)=>({condition_id:id,label:id,enabled:true,kind:'literal',syntax,by_board:{}});
 let set=R.createPresetSet({name:'ユーザの条件',preset_ids:['set.top.all','set.middle.all','set.bottom.all']});
 const top=set.conditions.find(c=>c.generator.preset_id==='set.top.all');top.label='私のトップセット';
 set.conditions.find(c=>c.generator.preset_id==='set.bottom.all').enabled=false;
 set.conditions.push(literal('all','*'),{...literal('per',''),kind:'per_board',by_board:{AsKd7c:'AA'}});
 let catalog={catalog_id:'generated-boards',revision:0,name:'Synthetic generated research',highest_rank:'all',scenario:{game:'PLO4',players:2,hero_position:'BB',villain_position:'BTN',effective_stack_bb:'100',pot_type:'SRP',action_history:'BTN open, BB call',solution_context:'synthetic test only',equity_reference:'Before both actions'},spots:[]};
 for(const [id,board,hand,check] of [['ahi','AsKd7c','AcAd4c5d',.25],['khi','Kh9h3c','KcKd4c5d',.6],['paired','AsAd7c','AcAh4c5d',.4],['same','AsKd7c','AcAd4c5d',.75]]){
  const csv=async(name,weight,h=hand)=>{const file=path.join(input,id+'_'+name+'.csv');await fs.writeFile(file,'hand,frequency,percent,weight,ev,ev_diff,variations\n'+h+',1,100,'+weight+',0,0,1\n');return {path:file};};
  catalog.spots.push({spot_id:id,board,node:id==='same'?'BB alternate':'BB first',hero_actions:[{action_id:'check',label:'Check',kind:'check',pot_fraction:'',source:await csv('check',check)},{action_id:'bet',label:'Bet 50%',kind:'bet',pot_fraction:'.5',source:await csv('bet',1-check)}],opponent_sources:[await csv('opponent',1,'QhQs6d8d')],opponent_percent_unit:false,all_actions_common_scale:true,opponent_common_scale:true});
 }
 const settings={HighestRank:'all',Trials:1000,Seed:7123,GroupBy:'pair',AllowMissingDefinitions:true};
 const req=(override={})=>({catalog_id:catalog.catalog_id,catalog_revision:catalog.revision,set_id:set.set_id,set_revision:set.revision,settings,...override});
 await connect();
 try{
  const presets=await call('research_list_default_presets',{limit:200});assert.equal(presets.total,185);assert.equal(presets.items.filter(p=>p.supported).length,159);
  assert.equal((await call('research_list_default_presets',{query:'set.top.all'})).total,1);
  set=(await call('research_save_condition_set',{condition_set:set,expected_workspace_revision:0})).condition_set;
  catalog=(await call('research_save_catalog',{catalog,expected_workspace_revision:1})).catalog;
  const bad=structuredClone(set);bad.conditions[0].generator.typo=true;await assert.rejects(()=>call('research_save_condition_set',{condition_set:bad,expected_workspace_revision:2}));
  const strict=await terminal(await call('research_validate',req({settings:{...settings,AllowMissingDefinitions:false}})));assert.equal(strict.state,'failed');assert.match(strict.error,/未設定/);
  const plan=await finish(await call('research_validate',req()));
  assert.equal(plan.generation_statistics.board_analyses,3);assert.equal(plan.target_spots,4);assert.equal(plan.generation.rows.length,12);
  assert.equal(plan.resolved.ahi.find(r=>r.Id===top.condition_id).Syntax,'AA');assert.equal(plan.resolved.khi.find(r=>r.Id===top.condition_id).Syntax,'KK');
  const empty=plan.generation.rows.find(r=>r.board==='AsAd7c'&&r.condition_id===set.conditions.find(c=>c.generator?.preset_id==='set.middle.all').condition_id);assert.equal(empty.status,'empty_condition');assert.equal(empty.legal_hand_count,0);
  const repeated=await finish(await call('research_validate',req()));assert.equal(repeated.input_fingerprint,plan.input_fingerprint);assert.ok(repeated.generation_statistics.cache_hits>0);
  const result=await finish(await call('research_start',{plan_id:plan.plan_id,request_key:'generated'}));assert.equal(result.completed_spots,4);assert.equal(result.status,'completed_with_gaps');assert.ok(result.status_counts.empty_condition>0);assert.ok(result.status_counts.no_matching_hands>0);
  const rows=(await call('research_get_result',{result_id:result.result_id,view:'board',limit:200})).items;
  const a=rows.find(r=>r.SpotId==='ahi'&&r.ConditionId===top.condition_id),k=rows.find(r=>r.SpotId==='khi'&&r.ConditionId===top.condition_id);
  assert.equal(a.Label,'私のトップセット');assert.equal(a.Syntax,'AA');assert.equal(k.Syntax,'KK');assert.equal(a.Actions[0].Frequency,25);assert.equal(k.Actions[0].Frequency,60);assert.ok(a.Equity.Equity>0&&a.Equity.Equity<100);
  assert.deepEqual(a.Equity,rows.find(r=>r.SpotId==='ahi'&&r.ConditionId==='per').Equity);
  for(const r of rows.filter(r=>['empty_condition','missing_definition','no_matching_hands'].includes(r.Status))){assert.equal(r.Equity,null);assert.ok(r.Actions.every(a=>a.Frequency===null));}
  const meta=await call('research_get_result',{result_id:result.result_id,view:'metadata'});assert.deepEqual(meta.condition_set,set);assert.equal(meta.generation_sha256,plan.generation_sha256);
  const category=await call('research_get_result',{result_id:result.result_id,view:'category',condition_id:top.condition_id});assert.ok(category.items.some(x=>x.ActionKey==='check'&&x.PairedCount===2));
  for(const format of ['json','csv','svg','markdown']){const artifact=await call('research_export',{result_id:result.result_id,format});const text=await fs.readFile(artifact.path,'utf8');assert.match(text,/私のトップセット/);if(format==='json'){const archive=JSON.parse(text);assert.equal(archive.Schema,2);assert.equal(archive.Generation.sha256,plan.generation_sha256);}if(format==='svg'){assert.match(text,/<title>/);assert.match(text,/Bet 50/);}}
  // A different highest-rank request reuses definitions, not A-high output.
  const onlyK=await finish(await call('research_validate',req({settings:{...settings,HighestRank:'K'}})));assert.equal(onlyK.target_spots,1);assert.equal(onlyK.generation_statistics.board_analyses,1);assert.ok(onlyK.generation_statistics.cache_hits>=3);assert.equal(onlyK.resolved.khi[0].Syntax,'KK');
  // Restart reads immutable generated snapshots without running generation again.
  await client.close();await connect();assert.equal((await call('research_get_result',{result_id:result.result_id,view:'metadata'})).generation_sha256,plan.generation_sha256);
  assert.equal((await call('research_list_history')).unreadable,0);
  // A cancelled generation has no executable plan and writes no calculation history.
  const generating=await call('research_validate',req());
  for(let i=0;i<1000;i++){const j=await call('research_get_job',{job_id:generating.job_id});if(j.progress?.message==='ボード別syntaxを生成中'){await call('research_cancel_job',{job_id:j.job_id});break;}assert.ok(['queued','running'].includes(j.state));await sleep(5);}
  const cancelledGeneration=await terminal(generating);assert.equal(cancelledGeneration.state,'cancelled');assert.equal(cancelledGeneration.result,null);assert.equal((await call('research_list_history')).total,1);
  // Cancellation during EQ retains complete boards and the original generation snapshot.
  const longPlan=await finish(await call('research_validate',req({settings:{...settings,Trials:100000}})));
  const running=await call('research_start',{plan_id:longPlan.plan_id,request_key:'cancel-generated'});
  for(let i=0;i<3000;i++){const j=await call('research_get_job',{job_id:running.job_id});if(j.progress?.Completed>=1){await call('research_cancel_job',{job_id:j.job_id});break;}assert.ok(['queued','running'].includes(j.state),j.error);await sleep(5);}
  const cancelledRun=await terminal(running);assert.equal(cancelledRun.state,'cancelled');assert.ok(cancelledRun.result.completed_spots>=1&&cancelledRun.result.completed_spots<4);assert.equal(cancelledRun.result.generation_sha256,longPlan.generation_sha256);
  const partial=(await call('research_get_result',{result_id:cancelledRun.result.result_id,view:'board',limit:200})).items;assert.equal(partial.length,cancelledRun.result.completed_spots*4);
  // Changed input bytes invalidate a generated plan before any numerical output.
  const sourcePlan=await finish(await call('research_validate',req())),source=catalog.spots[0].hero_actions[0].source.path,sourceBytes=await fs.readFile(source);
  try{await fs.appendFile(source,'\n');const changed=await terminal(await call('research_start',{plan_id:sourcePlan.plan_id,request_key:'changed-csv'}));assert.equal(changed.state,'failed');assert.equal(changed.result,null);}finally{await fs.writeFile(source,sourceBytes);}
  // Digest and unknown-field corruption fail closed when loading history.
  const archivePath=path.join(history,'Research','Runs',result.result_id+'.json'),original=await fs.readFile(archivePath,'utf8'),archive=JSON.parse(original);
  archive.Generation.payload_json=archive.Generation.payload_json.replace('"syntax":"AA"','"syntax":"KK"');await fs.writeFile(archivePath,JSON.stringify(archive));await assert.rejects(()=>call('research_get_result',{result_id:result.result_id}),/ハッシュ/);await fs.writeFile(archivePath,original);
  const other=JSON.parse(original),payload=JSON.parse(other.Generation.payload_json);payload.future_field=1;other.Generation.payload_json=JSON.stringify(payload);other.Generation.sha256=R.hash(other.Generation.payload_json);await fs.writeFile(archivePath,JSON.stringify(other));await assert.rejects(()=>call('research_get_result',{result_id:result.result_id}),/未対応/);await fs.writeFile(archivePath,original);
  // Unsupported versions must never become missing/zero even with allow-missing.
  const unsupported=structuredClone(set);unsupported.conditions[0].generator.recipe_version=99;
  set=(await call('research_save_condition_set',{condition_set:unsupported,expected_workspace_revision:2})).condition_set;
  const blocked=await terminal(await call('research_validate',req()));assert.equal(blocked.state,'failed');assert.match(blocked.error,/unsupported_recipe/);assert.equal(blocked.result,null);
  set.conditions[0].generator.recipe_version=1;set=(await call('research_save_condition_set',{condition_set:set,expected_workspace_revision:3})).condition_set;
  const stale=await finish(await call('research_validate',req()));set.conditions[0].label='変更後';set=(await call('research_save_condition_set',{condition_set:set,expected_workspace_revision:4})).condition_set;
  const rejected=await terminal(await call('research_start',{plan_id:stale.plan_id,request_key:'stale'}));assert.equal(rejected.state,'failed');assert.match(rejected.error,/changed/);
  await fs.writeFile(path.join(base,'evidence.json'),JSON.stringify({plan,result,rows,category},null,2));
 }finally{await client?.close();}
});
