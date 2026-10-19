import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {z} from 'zod';
import {boardModel,SLOTS,BLOCKS} from '../mcp/strategy-features.mjs';
import {evaluate,selectFour,averageCells,classifyBoards,analyzeBoards} from '../mcp/strategy-analysis.mjs';
import {collectStrategyBoards,readStrategyReport} from '../mcp/strategy-report.mjs';
import {ResearchService} from '../mcp/research-service.mjs';
import {registerResearchTools,researchToolNames} from '../mcp/research-tools.mjs';
import {Service} from '../mcp/service.mjs';
import {fileHash} from '../mcp/runtime.mjs';
const ranks=s=>[...s].map(r=>'23456789TJQKA'.indexOf(r)+2);
const feature=(b,h)=>boardModel(b).rankFeatures(ranks(h));

test('PLO exact card outs, wheel and strict all-out nut policy',()=>{
 for(const [b,h,outs,nut,state] of [['AsQd6c','AKJT',9,9,5],['AsKd5c','AQJT',9,9,5],['As6d5c','AK43',8,4,4],['As8d7c','AT96',17,11,6]]){
  const f=feature(b,h);assert.equal(f.outs,outs);assert.equal(f.nut_outs,nut);assert.equal(f.features.sd_outs_slot,state);assert.ok(BLOCKS.every(k=>f.features[k]===2));
 }
 assert.equal(feature('As8d2c','AKQJ').outs,0);
 assert.deepEqual(boardModel('As8d7c').oesd_cores,['T9','96','65']);
 // Two completion ranks can be a double gut, not an OESD core.
 assert.deepEqual(boardModel('As7d3c').oesd_cores,[]);
 assert.equal(feature('As7d3c','AK54').outs,8);
});
test('Ahit, combined BDFD slot and pocket rank semantics',()=>{
 const m=boardModel('AsKd5c');
 assert.equal(m.handFeatures('AhKhQdJs'),null);assert.equal(m.handFeatures('AhAdQdJs'),null);
 assert.equal(m.handFeatures('AhQsJdTc').features.bdfd_slot,0);
 assert.equal(m.handFeatures('AhQsJsTc').features.bdfd_slot,1);
 assert.equal(m.handFeatures('AhQsJsTs').features.bdfd_slot,2);
 assert.equal(m.handFeatures('AdQdJsTs').features.bdfd_slot,1); // 2+2
 assert.equal(feature('AsKd5c','AQ22').features.pp_slot,1);
 assert.equal(feature('AsKd5c','AQQ2').features.pp_slot,2);
 assert.equal(feature('AsKd5c','AQ77').features.pp_slot,3);
 assert.equal(feature('AsKd5c','AQQQ').features.pp_slot,2);
 assert.equal(feature('AsKd5c','AQQ2').features.block_sd_3,1);
 assert.equal(feature('AsKd5c','AQQ7').features.block_sd_3,0);
 assert.equal(feature('As8d7c','AK99').features.block_oe_targets2,1);
 assert.equal(feature('As6d3c','AKQ4').features.block_oe_top,1);
 assert.equal(feature('As6d3c','AKQ5').features.block_oe_bottom,1);
});
test('rejects unsupported board textures',()=>{
 for(const b of ['KsQd6c','AsQd6d','AsAd6c','As2d3c'])assert.throws(()=>boardModel(b));
});
function synthetic(){const atoms=[];for(let s=0;s<2;s++)for(let b=0;b<2;b++)for(let p=0;p<2;p++)for(let k=0;k<2;k++){
 const features=Object.fromEntries(SLOTS.map(f=>[f,0]));Object.assign(features,{sd_outs_slot:s,bdfd_slot:b,pp_slot:p,kicker_top:k});if(s)for(const f of BLOCKS)features[f]=2;
 const probability=.1+.3*b+.2*p+.1*k+.15*s;atoms.push({features,weight:1,bet:probability,n:1});}
 return {key:'AQ6',board:'AsQd6c',sd_ranks:['K','J','T'],oesd_cores:[],atoms};}
test('exhaustive 455 shared models, conditional priority and known signal recovery',()=>{
 const b=synthetic(),m=selectFour([b]);assert.equal(m.candidate_combinations,455);assert.deepEqual(m.selected,['sd_outs_slot','bdfd_slot','pp_slot','kicker_top']);assert.ok(m.selection.mae<1e-12);assert.ok(evaluate(b,m.selected).r2>1-1e-12);
 assert.equal(m.stages[0].routing_gate,true);assert.ok(m.stages.slice(1).every(s=>s.gain_added>=-1e-12));
});
test('equal-board mean includes zero, excludes absent, uses unrounded >=50%',()=>{
 const rows=[{board:'A',cells:[{answers:[0,0,0,0],p:0,n:100},{answers:[0,1,0,0],p:.49999,n:1},{answers:[0,2,0,0],p:null,n:0}]},{board:'B',cells:[{answers:[0,0,0,0],p:1,n:1}]}];
 const c=averageCells(rows);assert.equal(c[0].p,.5);assert.equal(c[0].action,'bet');assert.equal(c[0].valid_boards,2);assert.equal(c[1].action,'check');assert.equal(c.length,2);
});
test('approved A82/A87 separation, custom partition guards, reproducible effect clustering',()=>{
 const b=[{...synthetic(),key:'A82'},{...synthetic(),key:'A87'}];assert.deepEqual(classifyBoards(b).groups.map(g=>g.id),['G2','G5']);
 assert.throws(()=>classifyBoards([{...synthetic(),key:'AQ7'}]),/No approved block/);
 assert.throws(()=>classifyBoards(b,{mode:'custom',blocks:[{id:'G',boards:['A82','A82']}]}),/exactly once/);
 const options={mode:'effect-cluster',block_count:2};assert.deepEqual(classifyBoards(b,options),classifyBoards(b,options));
 assert.throws(()=>classifyBoards(b,{mode:'effect-cluster',block_count:3}));
});
async function fixture(t){
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-strategy-'));t.after(()=>fs.rm(dir,{recursive:true,force:true}));const historyRoot=path.join(dir,'history'),outputRoot=path.join(dir,'out');await fs.mkdir(historyRoot);await fs.mkdir(outputRoot);
 const config={inputRoots:[await fs.realpath(dir)],outputRoot:await fs.realpath(outputRoot),historyRoot:await fs.realpath(historyRoot),maxCsvBytes:4e6,timeoutSeconds:30};
 const actions=[];for(const [kind,text] of [['check','hand,weight\nAhKsJcTd,0.4\nAcKc2d2h,1\nAhQhJsTc,1\n'],['bet','hand,weight\nAhKsJcTd,0.6\nAcKc2d2h,0\nAdKc4s3h,1\n']]){const file=path.join(dir,kind+'.csv');await fs.writeFile(file,text);actions.push({action_id:kind,label:kind,kind,pot_fraction:kind==='bet'?'0.75':'',source:{path:file,sha256:await fileHash(file)}});}
 const catalog={catalog_id:'fixture',revision:1,scenario:{game:'PLO4',players:2,hero_position:'CO',villain_position:'BTN'},spots:[{spot_id:'aq6',board:'AsQd6c',node:'ROOT',all_actions_common_scale:true,hero_actions:actions,opponent_sources:[]}]};
 const legacy=new Service(config),service=new ResearchService(config,legacy,{native:async()=>({catalogs:[catalog]})});t.after(()=>service.close());return {dir,config,catalog,legacy,service};
}
test('CSV pipeline uses union weights, excludes two pair and keeps zero bet',async t=>{
 const f=await fixture(t),data=await collectStrategyBoards(f.config,f.catalog);assert.equal(data.boards.length,1);const atoms=data.boards[0].atoms;assert.equal(atoms.reduce((s,a)=>s+a.n,0),3);assert.equal(atoms.reduce((s,a)=>s+a.weight,0),3);assert.equal(atoms.reduce((s,a)=>s+a.bet,0),1.6);
 const zero=atoms.find(a=>a.features.pp_slot===1);assert.equal(zero.bet,0);
 const a=f.catalog.spots[0].hero_actions[0];await fs.appendFile(a.source.path,'Ah9s8h7c,.1\n');await assert.rejects(()=>collectStrategyBoards(f.config,f.catalog),/changed/);
});
test('source/menu/node/duplicate board and cancellation guards',async t=>{
 const f=await fixture(t),copy=()=>structuredClone(f.catalog);let c=copy();c.spots[0].all_actions_common_scale=false;await assert.rejects(()=>collectStrategyBoards(f.config,c),/common-scale/);
 c=copy();c.spots.push({...c.spots[0],spot_id:'other'});await assert.rejects(()=>collectStrategyBoards(f.config,c),/one physical board/);
 c=copy();c.spots.push({...c.spots[0],spot_id:'other',node:'C'});await assert.rejects(()=>collectStrategyBoards(f.config,c),/one decision node/);
 await assert.rejects(()=>collectStrategyBoards(f.config,f.catalog,['missing']),/spot ID/);
 await assert.rejects(()=>collectStrategyBoards(f.config,f.catalog,[],{signal:AbortSignal.abort()}),/Cancelled/);
});
test('job, persisted history, PDF bytes, restart and re-export through service',async t=>{
 const f=await fixture(t),job=f.service.strategyReport({catalog_id:'fixture',catalog_revision:1,classification:'approved-five',spot_ids:[],create_pdf:true});assert.equal(job.state,'queued');await f.service.queue;
 const result=f.service.getJob(job.job_id);assert.equal(result.state,'completed',result.error);assert.equal(result.result.blocks[0].selected.length,4);const {report_id,pdf}=result.result;
 const artifact=await f.legacy.readArtifact(pdf.artifact_id),pdfBytes=Buffer.from(artifact.blob,'base64');assert.ok(pdfBytes.subarray(0,5).equals(Buffer.from('%PDF-')));
 // Footers must stay on their original pages, not add empty overflow pages.
 assert.equal((pdfBytes.toString('latin1').match(/\/Type \/Page\b/g)||[]).length,6);
 const r=await readStrategyReport(f.config,report_id);assert.equal(r.threshold,.5);assert.equal(r.provenance.sources.length,2);assert.equal(r.groups[0].candidate_combinations,455);
 const fresh=new ResearchService(f.config,new Service(f.config),{native:async()=>{throw Error('CSV/native must not be read during re-export');}});t.after(()=>fresh.close());
 assert.equal((await fresh.strategyHistory({})).items[0].report_id,report_id);assert.equal((await fresh.strategyResult({report_id,view:'cells',limit:1})).items.length,1);
 const exported=await fresh.strategyExport({report_id,format:'pdf'});assert.equal(exported.mimeType,'application/pdf');assert.ok(exported.bytes>10000);
 await assert.rejects(()=>fresh.strategyResult({report_id:'../../escape',view:'metadata'}),/Invalid report/);
});
test('stale catalogs fail without persisted reports',async t=>{
 const f=await fixture(t),j=f.service.strategyReport({catalog_id:'fixture',catalog_revision:0,spot_ids:[]});await f.service.queue;assert.equal(f.service.getJob(j.job_id).state,'failed');assert.equal((await f.service.strategyHistory({})).total,0);
});
test('standard workflow is discoverable and strict MCP schemas provide defaults',()=>{
 const tools=new Map();registerResearchTools((name,description,shape,fn)=>tools.set(name,{schema:z.object(shape).strict(),fn}),{});
 for(const name of ['research_build_strategy_report','research_get_strategy_report','research_list_strategy_reports','research_export_strategy_report'])assert.ok(researchToolNames.includes(name)&&tools.has(name));
 const build=tools.get('research_build_strategy_report'),a=build.schema.parse({catalog_id:'c',catalog_revision:1});assert.equal(a.classification,'approved-five');assert.equal(a.create_pdf,true);assert.throws(()=>build.schema.parse({...a,threshold:.55}));
 assert.throws(()=>build.fn({...a,blocks:[{id:'G',boards:['AQ6']}]}),/custom/);
});
test('official SDK discovers, builds, reads and re-exports the workflow including a binary PDF resource',async t=>{
 const {Client,InMemoryTransport}=await import('@modelcontextprotocol/client');
 const {buildServer}=await import('../mcp/server-core.mjs');const f=await fixture(t);
 const server=buildServer(f.legacy,f.service),client=new Client({name:'strategy-test',version:'1'}),[ct,st]=InMemoryTransport.createLinkedPair();
 await server.connect(st);await client.connect(ct);t.after(async()=>{await client.close();await server.close();});
 async function call(name,args){const r=await client.callTool({name,arguments:args});assert.ok(!r.isError,JSON.stringify(r.content));return r.structuredContent;}
 assert.equal((await client.listTools()).tools.length,40);
 const job=await call('research_build_strategy_report',{catalog_id:'fixture',catalog_revision:1});await f.service.queue;
 const done=await call('research_get_job',{job_id:job.job_id});assert.equal(done.state,'completed',done.error);const {report_id,pdf}=done.result;
 const resource=await client.readResource({uri:pdf.uri});assert.equal(resource.contents[0].mimeType,'application/pdf');assert.ok(resource.contents[0].blob.startsWith('JVBER'));
 assert.equal((await call('research_get_strategy_report',{report_id,view:'metadata'})).threshold,.5);
 assert.equal((await call('research_list_strategy_reports',{})).total,1);
 assert.equal((await call('research_export_strategy_report',{report_id,format:'json'})).mimeType,'application/json');
});
