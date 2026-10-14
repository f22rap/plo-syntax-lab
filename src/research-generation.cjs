'use strict';
const P=require('./lab/engine.js'),generateDefaults=require('./defaults.js');
const R=require('./research-recipes.cjs');
const ranks='23456789TJQKA';
const pop=x=>{x-=(x>>>1)&0x55555555;x=(x&0x33333333)+((x>>>2)&0x33333333);return (((x+(x>>>4))&0x0f0f0f0f)*0x01010101)>>>24;};
function absent(code,message){const e=Error(message);e.emptyCode=code;throw e;}
function rank(ref,engine){
 if(ref.type==='fixed')return ranks.indexOf(ref.rank)+2;
 if(ref.position==='middle'&&engine.ranks.length!==3)absent('relative_rank_unavailable','このボードにはミドルランクがありません。');
 return ref.position==='top'?engine.ranks[0]:ref.position==='bottom'?engine.ranks.at(-1):engine.ranks[1];
}
function resolveFilter(f,e){
 const out={fd:f.fd,sd:f.sd,bdfd:f.bdfd,bdsd:f.bdsd,clean:f.clean,role:'all',pocket:'all',blockers:[]},r=f.role;
 switch(r.type){
  case 'all':break;
  case 'category':out.role='cat:'+R.categories.indexOf(r.category);break;
  case 'pair':case 'set':case 'trips':case 'quads':out.role=r.type+':'+rank(r.rank,e);break;
  case 'two_pair':{const rs=r.ranks.map(x=>rank(x,e)).sort((a,b)=>b-a);if(rs[0]===rs[1])absent('selectors_collide','2ペアの相対指定が同じランクになりました。');out.role='two:'+rs.join(':');break;}
  case 'full_house':{const a=rank(r.trips,e),b=rank(r.pair,e);if(a===b)absent('selectors_collide','フルハウスの相対指定が同じランクになりました。');out.role='full:'+a+':'+b;break;}
  case 'straight':case 'straight_flush':out.role=(r.type==='straight'?'straight':'sf')+':'+rank(r.high,e);break;
  case 'flush':out.role='flush:'+({nut:'0',second:'1',third:'third',other:'2'})[r.tier];break;
 }
 if(f.pocket.type==='rank')out.pocket=String(rank(f.pocket.rank,e));
 out.blockers=f.blockers.map(b=>({rank:rank(b.rank,e),mode:b.mode}));
 // Keep conjunctive constraints even when fixed and relative ranks coincide.
 // The engine then returns the empty set for a contradiction.
 return out;
}
function rowFor(c,board){return {condition_id:c.condition_id,label:c.label,board,kind:c.kind,recipe_hash:R.conditionHash(c),status:'pending',reason_code:null,reason:null,syntax:'',legal_hand_count:null,resolved_filter:null,resolved_preset:null,additional_syntax:c.kind==='generated'?c.generator.additional_syntax:'*',verified:false};}
function empty(row,code='no_legal_hands',reason='このボードでは該当する合法ハンドがありません。'){
 return Object.assign(row,{status:'empty_condition',reason_code:code,reason,syntax:'',legal_hand_count:0,verified:true});
}
function failure(row,status,code,reason){return Object.assign(row,{status,reason_code:code,reason,syntax:'',legal_hand_count:null,verified:false});}
function unsupported(g){
 if(g.recipe_version!==1||g.ruleset!=='plo4-flop-v1')return 'この生成形式・判定規則の版には対応していません。';
 if(g.source==='preset'){const p=R.getPreset(g.preset_id);if(!p)return 'デフォルト条件が見つかりません。';if(p.preset_revision!==g.preset_revision)return 'このデフォルト条件の版には対応していません。';if(!p.supported)return p.reason;}
 return null;
}
function applySyntax(row,base,extra,e){
 // All hand sets use the same physical board and the same full legal 4-card domain.
 const bits=e.expressionSet(base),additional=extra==='*'?null:e.expressionSet(extra);
 const combined=extra==='*'?base:base==='*'?extra:'('+base+'):('+extra+')';
 if(combined.length>1000000)throw Error('生成syntaxが1,000,000文字の上限を超えました。');
 const actual=additional?e.expressionSet(combined):bits;let count=0;
 for(let w=0;w<bits.length;w++){
  // expressionSet('*') and complements may fill padding in the final word.
  // Count and compare only the actual C(49,4) legal hands, never those bits.
  const remaining=e.hands.length-w*32,mask=remaining>=32?0xffffffff:(2**remaining-1)>>>0;
  const expected=((additional?(bits[w]&additional[w]):bits[w])&mask)>>>0;
  if(((actual[w]&mask)>>>0)!==expected)throw Error('追加syntaxとの集合照合が一致しません。');count+=pop(expected);
 }
 if(!count)return empty(row,extra==='*'?'no_legal_hands':'empty_intersection');
 return Object.assign(row,{status:'ready',syntax:combined,legal_hand_count:count,verified:true});
}
async function generatePrepared(e,conditionSet,{progress=()=>{}}={}){
 if(!e.ready||e.board.length!==3)throw Error('解析済みフロップが必要です。');
 const set=R.normalizeSet(conditionSet),board=R.canonicalBoard(e.board.map(P.card).join('')),selected=set.conditions.filter(c=>c.enabled);
 const wanted=new Map();for(const c of selected)if(c.kind==='generated'&&!unsupported(c.generator)&&c.generator.source==='preset'){const p=R.getPreset(c.generator.preset_id);wanted.set(p.preset_id,p);}
 const presets=[...wanted.values()],defaultRows=presets.length?await generateDefaults(P,e,presets,(done,total)=>progress({stage:'defaults',done,total})):[];
 const byCell=new Map(defaultRows.map(r=>[r.cell,r])),rows=[];
 for(const c of selected){
  const row=rowFor(c,board),start=Date.now();
  try{
   if(c.kind==='generated'){
    const g=c.generator,problem=unsupported(g);
    if(problem)failure(row,'unsupported_recipe','unsupported_recipe',problem);
    else if(g.source==='preset'){
     const p=R.getPreset(g.preset_id),generated=byCell.get(p.cell);
     row.resolved_preset={preset_id:p.preset_id,preset_revision:p.preset_revision,cell:p.cell,label:p.label};
     if(!generated)throw Error('デフォルト生成結果が不足しています。');
     if(generated.count)applySyntax(row,generated.syntax,g.additional_syntax,e);
     else if(generated.problem==='このボードでは該当なし')empty(row);
     else if(/このボードには(ミドルランク|3種類のランク)がありません/.test(generated.problem||''))empty(row,'relative_rank_unavailable',generated.problem);
     else throw Error(generated.problem||'デフォルト条件を生成できませんでした。');
    }else{
     row.resolved_filter=resolveFilter(g.filter,e);
     const role=row.resolved_filter.role;
     if(role!=='all'&&!role.startsWith('cat:')&&!e.roles.some(r=>r.key===role))empty(row,'role_not_present','このボードに指定した完成役は存在しません。');
     else{const generated=e.syntax(row.resolved_filter);if(generated.count)applySyntax(row,generated.syntax,g.additional_syntax,e);else empty(row);}
    }
   }else{
    const s=c.kind==='literal'?c.syntax:c.by_board[board];
    if(s===undefined)failure(row,'missing_definition','missing_definition','このボードのsyntaxが未設定です。');else applySyntax(row,s,'*',e);
   }
  }catch(err){if(err.emptyCode)empty(row,err.emptyCode,err.message);else failure(row,'generation_failed','generation_failed',err.message);}
  row.elapsed_ms=Date.now()-start;rows.push(row);progress({stage:'conditions',done:rows.length,total:selected.length});
  // Yield between conditions so worker progress/cancellation can be observed.
  if(rows.length%8===0)await new Promise(resolve=>setTimeout(resolve,0));
 }
 return {board,total_legal_hands:e.hands.length,rows};
}
async function generateBoard(board,set,{progress=()=>{}}={}){
 board=R.canonicalBoard(board);set=R.normalizeSet(set);
 const e=new P.Engine();await e.prepare(P.parseBoard(board),p=>progress({stage:'prepare',...p}));
 return generatePrepared(e,set,{progress});
}
module.exports={resolveFilter,generatePrepared,generateBoard};
