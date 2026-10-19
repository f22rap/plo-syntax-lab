import {SLOTS,RADIX,BLOCKS} from './strategy-features.mjs';
export const POLICY='ahit-four-slot-v1';
export const FIVE_BLOCKS=[{id:'G1',boards:['AJ3','AQ6','AJ7','AT7','AT6']},{id:'G2',boards:['A63','A65','A73','A82','A96']},{id:'G3',boards:['AQ9','AJ9','AT9']},{id:'G4',boards:['AK5','AK9']},{id:'G5',boards:['A87']}];
const mean=a=>a.reduce((s,x)=>s+x,0)/a.length;
const rounded=x=>Math.round(x*1e14)/1e14;
const comb=(a,n)=>n===0?[[]]:a.flatMap((x,i)=>comb(a.slice(i+1),n-1).map(t=>[x,...t]));
export function evaluate(board,features,detail=false){
 const mass=board.atoms.reduce((s,a)=>s+a.weight,0);if(!(mass>0))throw Error('Board has no positive Ahit range weight.');
 const cells=new Map();let base=0;
 for(const a of board.atoms){const answers=features.map(f=>a.features[f]),key=answers.join(',');let c=cells.get(key);if(!c){c={answers,weight:0,bet:0,n:0};cells.set(key,c);}c.weight+=a.weight;c.bet+=a.bet;c.n+=a.n;base+=a.bet;}
 base/=mass;let gain=-base*base,mae=0,variance=0;
 for(const c of cells.values()){c.p=c.bet/c.weight;c.share=c.weight/mass;gain+=c.share*c.p*c.p;}
 for(const a of board.atoms){const p=a.bet/a.weight,q=a.weight/mass,pred=cells.get(features.map(f=>a.features[f]).join(',')).p;mae+=q*Math.abs(p-pred);variance+=q*(p-base)**2;}
 return {gain:Math.max(0,gain),mae,r2:variance>1e-15?Math.max(0,gain)/variance:null,...(detail?{board:board.key,base_p:base,cells:[...cells.values()].map(({answers,p,share,n})=>({answers,p,share,n}))}:{})};
}
export function selectFour(boards){
 const candidates=comb(SLOTS.filter(f=>f!=='sd_outs_slot'),3).map(fs=>['sd_outs_slot',...fs]);
 const ranked=candidates.map(features=>{const rows=boards.map(b=>evaluate(b,features));return {features,gain:mean(rows.map(r=>r.gain)),mae:mean(rows.map(r=>r.mae))};});
 ranked.sort((a,b)=>rounded(b.gain)-rounded(a.gain)||rounded(a.mae)-rounded(b.mae)||a.features.join().localeCompare(b.features.join(),'en'));
 const best=ranked[0],selected=['sd_outs_slot'],rest=best.features.slice(1),stages=[];
 let prev=mean(boards.map(b=>evaluate(b,selected).gain));stages.push({feature:selected[0],gain_added:prev,gain_total:prev,routing_gate:true});
 while(rest.length){const order=rest.map(feature=>({feature,gain:mean(boards.map(b=>evaluate(b,[...selected,feature]).gain))})).sort((a,b)=>rounded(b.gain)-rounded(a.gain)||b.feature.localeCompare(a.feature,'en'));const next=order[0];selected.push(next.feature);stages.push({feature:next.feature,gain_added:next.gain-prev,gain_total:next.gain});prev=next.gain;rest.splice(rest.indexOf(next.feature),1);}
 return {selected,stages,selection:best,top_ten:ranked.slice(0,10),candidate_combinations:candidates.length,board_results:boards.map(b=>evaluate(b,selected,true))};
}
export function averageCells(results,threshold=0.5){
 const cells=new Map();for(const b of results)for(const c of b.cells){if(c.p===null)continue;const key=c.answers.join(',');let v=cells.get(key);if(!v){v={answers:c.answers,values:[],boards:[],n:0};cells.set(key,v);}v.values.push(c.p);v.boards.push(b.board);v.n+=c.n;}
 return [...cells.values()].map(({values,...c})=>({...c,p:mean(values),valid_boards:values.length,action:mean(values)>=threshold?'bet':'check'})).sort((a,b)=>a.answers.find((v,i)=>v!==b.answers[i])===undefined?0:compareAnswers(a.answers,b.answers));
}
function compareAnswers(a,b){for(let i=0;i<a.length;i++)if(a[i]!==b[i])return a[i]-b[i];return 0;}
function vectors(boards){
 const families=[['kicker_top','kicker_top2'],['bdfd_slot'],['pp_slot'],['sd_outs_slot'],BLOCKS];
 return boards.map(b=>{const v=[];for(const fs of families)for(const f of fs){const atoms=b.atoms.filter(a=>!BLOCKS.includes(f)||a.features.sd_outs_slot===0),mass=atoms.reduce((s,a)=>s+a.weight,0),base=mass?atoms.reduce((s,a)=>s+a.bet,0)/mass:0;
  for(let state=0;state<RADIX[f];state++){const subset=atoms.filter(a=>a.features[f]===state),w=subset.reduce((s,a)=>s+a.weight,0);v.push(w?Math.sqrt(w/mass)*(subset.reduce((s,a)=>s+a.bet,0)/w-base)/Math.sqrt(fs.length):0);}}
 const norm=Math.hypot(...v);return norm?v.map(x=>x/norm):v;});
}
export function classifyBoards(boards,{mode='approved-five',block_count=5,blocks}={}){
 if(mode==='custom'){
  if(!Array.isArray(blocks)||!blocks.length)throw Error('Custom blocks are required.');
  const keys=blocks.flatMap(g=>g.boards);if(new Set(blocks.map(g=>g.id)).size!==blocks.length||new Set(keys).size!==keys.length||keys.length!==boards.length||boards.some(b=>!keys.includes(b.key)))throw Error('Custom blocks must contain each selected board exactly once with unique block IDs.');
  return {mode,groups:blocks};
 }
 if(mode==='approved-five'){
  const known=new Set(FIVE_BLOCKS.flatMap(g=>g.boards)),unknown=boards.filter(b=>!known.has(b.key));if(unknown.length)throw Error(`No approved block for ${unknown.map(b=>b.key).join(',')}; use effect-cluster or explicit custom blocks.`);
  return {mode,preset:'ahi-five-v1',groups:FIVE_BLOCKS.map(g=>({...g,boards:g.boards.filter(k=>boards.some(b=>b.key===k))})).filter(g=>g.boards.length)};
 }
 if(mode!=='effect-cluster')throw Error('Unknown classification mode.');
 if(block_count>boards.length||block_count<1)throw Error('block_count must be between 1 and the number of selected boards.');
 const x=vectors(boards),distance=(i,j)=>{const ni=Math.hypot(...x[i]),nj=Math.hypot(...x[j]);return !ni&&!nj?0:!ni||!nj?1:Math.max(0,1-x[i].reduce((s,v,k)=>s+v*x[j][k],0));};
 const groups=boards.map((_,i)=>[i]);while(groups.length>block_count){let best=Infinity,ij;for(let i=0;i<groups.length;i++)for(let j=i+1;j<groups.length;j++){const d=mean(groups[i].flatMap(a=>groups[j].map(b=>distance(a,b))));if(d<best){best=d;ij=[i,j];}}groups[ij[0]].push(...groups[ij[1]]);groups.splice(ij[1],1);}
 return {mode,method:'family-balanced conditional frequency contrasts; cosine average linkage',block_count,groups:groups.map((g,i)=>({id:`G${i+1}`,boards:g.map(j=>boards[j].key)})),note:'Exploratory grouping on this sample; not a claim of optimal block count or causal effects.'};
}
export function analyzeBoards(boards,options={}){
 if(!boards.length||new Set(boards.map(b=>b.key)).size!==boards.length)throw Error('Select one physical board per rank texture and node.');
 const threshold=options.threshold??0.5;if(!Number.isFinite(threshold)||threshold<0||threshold>1)throw Error('Invalid threshold.');
 const classification=classifyBoards(boards,options);
 return {policy:POLICY,template_version:'ahit-landscape-v1',threshold,averaging:'equal valid boards per identical four-condition cell; missing excluded; zero included',selection_method:'Exhaustive 455 four-slot combinations including SD; maximize equal-board explained atomic-frequency variance; tie-break MAE. SD is routing gate first, remaining slots ordered by conditional gain.',classification,groups:classification.groups.map(g=>{const model=selectFour(g.boards.map(k=>boards.find(b=>b.key===k)));return {...g,...model,cells:averageCells(model.board_results,threshold)};}),board_definitions:boards.map(b=>({board:b.board,key:b.key,sd_ranks:b.sd_ranks,oesd_cores:b.oesd_cores,positive_hands:b.atoms.reduce((s,a)=>s+a.n,0)}))};
}
