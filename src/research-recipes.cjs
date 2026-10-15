'use strict';
// Board-independent saved definitions. No workspace writes or engine preparation.
const {randomUUID,createHash}=require('node:crypto');
const P=require('./lab/engine.js');
const catalog=require('./default-labels.json'),sourcePresets=require('./default-presets.json');
const clone=x=>JSON.parse(JSON.stringify(x));
const fail=(message,code='invalid_recipe')=>{const e=Error(message);e.code=code;throw e;};
function object(x,keys,name){
 if(!x||typeof x!=='object'||Array.isArray(x)||Object.keys(x).some(k=>!keys.includes(k))||keys.some(k=>!Object.hasOwn(x,k)))fail(name+'の項目が不足しているか、未対応の項目があります。');
}
function text(x,name,max=1000){if(typeof x!=='string'||!x.trim()||x.length>max)fail(name+'が不正です。');}
function id(x){if(typeof x!=='string'||! /^[a-zA-Z0-9_-]{1,100}$/.test(x))fail('条件IDが不正です。');}
function choice(x,values,name){if(!values.includes(x))fail(name+'が不正です：'+x);}
function integer(x,min,max,name){if(!Number.isSafeInteger(x)||x<min||x>max)fail(name+'が不正です。');}
function syntax(x){text(x,'syntax',1000000);try{P.compile(x);}catch{fail('syntaxの構文が不正です。');}}
const ranks='23456789TJQKA';
function rankRef(r){
 if(r?.type==='fixed'){object(r,['type','rank'],'固定ランク');choice(r.rank,[...ranks],'ランク');}
 else if(r?.type==='board'){object(r,['type','position'],'ボード相対ランク');choice(r.position,['top','middle','bottom'],'ランク位置');}
 else fail('ランク指定が不正です。');
}
const categories=['high_card','one_pair','two_pair','three_of_a_kind','straight','flush','full_house','quads','straight_flush'];
function role(r){
 switch(r?.type){
  case 'all':object(r,['type'],'役');break;
  case 'category':object(r,['type','category'],'役');choice(r.category,categories,'役分類');break;
  case 'pair':case 'set':case 'trips':case 'quads':object(r,['type','rank'],'役');rankRef(r.rank);break;
  case 'two_pair':object(r,['type','ranks'],'2ペア');if(!Array.isArray(r.ranks)||r.ranks.length!==2)fail('2ペアのランクは2個必要です。');r.ranks.forEach(rankRef);if(stable(r.ranks[0])===stable(r.ranks[1]))fail('2ペアのランクが重複しています。');break;
  case 'full_house':object(r,['type','trips','pair'],'フルハウス');rankRef(r.trips);rankRef(r.pair);if(stable(r.trips)===stable(r.pair))fail('フルハウスのランクが重複しています。');break;
  case 'straight':case 'straight_flush':object(r,['type','high'],'ストレート');rankRef(r.high);break;
  case 'flush':object(r,['type','tier'],'フラッシュ');choice(r.tier,['nut','second','third','other'],'フラッシュ順位');break;
  default:fail('未対応の役です。');
 }
}
const options={
 fd:['all','none','fd','fdNut','fdSecond','fdThird','fdLow','fdTriple','dualFd'],
 sd:['all','none','sd','wrap','nutGut','nutOpen','nonGut','nonOpen'],
 bdfd:['all','none','bdfd','bdNut','bdSecond','bdLow','bdTriple'],
 bdsd:['all','none','bdsd','bdsd4','bdsd8','bdsd9','bdsdOther'],
 clean:['','regular','all']
};
function filter(f){
 object(f,['role','fd','sd','bdfd','bdsd','pocket','clean','blockers'],'生成フィルター');role(f.role);
 for(const k of Object.keys(options))choice(f[k],options[k],k);
 if(f.pocket?.type==='all')object(f.pocket,['type'],'ポケット');
 else if(f.pocket?.type==='rank'){object(f.pocket,['type','rank'],'ポケット');rankRef(f.pocket.rank);}else fail('ポケット指定が不正です。');
 if(!Array.isArray(f.blockers)||f.blockers.length>13)fail('ブロッカー指定が不正です。');
 const seen=new Set();for(const b of f.blockers){object(b,['rank','mode'],'ブロッカー');rankRef(b.rank);choice(b.mode,['yes','no','one','two'],'ブロッカー枚数');const key=stable(b.rank);if(seen.has(key))fail('同じランク指定のブロッカーが重複しています。');seen.add(key);}
 if(f.clean&&[f.fd,f.sd].some(v=>v!=='all'))fail('cleanと通常ドローの個別指定を併用できません。');
 if(f.clean==='all'&&[f.bdfd,f.bdsd].some(v=>v!=='all'))fail('clean=allとバックドアの個別指定を併用できません。');
 if(!['all','none'].includes(f.bdsd)&&!['all','none'].includes(f.sd))fail('BDSDと通常SDを同時に要求できません。');
}
function generator(g){
 const keys=['recipe_version','ruleset','source','additional_syntax'];
 if(g?.source==='filter'){object(g,[...keys,'filter'],'生成条件');filter(g.filter);}
 else if(g?.source==='preset'){object(g,[...keys,'preset_id','preset_revision'],'デフォルト条件');text(g.preset_id,'プリセットID',120);if(!/^[a-z][a-z0-9_.-]*$/.test(g.preset_id))fail('プリセットIDが不正です。');integer(g.preset_revision,1,2147483647,'プリセット版');}
 else fail('未対応の生成方法です。');
 integer(g.recipe_version,1,2147483647,'生成形式の版');text(g.ruleset,'判定規則',100);syntax(g.additional_syntax);
}
function canonicalBoard(value){
 if(typeof value!=='string'||value.length>40)fail('フロップの指定が不正です。','invalid_board');
 let b;try{b=P.parseBoard(value);}catch{fail('フロップのカード指定が不正です。','invalid_board');}
 if(b.length!==3)fail('一括生成はフロップ3枚が対象です。','invalid_board');
 // Same order as C# ResearchValidation.Board: A..2, then s/h/d/c.
 return b.sort((a,b)=>P.rank(b)-P.rank(a)||P.suit(a)-P.suit(b)).map(P.card).join('');
}
function validateCondition(c){
 const keys=['condition_id','label','enabled','kind','syntax','by_board'];
 object(c,c?.kind==='generated'?[...keys,'generator']:keys,'条件');id(c.condition_id);text(c.label,'条件名');if(typeof c.enabled!=='boolean')fail('使用フラグが不正です。');
 if(!c.by_board||typeof c.by_board!=='object'||Array.isArray(c.by_board)||Object.keys(c.by_board).length>10000)fail('ボード別定義が不正です。');
 switch(c.kind){
  case 'literal':syntax(c.syntax);if(Object.keys(c.by_board).length)fail('共通syntaxにボード別定義を混在できません。');break;
  case 'per_board':{
   if(c.syntax!=='')fail('ボード別定義に共通syntaxを混在できません。');const seen=new Set();if(!Object.keys(c.by_board).length)fail('ボード別定義がありません。');
   for(const [b,s] of Object.entries(c.by_board)){const key=canonicalBoard(b);if(seen.has(key))fail('同じ物理ボードの定義が重複しています。');seen.add(key);syntax(s);}break;
  }
  case 'generated':if(c.syntax!==''||Object.keys(c.by_board).length)fail('自動生成条件に生成済みsyntaxを保存できません。');generator(c.generator);break;
  default:fail('未対応の条件形式です。');
 }
 return c;
}
function normalizeSet(set){
 object(set,['set_id','revision','name','conditions'],'条件セット');id(set.set_id);integer(set.revision,0,2147483647,'条件セット版');text(set.name,'条件セット名',200);
 if(!Array.isArray(set.conditions)||set.conditions.length>1000)fail('条件は1,000個までです。');
 const seen=new Set();for(const c of set.conditions){validateCondition(c);if(seen.has(c.condition_id))fail('条件IDが重複しています。');seen.add(c.condition_id);}
 const normalized=clone(set);
 for(const c of normalized.conditions){
  if(c.kind==='per_board')c.by_board=Object.fromEntries(Object.entries(c.by_board).map(([b,s])=>[canonicalBoard(b),s]).sort(([a],[b])=>a.localeCompare(b)));
  if(c.kind==='generated'&&c.generator.source==='filter')c.generator.filter.blockers.sort((a,b)=>stable(a).localeCompare(stable(b)));
 }
 return normalized;
}
function stable(value){
 if(Array.isArray(value))return '['+value.map(stable).join(',')+']';
 if(value&&typeof value==='object')return '{'+Object.keys(value).sort().map(k=>JSON.stringify(k)+':'+stable(value[k])).join(',')+'}';
 return JSON.stringify(value);
}
const hash=x=>createHash('sha256').update(typeof x==='string'?x:stable(x)).digest('hex');
function conditionHash(c){
 validateCondition(c);const meaning={kind:c.kind};
 if(c.kind==='generated'){meaning.generator=clone(c.generator);if(meaning.generator.source==='filter')meaning.generator.filter.blockers.sort((a,b)=>stable(a).localeCompare(stable(b)));}
 else if(c.kind==='literal')meaning.syntax=c.syntax;
 else meaning.by_board=Object.fromEntries(Object.entries(c.by_board).map(([b,s])=>[canonicalBoard(b),s]));
 return hash(meaning);
}
// Immutable identity mapping; display labels from saved user conditions are never parsed.
if(sourcePresets.length!==192||new Set(sourcePresets.map(p=>p.preset_id)).size!==192||sourcePresets.some((p,i)=>p.cell!==catalog[i]?.cell||p.label!==catalog[i]?.label))throw Error('デフォルト条件レジストリと既存一覧が一致しません。');
const presets=sourcePresets.map(p=>Object.freeze({...p})),byId=new Map(presets.map(p=>[p.preset_id,p]));
function listPresets({query='',supported_only=false}={}){
 const q=String(query).replace(/\s/g,'').toLowerCase();return presets.filter(p=>(!supported_only||p.supported)&&(p.label+p.preset_id).replace(/\s/g,'').toLowerCase().includes(q)).map(clone);
}
function getPreset(id){const p=byId.get(id);return p?clone(p):null;}
function createPresetSet({preset_ids,name='デフォルト調査条件',set_id=randomUUID()}={}){
 if(!Array.isArray(preset_ids)||!preset_ids.length||preset_ids.length>192)fail('デフォルト条件を1個以上選んでください。');
 const wanted=new Set(preset_ids);for(const pid of wanted){const p=byId.get(pid);if(!p||!p.supported)fail(p?.reason||'デフォルト条件が見つかりません：'+pid,'unsupported_recipe');}
 return normalizeSet({set_id,revision:0,name,conditions:presets.filter(p=>wanted.has(p.preset_id)).map(p=>({condition_id:randomUUID(),label:p.label,enabled:true,kind:'generated',syntax:'',by_board:{},generator:{recipe_version:1,ruleset:'plo4-flop-v1',source:'preset',preset_id:p.preset_id,preset_revision:p.preset_revision,additional_syntax:'*'}}))});
}
const defaultFilter=()=>({role:{type:'all'},fd:'all',sd:'all',bdfd:'all',bdsd:'all',pocket:{type:'all'},clean:'',blockers:[]});
module.exports={canonicalBoard,normalizeSet,validateCondition,conditionHash,stable,hash,listPresets,getPreset,createPresetSet,defaultFilter,categories};
