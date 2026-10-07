(function (root) {
'use strict';
const R='23456789TJQKA', S='shdc', BASE=759375;
const rank=c=>(c>>2)+2, suit=c=>c&3, card=c=>R[c>>2]+S[c&3];
const deck=Array.from({length:52},(_,i)=>i);
const CAT=['ハイカード','ワンペア','2ペア','スリーカード','ストレート','フラッシュ','フルハウス','クワッズ','ストレートフラッシュ'];
const F={fd:1,fdNut:2,fdSecond:4,fdLow:8,fdTriple:16,sd:32,wrap:64,nutGut:128,nutOpen:256,nonGut:512,nonOpen:1024,bdfd:2048,bdNut:4096,bdSecond:8192,bdLow:16384,bdTriple:32768,dualFd:131072};
const straightMasks=[];
for(let h=5;h<=14;h++) {let m=0;for(let k=0;k<5;k++)m|=1<<((h-k===1)?14:h-k);straightMasks.push([m,h]);}
function combos(a,n) {const out=[];function walk(start,b){if(b.length===n){out.push(b);return;}for(let i=start;i<=a.length-(n-b.length);i++)walk(i+1,b.concat(a[i]));}walk(0,[]);return out;}
function parseBoard(text) {
 const normalized=String(text).replace(/10/g,'T').replace(/♠/g,'s').replace(/♥/g,'h').replace(/♦/g,'d').replace(/♣/g,'c').replace(/[\s,|/・]+/g,'');
 if(!/^(?:[2-9TJQKA][shdc]){3,5}$/i.test(normalized))throw Error('ボードを3〜5枚で入力してください。例：As Kd 7s（T＝10）');
 const cs=normalized.match(/../g).map(x=>R.indexOf(x[0].toUpperCase())*4+S.indexOf(x[1].toLowerCase()));
 if(new Set(cs).size!==cs.length)throw Error('同じカードが重複しています。');return cs;
}
function five(cs) {
 const count=new Uint8Array(15);let mask=0,flush=true;
 for(let i=0;i<5;i++){const r=rank(cs[i]);count[r]++;mask|=1<<r;if(suit(cs[i])!==suit(cs[0]))flush=false;}
 let high=0;for(const [m,h] of straightMasks)if((mask&m)===m)high=h;
 const groups=[];for(let r=14;r>=2;r--)if(count[r])groups.push([count[r],r]);groups.sort((a,b)=>b[0]-a[0]||b[1]-a[1]);
 let cat,k;
 if(flush&&high){cat=8;k=[high];}
 else if(groups[0][0]===4){cat=7;k=[groups[0][1],groups[1][1]];}
 else if(groups[0][0]===3&&groups[1][0]===2){cat=6;k=[groups[0][1],groups[1][1]];}
 else if(flush){cat=5;k=groups.map(x=>x[1]).sort((a,b)=>b-a);}
 else if(high){cat=4;k=[high];}
 else if(groups[0][0]===3){cat=3;k=groups.map(x=>x[1]);}
 else if(groups[0][0]===2&&groups[1][0]===2){cat=2;k=groups.map(x=>x[1]);}
 else if(groups[0][0]===2){cat=1;k=groups.map(x=>x[1]);}
 else{cat=0;k=groups.map(x=>x[1]);}
 let v=cat;for(let i=0;i<5;i++)v=v*15+(k[i]||0);return v;
}
function category(v){return Math.floor(v/BASE);}
function digits(v){let x=v%BASE;const a=[];for(let i=4;i>=0;i--){const p=15**i;a.push(Math.floor(x/p));x%=p;}return a;}
function best(pair,triples){let v=0,sh=0;for(const t of triples){const cs=[...pair,...t],x=five(cs);if(x>v)v=x;let mask=0;for(const c of cs)mask|=1<<rank(c);for(const [m,h] of straightMasks)if(mask===m&&h>sh)sh=h;}return [v,sh];}
function evaluate(hand,board){let v=0;const ts=combos(board,3);for(const p of combos(hand,2))v=Math.max(v,best(p,ts)[0]);return v;}
function pack(a,b,c,d){return a|(b<<6)|(c<<12)|(d<<18);}
function unpack(x){return [x&63,(x>>>6)&63,(x>>>12)&63,(x>>>18)&63];}
function pop(x){x=x-((x>>>1)&0x55555555);x=(x&0x33333333)+((x>>>2)&0x33333333);return (((x+(x>>>4))&0x0F0F0F0F)*0x01010101)>>>24;}
function rname(r){return R[r-2];}
function rankKey(h){return h.map(rank).sort((a,b)=>b-a).map(rname).join('');}
function pairId(a,b){return a<b?a*52+b:b*52+a;}
function pause(){return new Promise(r=>setTimeout(r,0));}
// Range algebra. Constants never escape into the generated PPT syntax.
const YES={kind:'const',value:true},NO={kind:'const',value:false};
const atom=text=>({kind:'atom',text:/^[2-9TJQKA]+$/i.test(text)?[...text.toUpperCase()].sort((a,b)=>R.indexOf(b)-R.indexOf(a)).join(''):text});
function astKey(n){return n.kind==='const'?(n.value?'1':'0'):n.kind==='atom'?n.text:n.kind==='not'?'!'+astKey(n.child):n.kind+'('+n.children.map(astKey).sort().join(',')+')';}
function not(n){return n.kind==='const'?(n.value?NO:YES):n.kind==='not'?n.child:{kind:'not',child:n};}
function rankImplies(a,b){if(a.kind!=='atom'||b.kind!=='atom'||!/^[2-9TJQKA]+$/.test(a.text+b.text))return false;const remaining=[...a.text];for(const c of b.text){const i=remaining.indexOf(c);if(i<0)return false;remaining.splice(i,1);}return true;}
function join(kind,items){const identity=kind==='and',out=[],seen=new Set();for(const n of items.flatMap(n=>n.kind===kind?n.children:[n])){if(n.kind==='const'){if(n.value!==identity)return identity?NO:YES;continue;}const k=astKey(n);if(seen.has(astKey(not(n))))return identity?NO:YES;if(!seen.has(k)){seen.add(k);out.push(n);}}
 const reduced=out.filter((n,i)=>!out.some((m,j)=>i!==j&&(kind==='or'?rankImplies(n,m):rankImplies(m,n)||(n.kind==='or'&&n.children.some(c=>rankImplies(m,c))))));
 return !reduced.length?(identity?YES:NO):reduced.length===1?reduced[0]:{kind,children:reduced};}
const and=(...ns)=>join('and',ns),or=(...ns)=>join('or',ns);
function render(n){if(n.kind==='const')return n.value?'*':'!*';if(n.kind==='atom')return n.text;if(n.kind==='not'){const s=render(n.child);return '!'+(n.child.kind==='and'?'('+s+')':s);}if(n.kind==='or')return '('+n.children.map(render).join(',')+')';return n.children.map(render).join(':');}
function subsets(rs){const out=new Set();for(let m=0;m<(1<<rs.length);m++){const selected=rs.filter((_,i)=>m&(1<<i));out.add(selected.map(rname).join('')||'*');}return out;}
function rankFits(key,rs){if(key==='*')return true;const held=new Uint8Array(15);for(const r of rs)held[r]++;for(const c of key)if(--held[R.indexOf(c)+2]<0)return false;return true;}
function coverPatterns(rows,wanted,patternsFor){
 const candidates=new Map();for(let i=0;i<rows.length;i++)for(const text of patternsFor(rows[i])){let c=candidates.get(text);if(!c){c={text,yes:[],bad:false};candidates.set(text,c);}if(wanted(rows[i]))c.yes.push(i);else c.bad=true;}
 const pool=[...candidates.values()].filter(c=>!c.bad&&c.yes.length),uncovered=new Set(rows.map((r,i)=>wanted(r)?i:-1).filter(i=>i>=0)),terms=[];
 while(uncovered.size){let best,score=-1;for(const c of pool){let n=0;for(const i of c.yes)if(uncovered.has(i))n++;const value=n/(c.text.length+2);if(value>score&&n){score=value;best=c;}}if(!best)throw Error('条件式で分類を表現できませんでした');terms.push(best.text==='*'?YES:atom(best.text));for(const i of best.yes)uncovered.delete(i);}
 return or(...terms);
}
class Engine {
 async prepare(board,progress=()=>{}){
  this.board=board;this.expressionCache=new Map();this.rangeCache=new Map();this.atomSets=new Map();this.available=deck.filter(c=>!board.includes(c));this.bc=new Uint8Array(15);this.bs=new Uint8Array(4);for(const c of board){this.bc[rank(c)]++;this.bs[suit(c)]++;}
  this.ranks=[...new Set(board.map(rank))].sort((a,b)=>b-a);this.topSuit=Array.from({length:4},(_,s)=>this.available.filter(c=>suit(c)===s).sort((a,b)=>b-a));
  const triples=combos(board,3);this.pairs=new Array(2704);this.pairList=[];const roleMap=new Map();
  for(const p of combos(this.available,2)){
   const [score,sh]=best(p,triples);const role=this.roleOf(score,p);roleMap.set(role.key,role);
   let open=false;for(let start=2;start<=10;start++){const rr=[start,start+1,start+2,start+3];if(rank(p[0])!==rank(p[1])&&p.every(c=>rr.includes(rank(c)))&&rr.filter(r=>!p.some(c=>rank(c)===r)).every(r=>this.bc[r]>0))open=true;}

   const item={p,id:pairId(...p),score,sh,role:role.key,open,lo:0,hi:0,high:new Uint8Array(52)};this.pairs[item.id]=item;this.pairList.push(item);
  }
  this.roles=[...roleMap.values()].sort((a,b)=>b.order-a.order);this.roleIndex=new Map(this.roles.map((r,i)=>[r.key,i]));
  // Draw strength compares straight high cards only. A possible flush or full
  // house does not turn a nut-straight draw into a non-nut-straight draw.
  this.straightOpponents=new Array(52);
  if(board.length<5){
   let n=0;for(const out of this.available){const nextTriples=combos([...board,out],3),opp=[];
    for(const p of this.pairList){if(p.p.includes(out))continue;const [,h]=best(p.p,nextTriples);opp.push([h,...p.p]);if(h){p.high[out]=h;if(out<32)p.lo=(p.lo|(1<<out))>>>0;else p.hi=(p.hi|(1<<(out-32)))>>>0;}}
    opp.sort((a,b)=>b[0]-a[0]);this.straightOpponents[out]=opp;
    if(++n%5===0){progress({percent:Math.round(n/this.available.length*38),message:'次のカードとナッツ条件を照合中'});await pause();}
   }
  }
  const total=this.available.length*(this.available.length-1)*(this.available.length-2)*(this.available.length-3)/24;
  this.hands=new Uint32Array(total);this.handRoles=new Uint16Array(total);this.flags=new Uint32Array(total);this.outs=new Uint8Array(total);this.nutOuts=new Uint8Array(total);this.roleCounts=new Uint32Array(this.roles.length);this.rankTotals=new Map();
  let index=0;const av=this.available;
  for(let ai=0;ai<av.length-3;ai++){
   for(let bi=ai+1;bi<av.length-2;bi++)for(let ci=bi+1;ci<av.length-1;ci++)for(let di=ci+1;di<av.length;di++){
    const h=[av[ai],av[bi],av[ci],av[di]],ps=[this.pairs[pairId(h[0],h[1])],this.pairs[pairId(h[0],h[2])],this.pairs[pairId(h[0],h[3])],this.pairs[pairId(h[1],h[2])],this.pairs[pairId(h[1],h[3])],this.pairs[pairId(h[2],h[3])]];
    let win=ps[0];for(const p of ps)if(p.score>win.score)win=p;
    const rid=this.roleIndex.get(win.role);this.hands[index]=pack(...h);this.handRoles[index]=rid;this.roleCounts[rid]++;
    const rk=rankKey(h);this.rankTotals.set(rk,(this.rankTotals.get(rk)||0)+1);
    if(board.length<5){
     let flags=0,fdSuits=0;
     for(let s=0;s<4;s++){
      const suited=h.filter(c=>suit(c)===s);if(suited.length<2)continue;
      const top=Math.max(...suited),tier=this.topSuit[s].indexOf(top);
      if(this.bs[s]===2){fdSuits++;flags|=F.fd|(tier===0?F.fdNut:tier===1?F.fdSecond:F.fdLow);if(suited.length>=3)flags|=F.fdTriple;}
      if(board.length===3&&this.bs[s]===1){flags|=F.bdfd|(tier===0?F.bdNut:tier===1?F.bdSecond:F.bdLow);if(suited.length>=3)flags|=F.bdTriple;}
     }
     if(fdSuits>=2)flags|=F.dualFd;
     const madeStraight=ps.some(p=>p.sh>0);let lo=0,hi=0;
     if(!madeStraight){for(const p of ps){lo|=p.lo;hi|=p.hi;}for(const c of h){if(c<32)lo&=~(1<<c);else hi&=~(1<<(c-32));}lo>>>=0;hi>>>=0;}
     const outCount=pop(lo)+pop(hi);this.outs[index]=outCount;
     if(outCount){
      flags|=F.sd;let nut=0,rankMask=0;
      for(let part=0;part<2;part++){let bits=part?hi:lo;while(bits){const bit=31-Math.clz32(bits&-bits),out=bit+part*32;bits=(bits&(bits-1))>>>0;rankMask|=1<<rank(out);let high=0;for(const p of ps)high=Math.max(high,p.high[out]);
       for(const opp of this.straightOpponents[out]){if(!h.includes(opp[1])&&!h.includes(opp[2])){if(high>=opp[0])nut++;break;}}
      }}
      this.nutOuts[index]=nut;
      if(outCount>=9)flags|=F.wrap;
      else if(ps.some(p=>p.open))flags|=(nut===outCount?F.nutOpen:F.nonOpen);
      else if(pop(rankMask)===1)flags|=(nut===outCount?F.nutGut:F.nonGut);
     }
     this.flags[index]=flags;
    }
    index++;
   }
   progress({percent:40+Math.round(index/total*60),message:'全ての4枚ハンドを判定中'});await pause();
  }
  this.rankProfiles=new Map();for(let i=0;i<this.hands.length;i++){const h=unpack(this.hands[i]),key=rankKey(h),f=this.flags[i],features={sd:!!(f&F.sd),wrap:!!(f&F.wrap),gut:!!(f&(F.nutGut|F.nonGut)),open:!!(f&(F.nutOpen|F.nonOpen))};const old=this.rankProfiles.get(key);if(!old)this.rankProfiles.set(key,{rs:h.map(rank).sort((a,b)=>b-a),features});else for(const k of Object.keys(features))if(features[k]!==old.features[k])throw Error('ランク条件の整合性エラー');}
  this.ready=true;return this.summary();
 }
 roleOf(score,p){
  const cat=category(score),d=digits(score),r=d[0],s=d[1];let key,label,order=cat*10000+r*100+s;
  if(cat===3){const set=this.bc[r]===1&&rank(p[0])===r&&rank(p[1])===r;key=(set?'set:':'trips:')+r;const pos=this.ranks.indexOf(r);const place=pos===0?'トップ':pos===this.ranks.length-1?'ボトム':this.ranks.length===3?'ミドル':`第${pos+1}位`;label=set?`${place}セット (${rname(r)}${rname(r)})`:`トリップス (${rname(r)})`;order=cat*10000+r*100;}
  else if(cat===2){key=`two:${r}:${s}`;const positions=[this.ranks.indexOf(r),this.ranks.indexOf(s)];let name=positions[0]===0&&positions[1]===1?'トップ2':positions[0]===0&&positions[1]===this.ranks.length-1?'トップ＆ボトム':positions[0]===1&&positions[1]===2&&this.ranks.length===3?'ミドル＆ボトム':'2ペア';label=`${name} (${rname(r)}・${rname(s)})`;if(this.bc[r]>1||this.bc[s]>1)label+=' / ボードペア利用';}
  else if(cat===6){key=`full:${r}:${s}`;label=`${rname(r)}フル・${rname(s)}`;}
  else if(cat===5){const fs=suit(p[0]),top=Math.max(...p),tier=this.topSuit[fs].indexOf(top);key=`flush:${Math.min(tier,2)}`;label=tier===0?'ナッツフラッシュ':tier===1?'セカンドナッツフラッシュ':'その他のフラッシュ';order=cat*10000+(2-Math.min(tier,2))*100;}
  else if(cat===4||cat===8){key=`${cat===4?'straight':'sf'}:${r}`;label=`${rname(r)}ハイ・${CAT[cat]}`;order=cat*10000+r*100;}
  else if(cat===7){key=`quads:${r}`;label=`${rname(r)}のクワッズ`;order=cat*10000+r*100;}
  else if(cat===1){key=`pair:${r}`;const pos=this.ranks.indexOf(r);label=this.bc[r]===1?`${pos===0?'トップ':pos===this.ranks.length-1?'ボトム':this.ranks.length===3?'ミドル':`第${pos+1}位`}ペア (${rname(r)})`:`${rname(r)}のワンペア`;order=cat*10000+r*100;}
  else{key='high';label='ハイカード';order=0;}
  return {key,label,cat,order};
 }
 summary(){return {board:this.board.map(card),total:this.hands.length,roles:this.roles.map((r,i)=>({...r,count:this.roleCounts[i]})),ranks:this.ranks.map(r=>({value:r,label:rname(r)})),street:this.board.length};}
 matches(i,filter={}){
  const role=this.roles[this.handRoles[i]];
  if(filter.role&&filter.role!=='all'&&(filter.role.startsWith('cat:')?role.cat!==Number(filter.role.slice(4)):role.key!==filter.role))return false;
  const f=this.flags[i];
  for(const name of ['fd','sd','bdfd']){const value=filter[name];if(!value||value==='all')continue;if(this.board.length===5||(this.board.length!==3&&name==='bdfd'))return false;if(value==='none'){if(f&F[name])return false;}else if(!(f&F[value]))return false;}
  if(filter.clean==='regular'&&(f&(F.fd|F.sd)))return false;
  if(filter.clean==='all'&&(f&(F.fd|F.sd|F.bdfd)))return false;
  if(filter.pocket&&filter.pocket!=='all'&&unpack(this.hands[i]).filter(c=>rank(c)===Number(filter.pocket)).length<2)return false;
  if(filter.blockers?.length){const h=unpack(this.hands[i]);for(const b of filter.blockers){const n=h.filter(c=>rank(c)===Number(b.rank)).length;if(b.mode==='yes'&&!n||b.mode==='no'&&n||b.mode==='one'&&n!==1||b.mode==='two'&&n<2)return false;}}
  return true;
 }
 query(filter={}){
  const ids=[],counts={regularNone:0,allNone:0};for(const k of Object.keys(F))counts[k]=0;let min=99,max=0,nmin=99,nmax=0;const byRole={};const entries=Object.entries(F);
  for(let i=0;i<this.hands.length;i++)if(this.matches(i,filter)){ids.push(i);for(const [k,v] of entries)if(this.flags[i]&v)counts[k]++;if(!(this.flags[i]&(F.fd|F.sd)))counts.regularNone++;if(!(this.flags[i]&(F.fd|F.sd|F.bdfd)))counts.allNone++;min=Math.min(min,this.outs[i]);max=Math.max(max,this.outs[i]);nmin=Math.min(nmin,this.nutOuts[i]);nmax=Math.max(nmax,this.nutOuts[i]);const key=this.roles[this.handRoles[i]].key;byRole[key]=(byRole[key]||0)+1;}
  this.lastIds=ids;const examples=Array.from({length:Math.min(8,ids.length)},(_,k)=>ids[Math.floor(k*ids.length/Math.min(8,ids.length))]);return {count:ids.length,total:this.hands.length,counts,byRole,outs:ids.length?[min,max]:[0,0],nutOuts:ids.length?[nmin,nmax]:[0,0],examples:examples.map(i=>({cards:unpack(this.hands[i]).map(card),outs:this.outs[i],nutOuts:this.nutOuts[i]}))};
 }
 pairExpression(predicate){
  const patterns=p=>{const choices=p.p.map(c=>[rname(rank(c)),S[suit(c)],card(c)]),out=new Set(['*',...choices.flat()]);for(const a of choices[0])for(const b of choices[1]){const left=a.length===1&&R.includes(a)&&b.length===1&&S.includes(b)?b+a:a+b;const right=b.length===1&&R.includes(b)&&a.length===1&&S.includes(a)?a+b:b+a;let text=left<right?left:right;if([...text].every(c=>R.includes(c)))text=[...text].sort((a,b)=>R.indexOf(b)-R.indexOf(a)).join('');out.add(text);}return out;};
  const n=coverPatterns(this.pairList,predicate,patterns);return n===NO?null:render(n);
 }
 compact(filter){
  if(filter.sd&&!['all','none','sd'].includes(filter.sd)||filter.clean==='all')return null;
  const pos=[],neg=[];let currentMax=-1;
  if(filter.role&&filter.role!=='all'){
   const selected=this.pairList.filter(p=>filter.role.startsWith('cat:')?category(p.score)===Number(filter.role.slice(4)):p.role===filter.role);
   if(!selected.length)return null;currentMax=Math.max(...selected.map(p=>p.score));const keys=new Set(selected.map(p=>p.id));let yes=this.pairExpression(p=>keys.has(p.id)||p.score>currentMax);const no=this.pairExpression(p=>p.score>currentMax);
   if(yes&&no){const a=parseExpression(yes),b=parseExpression(no),excluded=new Set((b.kind==='or'?b.children:[b]).map(astKey));if(a.kind==='or')yes=render(or(...a.children.filter(c=>!excluded.has(astKey(c)))));}
   if(yes&&yes!=='*')pos.push(yes);if(no)neg.push(no);
  }
  for(const key of ['fd','bdfd']){let val=filter[key];if(key==='fd'&&filter.clean==='regular')val='none';if(val&&val!=='all'){
   if(this.board.length===5||(key==='bdfd'&&this.board.length!==3))return null;
   const n=key==='fd'?2:1,suits=[0,1,2,3].filter(s=>this.bs[s]===n),branches=[];
   if(val==='dualFd'){
    if(suits.length<2)return null;pos.push(suits.map(s=>S[s].repeat(2)).join(':'));continue;
   }
   for(const s of suits){const symbol=S[s],top=this.topSuit[s];let expr;
    if(val===key||val==='none')expr=symbol.repeat(2);
    else if(val==='fdNut'||val==='bdNut')expr=card(top[0])+symbol;
    else if(val==='fdSecond'||val==='bdSecond')expr=card(top[1])+symbol+':!'+card(top[0]).toLowerCase();
    else if(val==='fdLow'||val==='bdLow')expr=symbol.repeat(2)+':!('+top.slice(0,2).map(c=>card(c).toLowerCase()).join(',')+')';
    else if(val==='fdTriple'||val==='bdTriple')expr=symbol.repeat(3);
    else return null;
    branches.push(expr);
   }
   const expr=branches.length>1?`(${branches.join(',')})`:branches[0];if(expr)(val==='none'?neg:pos).push(expr);else if(val!=='none')return null;
  }}
  let sd=filter.clean==='regular'?'none':filter.sd;
  if(sd&&sd!=='all'){
   const has=this.pairExpression(p=>!!(p.lo||p.hi));const made=this.pairExpression(p=>p.sh>0);
   if(sd==='sd'){if(!has)return null;pos.push(has);if(made)neg.push(made);}
   else if(has){if(made)neg.push(`(${has}!${made})`);else neg.push(has);}
  }
  if(filter.pocket&&filter.pocket!=='all')pos.push(rname(Number(filter.pocket)).repeat(2));
  for(const b of filter.blockers||[]){const r=rname(Number(b.rank));if(b.mode==='yes')pos.push(r);else if(b.mode==='no')neg.push(r);else if(b.mode==='one'){pos.push(r);neg.push(r+r);}else if(b.mode==='two')pos.push(r+r);}
  let text=pos.length?pos.join(':'):'*';if(neg.length)text+='!('+neg.join(',')+')';return text;
 }
 rankExpression(feature){
  const cacheKey='rank:'+feature;if(this.expressionCache.has(cacheKey))return this.expressionCache.get(cacheKey);
  if(this.board.length===5)return NO;
  const pairNode=predicate=>{const text=this.pairExpression(predicate);return text?parseExpression(text):NO;};
  if(feature==='sd'){
   const made=pairNode(p=>p.sh>0),raw=pairNode(p=>!!(p.lo||p.hi));
   const result=and(raw,not(made));this.expressionCache.set(cacheKey,result);return result;
  }
  if(feature==='gut'||feature==='open'){
   const open=pairNode(p=>p.open);let result;
   if(feature==='open')result=and(this.rankExpression('sd'),open,not(this.rankExpression('wrap')));
   else{const ranks=[];for(let r=2;r<=14;r++){const outs=this.available.filter(c=>rank(c)===r),condition=pairNode(p=>outs.some(c=>p.high[c]>0));if(condition!==NO)ranks.push(condition);}const single=or(...ranks.map((c,i)=>and(c,not(or(...ranks.filter((_,j)=>j!==i))))));result=and(this.rankExpression('sd'),not(open),single);}
   this.expressionCache.set(cacheKey,result);return result;
  }
  const profiles=[...this.rankProfiles.values()],domain=['wrap','gut','open'].includes(feature)?profiles.filter(p=>p.features.sd):profiles,yes=domain.filter(p=>p.features[feature]);let result;
  if(!yes.length)result=NO;
  else{
   const common=[];for(let r=14;r>=2;r--){const count=Math.min(...yes.map(p=>p.rs.filter(x=>x===r).length));for(let i=0;i<count;i++)common.push(r);}
   const core=common.length?common.map(rname).join(''):'*',restricted=domain.filter(p=>rankFits(core,p.rs)),wanted=p=>p.features[feature];
   const positive=coverPatterns(restricted,wanted,p=>subsets(p.rs)),negative=not(coverPatterns(restricted,p=>!wanted(p),p=>subsets(p.rs)));
   result=and(core==='*'?YES:atom(core),render(negative).length<render(positive).length?negative:positive);
   if(domain!==profiles)result=and(this.rankExpression('sd'),result);
  }
  this.expressionCache.set(cacheKey,result);return result;
 }
 blockerCovers(edges){
  const covers=[];function walk(rest,held,budget){if(!rest.length){covers.push(held.slice().sort((a,b)=>a-b));return;}if(!budget)return;
   // A matching supplies a lower bound on the number of blockers required.
   const used=new Set();let bound=0;for(const [a,b] of rest)if(!used.has(a)&&!used.has(b)){used.add(a);used.add(b);if(++bound>budget)return;}
   const [a,b]=rest[0];for(const c of [a,b])walk(rest.filter(e=>!e.includes(c)),held.concat(c),budget-1);
  }walk(edges,[],4);
  return covers.filter((c,i)=>!covers.some((d,j)=>j!==i&&d.length<=c.length&&d.every(x=>c.includes(x))&&(d.length<c.length||j<i)));
 }
 nutExpression(shape){
  const cacheKey='nuts:'+shape;if(this.expressionCache.has(cacheKey))return this.expressionCache.get(cacheKey);
  const positive=shape==='gut'?F.nutGut:F.nutOpen,negative=shape==='gut'?F.nonGut:F.nonOpen;let n=0,b=0;for(const f of this.flags){if(f&positive)n++;if(f&negative)b++;}
  if(!n||!b){const result=n?YES:NO;this.expressionCache.set(cacheKey,result);return result;}
  const bad=[];
  for(const out of this.available){
   const pairs=this.pairList.filter(p=>!p.p.includes(out)),highs=[...new Set(pairs.map(p=>p.high[out]).filter(Boolean))];if(!highs.length)continue;
   const conditions=[];
   for(const h of highs){
    const better=this.straightOpponents[out].filter(p=>p[0]>h).map(p=>[p[1],p[2]]),covers=this.blockerCovers(better);if(!covers.length)continue;
    const can=or(...[...new Set(pairs.filter(p=>p.high[out]>=h).map(p=>p.p.map(rank).sort((a,b)=>b-a).map(rname).join('')))].map(atom));
    const block=or(...covers.map(c=>{const terms=[];for(let r=14;r>=2;r--){const chosen=c.filter(x=>rank(x)===r);if(!chosen.length)continue;const available=this.available.filter(x=>rank(x)===r&&x!==out);if(chosen.length===available.length)terms.push(atom(rname(r).repeat(chosen.length)));else terms.push(...chosen.map(x=>atom(card(x).toLowerCase())));}return and(...terms);}));
    conditions.push(and(can,block));
   }
   const any=or(...[...new Set(pairs.filter(p=>p.high[out]).map(p=>p.p.map(rank).sort((a,b)=>b-a).map(rname).join('')))].map(atom));
   bad.push(and(not(atom(card(out).toLowerCase())),any,not(or(...conditions))));
  }
  const result=not(or(...bad));this.expressionCache.set(cacheKey,result);return result;
 }
 symbolic(filter){
  const basic={...filter,sd:'all',clean:''};
  if(filter.clean&&this.board.length<5){basic.fd='none';if(filter.clean==='all')basic.bdfd=this.board.length===3?'none':'all';}
  const base=this.compact(basic);if(!base)throw Error('基本条件の構文を生成できませんでした');const parts=[parseExpression(base)];
  const sd=filter.clean?'none':filter.sd;
  if(sd&&sd!=='all'){
   if(sd==='sd'||sd==='none')parts.push(sd==='sd'?this.rankExpression('sd'):not(this.rankExpression('sd')));
   else if(sd==='wrap')parts.push(this.rankExpression('wrap'));
   else{const shape=sd.endsWith('Gut')?'gut':'open',nuts=this.nutExpression(shape);parts.push(this.rankExpression(shape),sd.startsWith('nut')?nuts:not(nuts));}
  }

  return render(and(...parts));
 }
 expressionSet(expression){
  const words=Math.ceil(this.hands.length/32),memo=new Map();
  const evaluate=n=>{const key=astKey(n);if(memo.has(key))return memo.get(key);let bits;
   if(n.kind==='const'){bits=new Uint32Array(words);if(n.value)bits.fill(0xffffffff);}
   else if(n.kind==='atom'){
    if(this.atomSets.has(n.text))return this.atomSets.get(n.text);
    const test=compile(n.text);bits=new Uint32Array(words);for(let i=0;i<this.hands.length;i++)if(test(unpack(this.hands[i])))bits[i>>>5]|=1<<(i&31);
    if(this.atomSets.size>=256)this.atomSets.delete(this.atomSets.keys().next().value);this.atomSets.set(n.text,bits);
   }else if(n.kind==='not'){bits=evaluate(n.child).slice();for(let i=0;i<words;i++)bits[i]=~bits[i];}
   else{const values=n.children.map(evaluate);bits=values[0].slice();for(let j=1;j<values.length;j++)for(let i=0;i<words;i++)bits[i]=n.kind==='and'?bits[i]&values[j][i]:bits[i]|values[j][i];}
   memo.set(key,bits);return bits;
  };return evaluate(parseExpression(expression));
 }
 syntax(filter={}){
  const result=this.query(filter);if(!result.count)return {...result,syntax:'',format:'empty',terms:0};
  const key=JSON.stringify(filter);let text=this.rangeCache.get(key);
  if(!text){text=this.compact(filter)||this.symbolic(filter);const actual=this.expressionSet(text);for(let i=0;i<this.hands.length;i++)if(!!(actual[i>>>5]&(1<<(i&31)))!==this.matches(i,filter))throw Error('生成した条件式が判定結果と一致しません。具体的なハンドの羅列は出力しません。');this.rangeCache.set(key,text);}
  return {...result,syntax:text,format:'compact',verified:true,terms:1};
 }
}
// Parser for the emitted subset of PPT: rank/card/suit patterns, union, intersection and exclusion.
// It is independent of the role and draw classifiers and verifies compact output against every legal hand.
function parseExpression(source){
 source=source.replace(/\s+/g,'');
 let at=0;function expression(){const list=[intersection()];while(source[at]===','){at++;list.push(intersection());}return or(...list);}
 function intersection(){let left=primary();while(source[at]===':'||source[at]==='!'){const op=source[at++],right=primary();left=and(left,op==='!'?not(right):right);}return left;}
 function primary(){if(source[at]==='!'){at++;return not(primary());}if(source[at]==='('){at++;const e=expression();if(source[at++]!==')')throw Error('Invalid parentheses');return e;}const start=at;while(at<source.length&&!':!,()'.includes(source[at]))at++;const text=source.slice(start,at);if(text==='*')return YES;if(!text)throw Error('Empty pattern');return atom(text);}
 const result=expression();if(at!==source.length)throw Error('Unexpected syntax');return result;
}
function compile(source){
 function node(n){if(n.kind==='const')return ()=>n.value;if(n.kind==='not'){const f=node(n.child);return h=>!f(h);}if(n.kind==='and'||n.kind==='or'){const fs=n.children.map(node);return n.kind==='and'?h=>fs.every(f=>f(h)):h=>fs.some(f=>f(h));}
  const text=n.text,tokens=[];let i=0;while(i<text.length){if(R.includes(text[i].toUpperCase())){const r=R.indexOf(text[i++].toUpperCase())+2;let s=-1;if(S.includes((text[i]||'?').toLowerCase()))s=S.indexOf(text[i++].toLowerCase());tokens.push([r,s]);}else if(S.includes(text[i].toLowerCase()))tokens.push([0,S.indexOf(text[i++].toLowerCase())]);else throw Error('Invalid card pattern');}
  return h=>{function match(t,used){if(t===tokens.length)return true;const [r,s]=tokens[t];for(let j=0;j<h.length;j++)if(!(used&(1<<j))&&(!r||rank(h[j])===r)&&(s<0||suit(h[j])===s)&&match(t+1,used|(1<<j)))return true;return false;}return match(0,0);};
 }return node(parseExpression(source));
}
const api={Engine,parseBoard,card,rank,suit,evaluate,five,category,combos,compile,unpack,F,CAT};root.PLO=api;if(typeof module!=='undefined')module.exports=api;
})(typeof self!=='undefined'?self:globalThis);
