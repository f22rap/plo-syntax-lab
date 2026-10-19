import fs from 'node:fs/promises';
import path from 'node:path';
import {randomUUID} from 'node:crypto';
import {readCsv} from './csv-ev-inspection.mjs';
import {boardModel,SLOTS} from './strategy-features.mjs';
import {analyzeBoards,POLICY} from './strategy-analysis.mjs';
import {hash,fileHash,inputFile,checkAbort,root,sourceCommit} from './runtime.mjs';
import {renderStrategyPdf} from './strategy-pdf.mjs';
const idPattern=/^[a-f0-9-]{36}$/;
const suffix='.strategy.json';
const page=(items,{offset=0,limit=50}={})=>({total:items.length,offset,items:items.slice(offset,offset+limit),next_offset:offset+limit<items.length?offset+limit:null});
async function output(config){if(await fs.realpath(config.outputRoot)!==config.outputRoot)throw Error('Output root changed.');return config.outputRoot;}
export async function collectStrategyBoards(config,catalog,spotIds=[],{signal,progress=()=>{}}={}){
 if(catalog.scenario?.game!=='PLO4'||catalog.scenario?.players!==2)throw Error('Select a heads-up PLO4 catalog.');
 if(new Set(spotIds).size!==spotIds.length||spotIds.some(id=>!catalog.spots.some(s=>s.spot_id===id)))throw Error('Unknown or duplicate spot ID.');
 const spots=catalog.spots.filter(s=>!spotIds.length||spotIds.includes(s.spot_id));if(!spots.length)throw Error('No selected spots.');
 const nodes=new Set(spots.map(s=>s.node));if(nodes.size!==1)throw Error('Select one decision node across boards.');
 const actionMenus=new Set(spots.map(s=>JSON.stringify(s.hero_actions.map(a=>[a.kind,a.pot_fraction]).sort())));if(actionMenus.size!==1)throw Error('Action menus or bet sizes differ across boards.');
 const boards=[],sources=[],paths=new Set();
 for(const spot of spots){checkAbort(signal);const model=boardModel(spot.board);
  if(boards.some(b=>b.key===model.key))throw Error('Select one physical board per rank texture.');
  if(spot.all_actions_common_scale!==true||spot.hero_actions.length!==2||spot.hero_actions.filter(a=>a.kind==='check').length!==1||spot.hero_actions.filter(a=>a.kind==='bet').length!==1)throw Error('This first-action report requires a confirmed complete common-scale menu of check plus one bet.');
  const files=[];
  for(const a of spot.hero_actions){checkAbort(signal);if(!/^[a-f0-9]{64}$/.test(a.source.sha256))throw Error('Revalidate catalog sources to record SHA-256 hashes.');
   const f=await readCsv(config,{path:a.source.path,board:spot.board,expected_sha256:a.source.sha256});if(f.summary.issue_count)throw Error(`CSV validation issues: ${spot.spot_id}/${a.action_id}`);
   if(paths.has(f.summary.path))throw Error('Duplicate action CSV source.');paths.add(f.summary.path);files.push({...f,action:a});sources.push({spot_id:spot.spot_id,action_id:a.action_id,kind:a.kind,...f.summary});
  }
  const named=files.map(f=>f.summary.filename_node);if(named.some(Boolean)&&(named.some(x=>!x)||new Set(named.map(x=>`${x.player}/${x.history}`)).size!==1||new Set(named.map(x=>x.action)).size!==2))throw Error('CSV filename nodes differ.');
  const hands=[...new Set(files.flatMap(f=>[...f.byHand.keys()]))].sort(),atoms=new Map();let done=0;
  for(const hand of hands){if(++done%2048===0){await new Promise(r=>setImmediate(r));checkAbort(signal);}
   const weights=files.map(f=>Number(f.byHand.get(hand)?.values.weight??0)),weight=weights.reduce((s,w)=>s+w,0);if(weight===0)continue;
   const h=model.handFeatures(hand);if(!h)continue;
   const bet=weights[files.findIndex(f=>f.action.kind==='bet')],key=SLOTS.map(f=>h.features[f]).join(',');let atom=atoms.get(key);if(!atom){atom={features:h.features,weight:0,bet:0,n:0};atoms.set(key,atom);}atom.weight+=weight;atom.bet+=bet;atom.n++;
  }
  if(!atoms.size)throw Error(`No positive Ahit range weight on ${model.key}.`);
  boards.push({board:model.board,key:model.key,sd_ranks:model.sd_ranks,oesd_cores:model.oesd_cores,atoms:[...atoms.values()]});progress({message:'Ahit条件を集計中',done:boards.length,total:spots.length,board:model.key});
 }
 return {boards,sources,node:spots[0].node,action_menu:spots[0].hero_actions.map(({kind,label,pot_fraction})=>({kind,label,pot_fraction}))};
}
export async function buildStrategyReport(service,a,job){
 const w=await service.workspace(),catalog=w.catalogs.find(c=>c.catalog_id===a.catalog_id);
 if(!catalog||catalog.revision!==a.catalog_revision)throw Error('Catalog changed or missing; select the current revision.');
 const fingerprint=hash(JSON.stringify(catalog));
 const data=await collectStrategyBoards(service.config,catalog,a.spot_ids,{signal:job.controller.signal,progress:p=>job.progress=p});
 checkAbort(job.controller.signal);job.progress={message:'ボード分類・4条件を選定中'};
 const analysis=analyzeBoards(data.boards,{mode:a.classification,block_count:a.block_count,blocks:a.blocks,threshold:0.5});
 await new Promise(r=>setImmediate(r));checkAbort(job.controller.signal);
 const latest=(await service.workspace()).catalogs.find(c=>c.catalog_id===a.catalog_id);if(hash(JSON.stringify(latest))!==fingerprint)throw Error('Catalog changed during analysis.');
 for(const source of data.sources){checkAbort(job.controller.signal);if(await fileHash(await inputFile(service.config,source.path))!==source.sha256)throw Error('CSV changed during analysis.');}
 const report_id=randomUUID(),modules=['mcp/strategy-features.mjs','mcp/strategy-analysis.mjs','mcp/strategy-report.mjs','mcp/strategy-pdf.mjs','assets/fonts/NotoSansJP-Regular.ttf'];
 const report={report_id,created_utc:new Date().toISOString(),...analysis,provenance:{source_commit:await sourceCommit(),modules:Object.fromEntries(await Promise.all(modules.map(async f=>[f,await fileHash(path.join(root,f))]))),catalog_id:catalog.catalog_id,catalog_revision:catalog.revision,catalog_sha256:fingerprint,scenario:catalog.scenario,node:data.node,action_menu:data.action_menu,sources:data.sources},notes:['登録した範囲の頻度による記述的な簡易戦略。EV損失や未登録ボードの最適戦略は未検証。','4・8アウツは全アウツがナッツの場合だけナッツ。9+は全ナッツ／一部ナッツ／ナッツなしを区別し、存在する行のみ表示。','全SDブロック条件とPPのSDブロック区別は、自分のSDなしの場合だけ評価。対象外は「いいえ」と区別。','BDFDは1枠の3状態。2+2スートは「はい／いいえ」に含む。','PPはランクの2枚以上（3枚含む）。22優先。SDブロックPPはペア自身のランクで判断。','SDを先に表示するのは論理上の入口であり、単条件の説明力が最大という意味ではない。','頻度はJavaScript数値による近似集計。判定は表示用の丸め前の平均に対して50%以上。']};
 const directory=await output(service.config),file=path.join(directory,report_id+suffix);
 // Compute the PDF before committing either file; failed/cancelled jobs do not advertise a report.
 const pdf=a.create_pdf===false?null:await renderStrategyPdf(report);checkAbort(job.controller.signal);
 let pdfArtifact=null;
 if(pdf){const artifact_id=randomUUID(),pdfPath=path.join(directory,artifact_id+'.pdf');await fs.writeFile(pdfPath,pdf,{flag:'wx'});pdfArtifact={artifact_id,path:pdfPath,uri:'plo://artifacts/'+artifact_id,mimeType:'application/pdf',bytes:pdf.length,report_id};}
 try{await fs.writeFile(file,JSON.stringify(report,null,2)+'\n',{flag:'wx'});}catch(e){if(pdfArtifact)await fs.unlink(pdfArtifact.path);throw e;}
 if(pdfArtifact)service.legacy.artifacts.set(pdfArtifact.artifact_id,pdfArtifact);
 return {report_id,policy:POLICY,template_version:report.template_version,board_count:data.boards.length,blocks:report.groups.map(g=>({id:g.id,boards:g.boards,selected:g.selected})),threshold:0.5,pdf:pdfArtifact,next_call:'research_get_strategy_report'};
}
export async function readStrategyReport(config,id){
 if(!idPattern.test(id))throw Error('Invalid report ID.');const directory=await output(config),file=path.join(directory,id+suffix);
 if(await fs.realpath(file)!==file||(await fs.stat(file)).size>32*1024*1024)throw Error('Invalid strategy report file.');
 const r=JSON.parse(await fs.readFile(file,'utf8'));if(r.report_id!==id||r.policy!==POLICY)throw Error('Strategy report identity or policy mismatch.');return r;
}
export async function getStrategyReport(config,a){const r=await readStrategyReport(config,a.report_id);
 if(a.view==='metadata'){const {groups,...metadata}=r;return {...metadata,blocks:groups.map(({id,boards,selected,stages,selection,candidate_combinations})=>({id,boards,selected,stages,selection,candidate_combinations}))};}
 const groups=r.groups.filter(g=>!a.block_id||g.id===a.block_id);if(!groups.length)throw Error('Unknown block ID.');
 const items=a.view==='board'?groups.flatMap(g=>g.board_results.map(b=>({block_id:g.id,selected:g.selected,...b}))):groups.flatMap(g=>g.cells.map(c=>({block_id:g.id,selected:g.selected,...c})));
 return {report_id:r.report_id,...page(items,a)};
}
export async function listStrategyReports(config,a){const directory=await output(config),items=[];for(const name of (await fs.readdir(directory)).filter(n=>n.endsWith(suffix)&&idPattern.test(n.slice(0,-suffix.length)))){const r=await readStrategyReport(config,name.slice(0,-suffix.length));items.push({report_id:r.report_id,created_utc:r.created_utc,catalog_id:r.provenance.catalog_id,board_count:r.board_definitions.length,block_count:r.groups.length,policy:r.policy});}items.sort((a,b)=>b.created_utc.localeCompare(a.created_utc));return page(items,a);}
export async function exportStrategyReport(service,a){const r=await readStrategyReport(service.config,a.report_id),artifact_id=randomUUID(),directory=await output(service.config),file=path.join(directory,artifact_id+'.'+a.format),content=a.format==='pdf'?await renderStrategyPdf(r):JSON.stringify(r,null,2)+'\n';await fs.writeFile(file,content,{flag:'wx'});const artifact={artifact_id,report_id:r.report_id,path:file,uri:'plo://artifacts/'+artifact_id,mimeType:a.format==='pdf'?'application/pdf':'application/json',bytes:Buffer.byteLength(content)};service.legacy.artifacts.set(artifact_id,artifact);return artifact;}
