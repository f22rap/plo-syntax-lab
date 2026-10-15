import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {ConditionGenerator,createPresetSet,listPresets,normalizeSet} from '../mcp/generated-conditions.mjs';
const require=createRequire(import.meta.url),R=require('../src/research-recipes.cjs'),G=require('../src/research-generation.cjs');
const P=require('../src/lab/engine.js'),defaults=require('../src/defaults.js'),catalog=require('../src/default-labels.json');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const clone=x=>structuredClone(x),fixed=rank=>({type:'fixed',rank}),relative=position=>({type:'board',position});
const setOf=conditions=>({set_id:'test_set',revision:0,name:'検証条件',conditions});
const recipe=(condition_id,changes={},extra='*')=>({condition_id,label:condition_id,enabled:true,kind:'generated',syntax:'',by_board:{},generator:{recipe_version:1,ruleset:'plo4-flop-v1',source:'filter',filter:{...R.defaultFilter(),...changes},additional_syntax:extra}});
const engines=new Map();
async function engine(board){if(!engines.has(board)){const e=new P.Engine();await e.prepare(board);engines.set(board,e);}return engines.get(board);}
const byId=(rows,id)=>rows.find(r=>r.condition_id===id);

test('192条件の名称・順序・安定IDを維持し、未定義26条件を明示する',()=>{
 const all=listPresets();assert.equal(all.length,192);assert.equal(new Set(all.map(p=>p.preset_id)).size,192);
 assert.deepEqual(all.map(({cell,label})=>({cell,label})),catalog);
 assert.equal(all.filter(p=>p.supported).length,166);
 for(const p of all.filter(p=>!p.supported)){assert.ok(p.reason);assert.match(p.label,/pp all|blocker/i);assert.throws(()=>createPresetSet({preset_ids:[p.preset_id]}));}
 const set=createPresetSet({preset_ids:['aa.sd.bdsd','set.top.all','set.top.all']});
 assert.deepEqual(set.conditions.map(c=>c.generator.preset_id),['set.top.all','aa.sd.bdsd']);
 const c=clone(set.conditions[0]),hash=R.conditionHash(c);c.label='任意の日本語名';c.condition_id='renamed';c.enabled=false;assert.equal(R.conditionHash(c),hash);
 all[0].label='外部から変更';assert.equal(listPresets()[0].label,'Tset all');
 assert.equal(listPresets({query:'Tsetall'})[0].preset_id,'set.top.all');
});

test('不明な設定・重複・矛盾は黙って読み替えず拒否する',()=>{
 const c=recipe('valid');R.validateCondition(c);
 for(const change of [x=>{x.generator.filter.fd='anything';},x=>{x.generator.filter.typo=true;},x=>{delete x.generator.filter.sd;},x=>{x.generator.filter.clean='all';x.generator.filter.bdfd='none';},x=>{x.generator.filter.bdsd='bdsd8';x.generator.filter.sd='sd';},x=>{x.generator.filter.blockers=[{rank:fixed('A'),mode:'one'},{rank:fixed('A'),mode:'no'}];},x=>{x.syntax='AA';},x=>{x.generator.additional_syntax='('; }]){
  const bad=clone(c);change(bad);assert.throws(()=>R.validateCondition(bad));
 }
 assert.throws(()=>normalizeSet(setOf([c,c])));
 assert.equal(R.canonicalBoard('7s kd As'),'AsKd7s');assert.equal(R.canonicalBoard('Ad7cAs'),'AsAd7c');
 for(const b of ['AsAs7c','AsKd7c2d','AsKd','As Kd 7x'])assert.throws(()=>R.canonicalBoard(b));
 const legacy={condition_id:'per_board',label:'既存',enabled:true,kind:'per_board',syntax:'',by_board:{'7cKdAs':'AK'}};
 assert.deepEqual(normalizeSet(setOf([legacy])).conditions[0].by_board,{AsKd7c:'AK'});
 legacy.by_board.AsKd7c='AA';assert.throws(()=>normalizeSet(setOf([legacy])));
 const examples=require('../docs/generated-condition.examples.json');assert.equal(normalizeSet(setOf(examples)).conditions.length,12);
});

test('3ボード×192条件が既存生成と一致し、名前変更でも同じ集合になる',async()=>{
 const selected=setOf(listPresets().map((p,i)=>({condition_id:'p'+i,label:'ユーザー条件 '+i,enabled:true,kind:'generated',syntax:'',by_board:{},generator:{recipe_version:1,ruleset:'plo4-flop-v1',source:'preset',preset_id:p.preset_id,preset_revision:1,additional_syntax:'*'}})));
 for(const board of ['JsTd7c','AsKd7s','AsAd7c']){
  const e=await engine(board),expected=await defaults(P,e,catalog),actual=(await G.generatePrepared(e,selected)).rows;
  assert.equal(actual.length,192);
  for(let i=0;i<192;i++){
   const p=listPresets()[i],a=actual[i],b=expected[i];assert.equal(a.label,'ユーザー条件 '+i);
   if(!p.supported){assert.equal(a.status,'unsupported_recipe');assert.equal(a.legal_hand_count,null);assert.equal(a.syntax,'');continue;}
   assert.equal(a.status,b.count?'ready':'empty_condition',`${board} ${p.label}: ${a.reason}`);
   assert.equal(a.syntax,b.syntax,board+' '+p.label);assert.equal(a.legal_hand_count,b.count);assert.equal(a.verified,true);
  }
  if(board==='JsTd7c'){assert.equal(actual[0].syntax,'JJ!(98)');assert.equal(actual[0].legal_hand_count,3103);const mid=actual.find(r=>r.resolved_preset?.preset_id==='set.middle.all');assert.equal(mid.syntax,'TT!(98,JJ)');assert.equal(mid.legal_hand_count,3094);}
  if(board==='AsKd7s'){assert.equal(actual[0].legal_hand_count,3151);assert.equal(actual[0].syntax,'AA');}
  if(board==='AsAd7c')assert.equal(actual.find(r=>r.resolved_preset?.preset_id==='set.middle.all').reason_code,'relative_rank_unavailable');
 }
});

test('相対役・固定AA・第三FD・BDSD・追加ANDを全合法ハンドに照合する',async()=>{
 const e=await engine('AsKd7s'),conditions=[
  recipe('top',{role:{type:'set',rank:relative('top')}}),
  recipe('aa',{pocket:{type:'rank',rank:fixed('A')}}),
  recipe('third',{fd:'fdThird'}),recipe('other',{fd:'fdLow'}),
  recipe('bdsd4',{bdsd:'bdsd4',sd:'none'}),recipe('bdsd8',{bdsd:'bdsd8',sd:'none'}),recipe('bdsd9',{bdsd:'bdsd9',sd:'none'}),
  recipe('and',{fd:'fdNut'},'AA'),
  recipe('top_one',{blockers:[{rank:relative('top'),mode:'one'}]}),
  recipe('conflict',{blockers:[{rank:relative('top'),mode:'one'},{rank:fixed('A'),mode:'no'}]}),
  recipe('empty_and',{pocket:{type:'rank',rank:fixed('A')}},'!A'),
  recipe('dual',{fd:'dualFd'})
 ];
 const rows=(await G.generatePrepared(e,setOf(conditions))).rows;
 for(const row of rows){assert.ok(['ready','empty_condition'].includes(row.status),row.reason);const f=row.resolved_filter,bits=row.syntax?e.expressionSet(row.syntax):null,additional=P.compile(row.additional_syntax);let count=0;
  for(let i=0;i<e.hands.length;i++){
   const h=P.unpack(e.hands[i]),expected=e.matches(i,f)&&additional(h);const actual=!!(bits&&(bits[i>>>5]&(1<<(i&31))));assert.equal(actual,expected,row.label+' '+h.map(P.card).join(''));if(!actual)continue;count++;
   if(['bdsd4','bdsd8','bdsd9'].includes(row.condition_id)){assert.equal(e.flags[i]&P.F.sd,0);assert.ok(e.flags[i]&P.F.bdsd);assert.ok(e.roles[e.handRoles[i]].cat<4);}
   if(row.condition_id==='third'){assert.ok(h.some(c=>P.card(c)==='Js'));assert.ok(!h.some(c=>['Ks','Qs'].includes(P.card(c))));assert.ok(h.filter(c=>P.suit(c)===0).length>=2);}
   if(row.condition_id==='other')assert.equal(e.flags[i]&P.F.fdThird,0);
   if(row.condition_id==='aa')assert.ok(h.filter(c=>P.rank(c)===14).length>=2);
  }
  assert.equal(count,row.legal_hand_count);
 }
 assert.equal(byId(rows,'top').syntax,'AA');assert.equal(byId(rows,'aa').legal_hand_count,3151);
 assert.ok(byId(rows,'third').legal_hand_count>0);assert.equal(byId(rows,'conflict').status,'empty_condition');
 assert.equal(byId(rows,'empty_and').reason_code,'empty_intersection');assert.equal(byId(rows,'dual').status,'empty_condition');
});

test('Khi・ペア・モノトーンでも相対指定と未設定/未対応/空集合を区別する',async()=>{
 const conditions=[recipe('top',{role:{type:'set',rank:relative('top')}}),recipe('mid',{role:{type:'set',rank:relative('middle')}}),recipe('aa',{pocket:{type:'rank',rank:fixed('A')}}),recipe('fd',{fd:'fdNut'}),recipe('flush3',{role:{type:'flush',tier:'third'}}),
  {condition_id:'missing',label:'未設定',enabled:true,kind:'per_board',syntax:'',by_board:{AsKd7s:'AK'}},
  {...recipe('unknown'),generator:{...recipe('unknown').generator,recipe_version:2}},
  {condition_id:'literal',label:'固定AA',enabled:true,kind:'literal',syntax:'AA',by_board:{}},
  {condition_id:'defined',label:'ボード別AA',enabled:true,kind:'per_board',syntax:'',by_board:{Kh9h3c:'AA',AsAd7c:'AA',AsKs7s:'AA'}}
 ];
 for(const board of ['Kh9h3c','AsAd7c','AsKs7s']){
  const e=await engine(board),rows=(await G.generatePrepared(e,setOf(conditions))).rows;
  assert.equal(byId(rows,'literal').legal_hand_count,byId(rows,'aa').legal_hand_count);assert.equal(byId(rows,'defined').legal_hand_count,byId(rows,'aa').legal_hand_count);
  assert.equal(byId(rows,'missing').status,'missing_definition');assert.equal(byId(rows,'missing').legal_hand_count,null);assert.equal(byId(rows,'unknown').status,'unsupported_recipe');
  if(board==='Kh9h3c'){assert.equal(byId(rows,'top').syntax,'KK');assert.equal(byId(rows,'top').legal_hand_count,3151);assert.equal(byId(rows,'aa').syntax,'AA');assert.equal(byId(rows,'aa').legal_hand_count,6121);const match=P.compile(byId(rows,'fd').syntax);for(let i=0;i<e.hands.length;i++){const h=P.unpack(e.hands[i]);assert.equal(match(h),h.some(c=>P.card(c)==='Ah')&&h.filter(c=>P.suit(c)===1).length>=2);}}
  if(board==='AsAd7c'){assert.equal(byId(rows,'mid').reason_code,'relative_rank_unavailable');assert.equal(byId(rows,'top').status,'empty_condition');}
  if(board==='AsKs7s'){assert.equal(byId(rows,'fd').status,'empty_condition');assert.ok(byId(rows,'flush3').legal_hand_count>0);const match=P.compile(byId(rows,'flush3').syntax);for(let i=0;i<e.hands.length;i++)if(match(P.unpack(e.hands[i]))){const h=P.unpack(e.hands[i]);assert.ok(h.some(c=>P.card(c)==='Ts'));assert.ok(!h.some(c=>['Qs','Js'].includes(P.card(c))));}}
 }
});

test('全体・否定集合の末尾パディングを合法ハンド数に含めない',async()=>{
 const e=await engine('AsKd7s'),conditions=['*','!AA','(*):(!AA)'].map((syntax,i)=>({condition_id:'padding'+i,label:'padding'+i,enabled:true,kind:'literal',syntax,by_board:{}}));
 const rows=(await G.generatePrepared(e,setOf(conditions))).rows;
 assert.equal(e.hands.length,211876);assert.equal(rows[0].legal_hand_count,211876);
 for(let i=0;i<rows.length;i++){const match=P.compile(conditions[i].syntax);let count=0;for(const hand of e.hands)if(match(P.unpack(hand)))count++;assert.equal(rows[i].legal_hand_count,count);}
});

test('ワーカーはボードごと1回解析し、改名後もキャッシュを再利用する',async()=>{
 const generator=new ConditionGenerator(),set=createPresetSet({preset_ids:['set.top.all','set.middle.all']}),input={condition_set:set,boards:['7sKdAs','AsKd7s']};
 const first=await generator.generate(input);assert.equal(first.status,'ready');assert.equal(first.statistics.board_analyses,1);assert.equal(first.rows.length,2);
 const second=await generator.generate(input);assert.equal(second.statistics.board_analyses,0);assert.equal(second.statistics.cache_hits,2);assert.equal(first.snapshot_hash,second.snapshot_hash);
 set.conditions[0].label='変更した表示名';set.conditions[0].condition_id='new_id';const renamed=await generator.generate(input);assert.equal(renamed.statistics.board_analyses,0);assert.equal(renamed.rows[0].label,'変更した表示名');assert.equal(renamed.rows[0].condition_id,'new_id');assert.equal(renamed.rows[0].syntax,first.rows[0].syntax);
 renamed.rows[0].syntax='*';assert.equal((await generator.generate(input)).rows[0].syntax,'AA');
 set.conditions[0].generator.additional_syntax='ss';const changed=await generator.generate(input);assert.equal(changed.statistics.board_analyses,1);assert.equal(changed.statistics.cache_hits,1);assert.ok(changed.rows[0].legal_hand_count<first.rows[0].legal_hand_count);
 assert.match(changed.versions.engine_hash,/^[a-f0-9]{64}$/);assert.match(changed.snapshot_hash,/^[a-f0-9]{64}$/);
});

test('中断時は完了したボードだけ返し、キャッシュ上限を守る',async()=>{
 const generator=new ConditionGenerator({maxCacheBytes:0}),controller=new AbortController(),set=createPresetSet({preset_ids:['set.top.all']});
 const result=await generator.generate({condition_set:set,boards:['AsKd7s','Kh9h3c']},{signal:controller.signal,progress:p=>{if(p.stage==='board_complete')controller.abort();}});
 assert.equal(result.status,'cancelled');assert.equal(result.statistics.completed_boards,1);assert.equal(result.rows.length,1);assert.equal(generator.cache.size,0);assert.equal(generator.cacheBytes,0);
 const before=await generator.generate({condition_set:set,boards:['AsKd7s']},{signal:controller.signal});assert.equal(before.rows.length,0);assert.equal(before.statistics.board_analyses,0);
 const during=new AbortController();const stopped=await generator.generate({condition_set:set,boards:['AsKd7s']},{signal:during.signal,progress:()=>during.abort()});assert.equal(stopped.status,'cancelled');assert.equal(stopped.rows.length,0);
});

test('保存プレビューを再読込でき、CLIは既存ファイルを上書きしない',async()=>{
 const tmp=await fs.mkdtemp(path.join(os.tmpdir(),'plo-generated-'));
 try{
  const list=spawnSync(process.execPath,['mcp/preview-generated-conditions.mjs','--list','--query','Tsetall'],{cwd:root,encoding:'utf8'});assert.equal(list.status,0);assert.equal(JSON.parse(list.stdout)[0].preset_id,'set.top.all');
  const output=path.join(tmp,'preview.json'),existing='既存データ';await fs.writeFile(output,existing);
  const blocked=spawnSync(process.execPath,['mcp/preview-generated-conditions.mjs','--presets','set.top.all','--boards','AsKd7s','--output',output],{cwd:root,encoding:'utf8'});assert.equal(blocked.status,1);assert.match(blocked.stderr,/既に存在/);assert.equal(await fs.readFile(output,'utf8'),existing);
  const saved=setOf(require('../docs/generated-condition.examples.json'));await fs.writeFile(output,JSON.stringify({kind:'generated_condition_preview',condition_set:saved}));
  assert.deepEqual(normalizeSet(JSON.parse(await fs.readFile(output,'utf8')).condition_set),normalizeSet(saved));
  const workspace=path.join(tmp,'settings.json');await fs.writeFile(workspace,JSON.stringify({schema_version:2,revision:1,condition_sets:[saved,{...saved,set_id:'second',name:'別のセット'}],catalogs:[]}));
  const sets=spawnSync(process.execPath,['mcp/preview-generated-conditions.mjs','--set',workspace,'--list-sets'],{cwd:root,encoding:'utf8'});assert.equal(sets.status,0);assert.deepEqual(JSON.parse(sets.stdout).map(s=>s.set_id),['test_set','second']);
  const ambiguous=spawnSync(process.execPath,['mcp/preview-generated-conditions.mjs','--set',workspace,'--boards','AsKd7s','--output',path.join(tmp,'unused.json')],{cwd:root,encoding:'utf8'});assert.equal(ambiguous.status,1);assert.match(ambiguous.stderr,/--set-id/);
  const selected=spawnSync(process.execPath,['mcp/preview-generated-conditions.mjs','--set',workspace,'--set-id','second','--boards','AsKd7s','--output',output],{cwd:root,encoding:'utf8'});assert.equal(selected.status,1);assert.match(selected.stderr,/既に存在/);
 }finally{await fs.rm(tmp,{recursive:true,force:true});}
});
