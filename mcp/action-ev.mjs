import P from '../src/lab/engine.js';
import {readCsv} from './csv-ev-inspection.mjs';
import {inputFile,fileHash} from './runtime.mjs';
const n=x=>x==null||String(x).trim()===''?null:Number(x);
const finite=x=>x!==null&&Number.isFinite(x);
export const EV_DEFINITION='action_minus_best_other_bb';
// The caller confirms a complete action menu, common-scale weights and semantics.
// Missing actions in an export are not inferred to have zero EV.
export function evaluateHand(values,tolerance=0.015){
 const actions=values.map(v=>({weight:v?n(v.weight):0,ev_bb:v?n(v.ev):null,ev_diff_bb:v?n(v.ev_diff):null,ev_source:v&&finite(n(v.ev))?'csv':'missing',loss_bb:null}));
 const problems=[];const disagree=(a,b)=>Math.abs(a-b)>tolerance+1e-10;
 if(actions.length===2){
  for(let i=0;i<2;i++){const a=actions[i],b=actions[1-i];if(finite(a.ev_bb)&&finite(a.ev_diff_bb)&&!finite(b.ev_bb)){b.ev_bb=a.ev_bb-a.ev_diff_bb;b.ev_source='derived_from_other_ev_diff';}}
 }
 const allEv=actions.every(a=>finite(a.ev_bb));
 if(allEv){
  for(let i=0;i<actions.length;i++){const a=actions[i],other=Math.max(...actions.filter((_,j)=>j!==i).map(a=>a.ev_bb));if(finite(a.ev_diff_bb)&&disagree(a.ev_bb-other,a.ev_diff_bb))problems.push('ev_diff_mismatch:'+i);}
 }else{
  // Each EV+diff pair also identifies best overall EV, but not the identity or
  // EVs of the remaining alternatives. Validate agreement within rounding bounds.
  const anchors=actions.filter(a=>finite(a.ev_bb)&&finite(a.ev_diff_bb)).map(a=>a.ev_bb+Math.max(0,-a.ev_diff_bb));
  if(anchors.length>1&&Math.max(...anchors)-Math.min(...anchors)>2*tolerance+1e-10)problems.push('inconsistent_best_ev');
  for(let i=0;i<actions.length;i++){const a=actions[i];if(!finite(a.ev_bb)||!finite(a.ev_diff_bb))continue;const other=a.ev_bb-a.ev_diff_bb;
   if(actions.some((b,j)=>j!==i&&finite(b.ev_bb)&&b.ev_bb>other+tolerance+1e-10))problems.push('known_alternative_exceeds_implied_best:'+i);
  }
 }
 if(actions.filter(a=>finite(a.ev_diff_bb)&&a.ev_diff_bb>tolerance).length>1)problems.push('multiple_strict_best_actions');
 if(!problems.length){for(const a of actions){if(finite(a.ev_diff_bb))a.loss_bb=Math.max(0,-a.ev_diff_bb);else if(allEv)a.loss_bb=Math.max(0,Math.max(...actions.map(x=>x.ev_bb))-a.ev_bb);}}
 return {actions,problems,status:problems.length?'inconsistent':actions.every(a=>a.loss_bb!==null)?'complete':'partial',pre_action_weight:actions.reduce((s,a)=>s+a.weight,0)};
}
// Conditions are explicit board-resolved syntax; never substitute defaults.
export function conditionAccumulator(conditions,board,actionIds){
 if(conditions.length>100||new Set(conditions.map(c=>c.condition_id)).size!==conditions.length)throw Error('Select at most 100 unique conditions');
 const parsedBoard=P.parseBoard(board);
 const rows=conditions.map(c=>({...c,test:P.compile(c.syntax),hands:0,pre_action_weight:0,common_hands:0,common_weight:0,inconsistent_hands:0,actions:actionIds.map(action_id=>({action_id,action_weight:0,covered_weight:0,loss:0,common_loss:0}))}));
 return {
  add(hand,result){const cards=P.parseHand(hand,parsedBoard),w=result.pre_action_weight;
   for(const r of rows){if(!r.test(cards))continue;r.hands++;r.pre_action_weight+=w;if(result.status==='inconsistent')r.inconsistent_hands++;
    const common=result.actions.every(x=>x.loss_bb!==null)&&w>0;
    if(common){r.common_hands++;r.common_weight+=w;}
    result.actions.forEach((a,i)=>{const g=r.actions[i];g.action_weight+=a.weight;if(a.loss_bb!==null&&w>0){g.covered_weight+=w;g.loss+=w*a.loss_bb;}if(common)g.common_loss+=w*a.loss_bb;});
   }
  },
  finish(){return rows.map(({test,...r})=>({...r,status:r.pre_action_weight?'ok':'no_matching_weight',common_coverage_percent:r.pre_action_weight?100*r.common_weight/r.pre_action_weight:null,actions:r.actions.map(({loss,common_loss,...a})=>({...a,frequency_percent:r.pre_action_weight?100*a.action_weight/r.pre_action_weight:null,coverage_percent:r.pre_action_weight?100*a.covered_weight/r.pre_action_weight:null,mean_loss_bb_on_covered_range:a.covered_weight?loss/a.covered_weight:null,mean_loss_bb_on_common_range:r.common_weight?common_loss/r.common_weight:null}))}));}
 };
}
export async function analyzeActionCsvs(config,a){
 if(a.ev_definition!==EV_DEFINITION||a.complete_action_menu!==true||a.common_weight_scale!==true)throw Error('Explicit EV definition, complete action menu and common weight scale are required');
 if(a.actions.length<2||a.actions.length>3||new Set(a.actions.map(x=>x.action_id)).size!==a.actions.length)throw Error('Select 2 or 3 unique actions');
 const grouped=conditionAccumulator(a.conditions??[],a.board,a.actions.map(x=>x.action_id));
 const files=[];
 for(const action of a.actions){const f=await readCsv(config,{path:action.path,board:a.board,expected_sha256:action.expected_sha256});if(f.summary.issue_count)throw Error('Inspect CSV issues before EV analysis: '+action.action_id);files.push(f);}
 if(new Set(files.map(f=>f.summary.path)).size!==files.length)throw Error('Duplicate action CSV');
 const nodes=files.map(f=>f.summary.filename_node).filter(Boolean);
 if(nodes.some(x=>x.player!==nodes[0].player||x.history!==nodes[0].history)||new Set(nodes.map(x=>x.action)).size!==nodes.length)throw Error('CSV actions belong to different nodes or duplicate actions');
 const hands=[...new Set(files.flatMap(f=>[...f.byHand.keys()]))].sort(),items=[];
 const totals={hands:hands.length,pre_action_weight:0,inconsistent_hands:0,inconsistent_weight:0,zero_weight_hands:0,complete_hands:0,partial_hands:0};
 const aggregates=a.actions.map(x=>({action_id:x.action_id,covered_hands:0,covered_weight:0,weighted_loss:0,csv_ev_hands:0,derived_ev_hands:0,missing_ev_hands:0}));
 for(const hand of hands){const result=evaluateHand(files.map(f=>f.byHand.get(hand)?.values??null),a.tolerance_bb??0.015);const w=result.pre_action_weight;totals.pre_action_weight+=w;if(w===0)totals.zero_weight_hands++;
  totals[result.status+'_hands']++;if(result.status==='inconsistent')totals.inconsistent_weight+=w;
  result.actions.forEach((x,i)=>{const g=aggregates[i];g[x.ev_source==='csv'?'csv_ev_hands':x.ev_source==='missing'?'missing_ev_hands':'derived_ev_hands']++;if(x.loss_bb!==null&&w>0){g.covered_hands++;g.covered_weight+=w;g.weighted_loss+=w*x.loss_bb;}x.action_id=a.actions[i].action_id;});
  grouped.add(hand,result);
  if((a.view??'all')==='all'||a.view===result.status)items.push({hand,...result});
 }
 for(const f of files)if(await inputFile(config,f.summary.path)!==f.summary.path||await fileHash(f.summary.path)!==f.summary.sha256)throw Error('CSV changed during EV analysis');
 for(const g of aggregates){g.coverage_percent=totals.pre_action_weight?100*g.covered_weight/totals.pre_action_weight:null;g.mean_loss_bb_on_covered_range=g.covered_weight?g.weighted_loss/g.covered_weight:null;delete g.weighted_loss;}
 const offset=a.offset??0,limit=a.limit??20;
 return {conditions:grouped.finish(),definition:EV_DEFINITION,unit:'bb',definition_basis:'user_confirmed',complete_action_menu:true,tolerance_bb:a.tolerance_bb??0.015,board:a.board,sources:files.map((f,i)=>({action_id:a.actions[i].action_id,...f.summary})),totals,actions:aggregates,total:items.length,offset,items:items.slice(offset,offset+limit),next_offset:offset+limit<items.length?offset+limit:null,notes:['Loss is max(0,-ev_diff); EV reconstruction is limited to exactly two legal actions.','Means evaluate always choosing the named action on its covered range, not observed strategy loss or a re-solved strategy. Different coverage makes means not directly comparable.','Top-level totals describe all hands; conditions use the exact supplied board-specific syntax and may overlap. Common-range losses compare identical hands across actions. Frequencies use all matching pre-action weight, including hands excluded from EV analysis. No reach weighting or bb/100.','Independent CSV rounding may cause up to 0.015bb residual with 0.01bb rounded EV/diff. Tolerance flags contradictions; it does not prove solver accuracy. Raw strings remain accessible via inspect tools.','Return hashes are snapshots; use expected_sha256 for each source on subsequent pages. Missing EV remains null in 3-action cases.']};
}
