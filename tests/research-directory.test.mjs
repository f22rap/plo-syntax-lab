import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {scanDirectory,directoryCatalog} from '../mcp/research-directory.mjs';
import {makeDirectoryFixture,directoryScenario} from './directory-fixture.mjs';

async function fixture(t){const root=await fs.mkdtemp(path.join(os.tmpdir(),'plo-directory-'));t.after(()=>fs.rm(root,{recursive:true,force:true}));await makeDirectoryFixture(root);return {root,config:{inputRoots:[root],maxCsvBytes:2000000}};}
const args=(s,folder='vs_check')=>({node_id:s.nodes.find(n=>n.folder===folder).node_id,catalog_id:'imported',name:'Imported Ahi',scenario:directoryScenario,confirm_profile:'plo-post-tree-v1',all_actions_common_scale:true});

test('User tree: Ahi filter, all 10 nodes, exact previous opponent node, sizes and all-in',async t=>{
 const {root,config}=await fixture(t),s=await scanDirectory(config,{directory:root});
 assert.equal(s.board_count,2);assert.equal(s.non_ahi_count,2);assert.equal(s.nodes.length,10);assert.equal(s.spots.length,20);assert.deepEqual(s.issues,[]);assert.ok(s.spots.every(x=>!x.problems.length));
 const one=folder=>s.spots.find(x=>x.board==='AsKd7c'&&x.folder===folder);
 assert.equal(one('vs_check').opponent_node,'OOP/donk/ROOT');assert.equal(one('vs_check').spot.opponent_sources.length,2);
 assert.ok(one('vs_check').spot.opponent_sources.every(x=>x.path.includes('OOP'+path.sep+'donk')));
 assert.equal(one('vs_bet').opponent_node,'IP/vs_check/C');assert.equal(one('vs_XR').opponent_node,'OOP/vs_bet/C-R50');
 assert.equal(one('vs_check').spot.hero_actions.find(x=>x.kind==='bet').pot_fraction,'0.5');assert.equal(one('vs_bet').spot.hero_actions.find(x=>x.kind==='raise').pot_fraction,'0.75');
 assert.equal(one('vs_XRraise').spot.hero_actions.find(x=>x.label==='All-in').pot_fraction,'allin');assert.equal(one('vs_L4').spot.hero_actions.length,1);
 assert.equal(one('donk').eq_status,'opponent_root_range_not_exported');assert.deepEqual(one('donk').spot.opponent_sources,[]);
 const c=directoryCatalog(s,args(s));assert.equal(c.spots.length,2);assert.match(c.scenario.equity_reference,/BEFORE/);assert.ok(c.spots[0].hero_actions.every(x=>x.source.sha256.length===64));
 assert.throws(()=>directoryCatalog(s,{...args(s),scenario:{...directoryScenario,hero_position:'CO'}}),/positions/);
 assert.throws(()=>directoryCatalog(s,{...args(s),boards:['AsKd7c','AsKd7c']}),/duplicate/);
 assert.throws(()=>directoryCatalog(s,{...args(s,'donk'),scenario:{...directoryScenario,hero_position:'CO',villain_position:'BTN'}}),/opponent_root/);
 assert.equal(directoryCatalog(s,{...args(s,'donk'),scenario:{...directoryScenario,hero_position:'CO',villain_position:'BTN'},allow_frequency_only:true}).spots.length,2);
 assert.equal((await scanDirectory(config,{directory:root,ahi_only:false})).board_count,4);
});
test('Malformed sibling blocks its whole denominator and use as an opponent source',async t=>{
 const {root,config}=await fixture(t);await fs.writeFile(path.join(root,'AsKd7c/OOP/donk/unknown.csv'),'bad');
 const s=await scanDirectory(config,{directory:root});assert.ok(s.issues.length);const row=s.spots.find(x=>x.board==='AsKd7c'&&x.folder==='donk');assert.ok(row.problems.includes('unreadable_or_unknown_sibling'));assert.equal(s.spots.find(x=>x.board==='AsKd7c'&&x.folder==='vs_check').eq_status,'opponent_pre_action_node_missing_or_ambiguous');assert.throws(()=>directoryCatalog(s,args(s)),/blocked boards/);
 assert.equal(directoryCatalog(s,{...args(s),boards:['Ah8h3c']}).spots.length,1);
});
test('Unknown positions, duplicate physical boards and mixed histories are blocked',async t=>{
 const {root,config}=await fixture(t);await makeDirectoryFixture(root,['KdAs7c']);
 const dir=path.join(root,'Ah8h3c/IP/vs_check');await fs.rename(path.join(dir,'Ah8h3c_BTN_C_C.csv'),path.join(dir,'Ah8h3c_SB_R75_C.csv'));
 const s=await scanDirectory(config,{directory:root});assert.ok(s.spots.find(x=>x.board==='AsKd7c').problems.includes('duplicate_physical_board'));assert.ok(s.spots.find(x=>x.board==='Ah8h3c'&&x.folder==='vs_check').problems.includes('mixed_position_or_history_in_node'));
});
test('Scope, linked paths and changed file inventory are detected',async t=>{
 const {root,config}=await fixture(t);await assert.rejects(()=>scanDirectory(config,{directory:path.dirname(root)}),/outside/);
 const first=await scanDirectory(config,{directory:root}),p=first.spots[0].spot.hero_actions[0].source.path;await fs.appendFile(p,'\n');assert.notEqual((await scanDirectory(config,{directory:root})).fingerprint,first.fingerprint);
 const link=path.join(root,'alias');try{await fs.symlink(path.dirname(root),link,'junction');await assert.rejects(()=>scanDirectory(config,{directory:link}),/Linked/);}catch(e){if(e.code!=='EPERM')throw e;}
});
