import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {inspectCsv,compareCsv} from '../mcp/csv-ev-inspection.mjs';
import {registerResearchTools} from '../mcp/research-tools.mjs';
import {ResearchService} from '../mcp/research-service.mjs';
import {z} from 'zod';
const header='hand,frequency,percent,weight,ev,ev_diff,variations\r\n';
async function fixture(t){const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-ev-'));t.after(()=>fs.rm(dir,{recursive:true,force:true}));return {dir,config:{inputRoots:[await fs.realpath(dir)],maxCsvBytes:4e6},write:async(name,text)=>{const f=path.join(dir,name);await fs.writeFile(f,text);return f;}};}
test('preserves zero-weight EV and raw precision, quoted CSV and BOM',async t=>{
 const f=await fixture(t),p=await f.write('data.csv','\uFEFF'+header+'"AhKsQcJd",0,0,0,1.24000000000000000001,-0.29,1\r\nAsKhQdJc,1,100,1,,0,1\r\n');
 const r=await inspectCsv(f.config,{path:p,board:'AcTd7c',limit:1});assert.equal(r.row_count,2);assert.equal(r.zero_weight_rows,1);assert.equal(r.samples[0].values.ev,'1.24000000000000000001');assert.equal(r.columns_summary.ev.missing,1);assert.equal(r.next_offset,1);assert.equal(r.ev_calculation_performed,false);
 const next=await inspectCsv(f.config,{path:p,board:'AcTd7c',offset:1,expected_sha256:r.sha256});assert.equal(next.samples[0].values.ev,'');
 await fs.appendFile(p,'AhKdQsJc,0,0,0,1,0,1\n');await assert.rejects(()=>inspectCsv(f.config,{path:p,board:'AcTd7c',expected_sha256:r.sha256}),/changed/);
});
test('physical hand join, absent action EV null and all-zero hands retained',async t=>{
 const f=await fixture(t),l=await f.write('AsTd7c_CO_ROOT_C.csv',header+'AhKsQcJd,0,0,0,1.2,0,1\nAcKhQdJc,1,100,1,2,0,1\n');
 const r=await f.write('AsTd7c_CO_ROOT_R75.csv',header+'JdQcKsAh,0,0,0,1.3,-.1,1\nAdKcQhJs,1,100,1,3,0,1\n');
 const out=await compareCsv(f.config,{left_path:l,right_path:r,board:'AsTd7c'});
 assert.deepEqual(out.coverage,{union_hands:3,matched_hands:1,left_only:1,right_only:1,both_numeric_ev:1,zero_combined_weight:1});
 assert.equal(out.items.find(x=>x.match_status==='left_only').right,null);assert.equal(out.items.find(x=>x.match_status==='matched').right.ev,'1.3');assert.equal(out.ev_unit,'unknown');assert.equal(out.ev_calculation_performed,false);
 const other=await f.write('AsTd7c_BTN_C_R50.csv',await fs.readFile(r));await assert.rejects(()=>compareCsv(f.config,{left_path:l,right_path:other,board:'AsTd7c'}),/same player/);
 await assert.rejects(()=>inspectCsv(f.config,{path:l,board:'As9d7c'}),/Filename board/);
});
test('reports invalid EV, board collisions, normalized duplicates and refuses join',async t=>{
 const f=await fixture(t),p=await f.write('bad.csv',header+'AhKsQcJd,0,0,0,NaN,0,1\nJdQcKsAh,0,0,0,1,0,1\nAsKhQdJc,1,100,1,2,0,1\n');
 const r=await inspectCsv(f.config,{path:p,board:'AsTd7c'});for(const code of ['invalid_ev','duplicate_physical_hand','board_collision'])assert.ok(r.issues.some(x=>x.code===code));
 const q=await f.write('valid.csv',header+'AcKhQdJc,1,100,1,2,0,1\n');await assert.rejects(()=>compareCsv(f.config,{left_path:p,right_path:q,board:'AsTd7c'}),/validation issues/);
});
test('missing EV column, UTF16, malformed CSV and configured root boundary',async t=>{
 const f=await fixture(t),p=await f.write('utf16.csv',Buffer.concat([Buffer.from([255,254]),Buffer.from('hand,weight\nAhKsQcJd,0\n','utf16le')]));
 const r=await inspectCsv(f.config,{path:p,board:'AsTd7c'});assert.equal(r.encoding,'utf-16le');assert.equal(r.columns_summary.ev.present,false);assert.equal(r.samples[0].values.ev,null);
 const bad=await f.write('quote.csv','hand,weight\n"AhKsQcJd,0');await assert.rejects(()=>inspectCsv(f.config,{path:bad,board:'AsTd7c'}),/Unclosed/);
 const duplicate=await f.write('headers.csv','hand,weight,ev,ev\nAhKsQcJd,0,1,2');await assert.rejects(()=>inspectCsv(f.config,{path:duplicate,board:'AsTd7c'}),/Duplicate/);
 await assert.rejects(()=>inspectCsv({...f.config,inputRoots:[path.join(f.dir,'restricted')]},{path:p,board:'AsTd7c'}),/outside/);
});
test('tool schemas route to real service, enforce bounds and expose only inspection semantics',async t=>{
 const f=await fixture(t),p=await f.write('a.csv',header+'AhKsQcJd,0,0,0,1,0,1\n');
 const svc=new ResearchService(f.config,{}),registered=new Map();registerResearchTools((name,description,shape,fn,readOnly=true)=>registered.set(name,{schema:z.object(shape).strict(),fn,readOnly}),svc);
 const tool=registered.get('research_inspect_csv');assert.equal(tool.readOnly,true);assert.throws(()=>tool.schema.parse({path:p,board:'AsTd7c',limit:201}));
 const out=await tool.fn(tool.schema.parse({path:p,board:'AsTd7c'}));assert.equal(out.zero_weight_rows,1);assert.equal(out.ev_semantics,'unverified');assert.equal(registered.get('research_match_action_csvs').readOnly,true);
});
test('MCP client discovers and calls the new tools',async t=>{
 const {Client,InMemoryTransport}=await import('@modelcontextprotocol/client');
 const {buildServer}=await import('../mcp/server-core.mjs');const {Service}=await import('../mcp/service.mjs');
 const f=await fixture(t),p=await f.write('a.csv',header+'AhKsQcJd,0,0,0,1.24,-.1,1\n');
 const server=buildServer(new Service(f.config)),client=new Client({name:'ev-test',version:'1'}),[ct,st]=InMemoryTransport.createLinkedPair();
 await server.connect(st);await client.connect(ct);t.after(async()=>{await client.close();await server.close();});
 const tools=await client.listTools();assert.equal(tools.tools.length,36);
 const r=await client.callTool({name:'research_inspect_csv',arguments:{path:p,board:'AsTd7c'}});assert.ok(!r.isError);assert.equal(r.structuredContent.samples[0].values.ev,'1.24');
 const bad=await client.callTool({name:'research_match_action_csvs',arguments:{left_path:p,right_path:p,board:'AsTd7c'}});assert.equal(bad.isError,true);
 const q=await f.write('b.csv',header+'JdQcKsAh,1,100,1,1.34,.1,1\n');
 const ev=await client.callTool({name:'research_analyze_action_evs',arguments:{board:'AsTd7c',actions:[{action_id:'check',path:p},{action_id:'bet',path:q}],ev_definition:'action_minus_best_other_bb',complete_action_menu:true,common_weight_scale:true}});
 assert.ok(!ev.isError);assert.equal(ev.structuredContent.actions[0].mean_loss_bb_on_covered_range,.1);assert.equal(ev.structuredContent.totals.complete_hands,1);

});
