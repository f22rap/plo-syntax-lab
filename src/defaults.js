(function(root){
const R='23456789TJQKA';
const union=xs=>!xs.length?'!*':xs.length===1?xs[0]:'('+xs.join(',')+')';
const conjunction=xs=>xs.filter(x=>x&&x!=='*').join(':')||'*';
root.generateDefaults=async function(P,engine,catalog,progress=()=>{}){
 const ranks=engine.ranks,t=ranks[0],m=ranks.length>=3?ranks[1]:null,b=ranks[ranks.length-1],rn=n=>R[n-2];
 const allRanks=Array.from({length:13},(_,i)=>i+2),over=allRanks.filter(r=>r>t),under=allRanks.filter(r=>r<t&&!ranks.includes(r));
 const matchesCache=new Map();
 function plan(label){
  const space=label.indexOf(' '),prefix=label.slice(0,space),suffix=label.slice(space+1),extras=[],predicates=[];let filters=[];
  const exact=(text,test)=>{extras.push(text);predicates.push(test);};
  const rankAt=name=>({T:t,M:m,B:b})[name];
  const has=(r,n=1)=>h=>h.filter(c=>P.rank(c)===r).length>=n;
  function requireRank(r,n=1){if(r==null)throw Error('このボードにはミドルランクがありません。');exact(rn(r).repeat(n),has(r,n));}
  function forbidRank(r){if(r!=null)exact('!'+rn(r),h=>!has(r)(h));}
  if(/^[TMB]set$/.test(prefix)){const r=rankAt(prefix[0]);if(r==null)throw Error('このボードにはミドルランクがありません。');filters=[{role:'set:'+r}];}
  else if(/^[TMB]hit$/.test(prefix)){const r=rankAt(prefix[0]);if(r==null)throw Error('このボードにはミドルランクがありません。');filters=[{role:'pair:'+r}];requireRank(r);}
  else if(prefix==='AA'||prefix==='KK'){const r=prefix==='AA'?14:13;filters=[{role:'pair:'+r,pocket:r}];}
  else if(prefix==='3P'){if(!m)throw Error('このボードには3種類のランクがありません。');filters=[{role:'two:'+t+':'+m}];requireRank(t);requireRank(m);requireRank(b);}
  else if(/^(TM|TB|MB)2P$/.test(prefix)){const pair=prefix.slice(0,2).split('').map(rankAt);if(pair.includes(null))throw Error('このボードにはミドルランクがありません。');filters=[{role:'two:'+Math.max(...pair)+':'+Math.min(...pair)}];for(const r of pair)requireRank(r);for(const name of 'TMB')if(!prefix.slice(0,2).includes(name))forbidRank(rankAt(name));}
  else throw Error('この条件名の定義がありません。');
  if(suffix==='all'){}
  else if(suffix==='obdfd'||suffix==='1bdfd'||suffix==='2bdfd'){
   if(engine.board.length!==3)throw Error('BDFDはフロップのみです。');
   const suits=[0,1,2,3].filter(s=>engine.bs[s]===1),patterns=suits.map(s=>'shdc'[s].repeat(2)),n=suffix==='obdfd'?0:Number(suffix[0]);
   let text;if(n===0)text=patterns.length?'!'+union(patterns):'*';else if(n===1)text=union(patterns.map((p,i)=>conjunction([p,...patterns.filter((_,j)=>j!==i).map(q=>'!'+q)])));else text=union(patterns.flatMap((p,i)=>patterns.slice(i+1).map(q=>p+':'+q)));
   exact(text,h=>suits.filter(s=>h.filter(c=>P.suit(c)===s).length>=2).length===n);
  }else if(suffix.startsWith('hit ')){
   const target=suffix.slice(4);requireRank(rankAt(target));if(prefix.endsWith('set'))for(const name of 'TMB')if(name!==prefix[0]&&name!==target)forbidRank(rankAt(name));
  }else if(suffix==='pp all')throw Error('旧一覧で未定義の条件です。syntax生成でポケットを指定してください。');
  else if(suffix==='pp over'||suffix==='pp under'){
   const higher=suffix==='pp over',wanted=higher?over:under,other=higher?under:over;
   exact(union(wanted.map(r=>rn(r).repeat(2))),h=>wanted.some(r=>has(r,2)(h)));
   if(!prefix.endsWith('set')&&other.length)exact('!'+union(other.map(r=>rn(r).repeat(2))),h=>!other.some(r=>has(r,2)(h)));
  }else if(/^over ?card (all|A|no)$/.test(suffix)){
   const value=suffix.split(' ').pop();if(value==='A'){exact(over.includes(14)?'A':'!*',h=>over.includes(14)&&has(14)(h));}
   else{const negate=value==='no';exact((negate?'!':'')+union(over.map(rn)),h=>negate?!over.some(r=>has(r)(h)):over.some(r=>has(r)(h)));}
  }else if(suffix.startsWith('SD ')){
   const kind=suffix.slice(3);if(kind==='BDSD')throw Error('バックドアストレートは新仕様では対象外です。');
   if(/blocker/i.test(kind))throw Error('SDブロッカーは新仕様で未定義です。');
   if(engine.board.length===5)throw Error('リバーにドロー条件はありません。');
   let variants={all:['sd'],no:['none'],'nuts oe':['nutOpen'],'nuts gut':['nutGut'],oe:['nonOpen'],gut:['nonGut']}[kind];
   if(prefix.endsWith('set')&&kind==='oe')variants=['nutOpen','nonOpen'];if(prefix.endsWith('set')&&kind==='gut')variants=['nutGut','nonGut'];
   if(!variants)throw Error('SD条件が未定義です。');filters=variants.flatMap(sd=>filters.map(f=>({...f,sd})));
  }else throw Error('この追加条件の定義がありません。');
  return {filters,extras,predicates};
 }
 const rows=[];
 for(let idx=0;idx<catalog.length;idx++){
  const item=catalog[idx];let row={cell:item.cell,label:item.label,count:0,syntax:'',problem:''};
  try{
   const p=plan(item.label),base=[];for(const f of p.filters){const key=JSON.stringify(f);let cached=matchesCache.get(key);if(!cached){const result=engine.syntax(f),bits=new Uint8Array(engine.hands.length);for(let i=0;i<bits.length;i++)bits[i]=Number(engine.matches(i,f));cached={result,bits};matchesCache.set(key,cached);}base.push(cached);}
   const positives=base.filter(x=>x.result.count>0);if(positives.length){const syntax=conjunction([union(positives.map(x=>x.result.syntax)),...p.extras]);const actual=engine.expressionSet(syntax);let count=0;
    for(let i=0;i<engine.hands.length;i++){const expected=base.some(x=>!!x.bits[i])&&(!p.predicates.length||p.predicates.every(test=>test(P.unpack(engine.hands[i]))));if(expected)count++;if(expected!==!!(actual[i>>>5]&(1<<(i&31))))throw Error('構文照合が一致しません。');}
    row.count=count;if(count)row.syntax=syntax;
   }
   if(!row.count)row.problem='このボードでは該当なし';
  }catch(e){if(/構文照合|生成した条件式/.test(e.message))throw e;row.problem=e.message;}
  rows.push(row);progress(idx+1,catalog.length);if(idx%8===0)await new Promise(r=>setTimeout(r,0));
 }
 return rows;
};
if(typeof module!=='undefined')module.exports=root.generateDefaults;
})(typeof self!=='undefined'?self:globalThis);
