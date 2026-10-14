import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {setTimeout as sleep} from 'node:timers/promises';
import {Client} from '@modelcontextprotocol/client';
import {StdioClientTransport} from '@modelcontextprotocol/client/stdio';
import {root} from '../mcp/runtime.mjs';
import {scanDirectory} from '../mcp/research-directory.mjs';
import {makeDirectoryFixture,directoryScenario} from './directory-fixture.mjs';

test('Native directory discovery: Node parity, malformed denominators, duplicate boards, scope and schema checks',{timeout:30000},async t=>{
 const base=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'plo-native-directory-')));
 const input=path.join(base,'CSV 日本語 with spaces'),output=path.join(base,'output'),history=path.join(base,'history');
 for(const p of [input,output,history])await fs.mkdir(p);
 const config={inputRoots:[input],outputRoot:output,historyRoot:history,maxCsvBytes:2000000,timeoutSeconds:120};
 const file=path.join(base,'config.json');await fs.writeFile(file,JSON.stringify(config));
 const windows=process.platform==='win32';
 const client=new Client({name:'native-directory-test',version:'1'});
 const env={...process.env,PATH:''};for(const key of Object.keys(env))if(key.toUpperCase()==='PATH'&&key!=='PATH')delete env[key];
 const transport=new StdioClientTransport({command:windows?path.join(root,'dist/PloMcp.Connect.exe'):process.env.PLO_NATIVE_TEST_DOTNET,args:[...(windows?[]:[process.env.PLO_NATIVE_TEST_HOST]),'--stdio','--config',file],env,stderr:'pipe'});
 let stderr='';transport.stderr.on('data',b=>{stderr+=b;});
 t.after(async()=>{await client.close();await fs.rm(base,{recursive:true,force:true});});await client.connect(transport);
 async function call(name,args={}){const r=await client.callTool({name,arguments:args});if(r.isError)throw Error(r.content[0].text);return r.structuredContent;}
 async function finish(job){job=await job;for(let i=0;i<1000;i++){const j=await call('research_get_job',{job_id:job.job_id});if(j.state==='completed')return j.result;if(!['queued','running'].includes(j.state))throw Error(j.error||j.state);await sleep(5);}throw Error('Job timed out.');}
 async function scan(){const s=await finish(await call('research_scan_directory',{directory:input,highest_rank:'A'}));return {...s,nodes:(await call('research_get_directory_scan',{scan_id:s.scan_id})).items,spots:(await call('research_get_directory_scan',{scan_id:s.scan_id,view:'spots',limit:200})).items,issues:(await call('research_get_directory_scan',{scan_id:s.scan_id,view:'issues',limit:200})).items};}
 const sorted=(rows,key)=>[...rows].sort((a,b)=>String(a[key]).localeCompare(String(b[key])));
 const core=s=>({...s,spot:{spot_id:s.spot.spot_id,board:s.spot.board,node:s.spot.node,hero_actions:sorted(s.spot.hero_actions,'action_id'),opponent_sources:sorted(s.spot.opponent_sources,'path'),opponent_percent_unit:s.spot.opponent_percent_unit,all_actions_common_scale:s.spot.all_actions_common_scale,opponent_common_scale:s.spot.opponent_common_scale}});
 await makeDirectoryFixture(input);
 const native=await scan(),node=await scanDirectory(config,{directory:input,highest_rank:'A'});
 assert.equal(native.board_count,2);assert.equal(native.excluded_board_count,2);
 assert.deepEqual(sorted(native.nodes,'node_id'),sorted(node.nodes,'node_id'));
 assert.deepEqual(sorted(native.spots.map(core),'opponent_node').sort((a,b)=>(a.board+a.node_id).localeCompare(b.board+b.node_id)),node.spots.map(core).sort((a,b)=>(a.board+a.node_id).localeCompare(b.board+b.node_id)));
 assert.deepEqual(native.issues,[]);
 // Both implementations use every BTN vs_check action for the CO root range.
 const co=native.spots.find(s=>s.board==='AsKd7c'&&s.folder==='donk');
 assert.equal(co.eq_status,'ready');assert.equal(co.opponent_node,'IP/vs_check/C');
 assert.deepEqual(sorted(co.spot.opponent_sources,'path'),sorted(native.spots.find(s=>s.board==='AsKd7c'&&s.folder==='vs_check').spot.hero_actions.map(a=>a.source),'path'));
 const n=native.nodes.find(n=>n.folder==='vs_check');
 const args=s=>({scan_id:s.scan_id,node_id:n.node_id,catalog_id:'blocked',name:'Blocked test',scenario:directoryScenario,confirm_profile:'plo-post-tree-v1',all_actions_common_scale:true,expected_workspace_revision:0});
 const bad=path.join(input,'AsKd7c/OOP/donk/unknown.csv');await fs.writeFile(bad,'bad');
 const malformed=await scan();
 assert.ok(malformed.spots.find(s=>s.board==='AsKd7c'&&s.folder==='donk').problems.includes('unreadable_or_unknown_sibling'));
 assert.equal(malformed.spots.find(s=>s.board==='AsKd7c'&&s.folder==='vs_check').eq_status,'opponent_pre_action_node_missing_or_ambiguous');
 await assert.rejects(()=>finish(callJob('research_import_directory',args(malformed))),/blocked boards/);
 assert.equal((await call('research_list_workspace')).workspace_revision,0);
 await fs.rm(bad);await makeDirectoryFixture(input,['KdAs7c']);
 const duplicate=await scan();assert.ok(duplicate.spots.filter(s=>['AsKd7c','KdAs7c'].includes(s.board)).every(s=>s.problems.includes('duplicate_physical_board')));
 await assert.rejects(()=>finish(callJob('research_import_directory',{...args(duplicate),allow_frequency_only:true})),/duplicate_physical_board/);
 await assert.rejects(()=>finish(callJob('research_scan_directory',{directory:base})),/outside/);
 const aliased=path.join(input,'linked');
 try{await fs.symlink(base,aliased,windows?'junction':'dir');await assert.rejects(()=>finish(callJob('research_scan_directory',{directory:aliased})),/リンク/);}catch(e){if(!['EPERM','EACCES'].includes(e.code))throw e;}
 for(const [name,testArgs] of [
  ['research_list_workspace',{limit:'10'}],['research_list_workspace',{limit:1.5}],['research_list_workspace',{limit:201}],
  ['research_validate',{catalog_id:'c',catalog_revision:0,set_id:'s',set_revision:0,settings:{Trials:100,execute:'shell'}}],
  ['research_scan_directory',{directory:input,highest_rank:'k'}],['research_get_job',{job_id:'../outside'}],
  ['research_get_result',{result_id:'a'.repeat(32)+'\n'}],['get_status',{unexpected:true}],
  ['research_import_directory',{...args(native),scenario:{...directoryScenario,players:'2'}}]
 ])await assert.rejects(()=>call(name,testArgs));
 assert.equal((await call('research_list_workspace')).workspace_revision,0);
 await assert.rejects(()=>client.readResource({uri:'file://'+file}),/Unknown resource/);
 // Import the unaffected board and verify the convention survives disk storage.
 const imported=await finish(call('research_import_directory',{...args(await scan()),node_id:co.node_id,catalog_id:'co-root',boards:['Ah8h3c'],scenario:{...directoryScenario,hero_position:'CO',villain_position:'BTN',action_history:'ROOT'}}));
 assert.equal(imported.spot_count,1);
 const saved=JSON.parse((await fs.readFile(path.join(history,'Research','workspace.json'),'utf8')).replace(/^\uFEFF/,''));
 const catalog=saved.catalogs.find(c=>c.catalog_id==='co-root');
 assert.match(catalog.scenario.equity_reference,/co-btn-vs-check-v1/);
 assert.equal(catalog.spots[0].opponent_sources.length,2);
 assert.ok(catalog.spots[0].opponent_sources.every(s=>s.path.includes('vs_check')&&s.sha256.length===64));
 assert.equal(stderr,'');
 // Resolve the submitted job before polling; keep failure messages intact.
 async function callJob(name,args){return call(name,args);}
});
