// Rank evaluator: exactly two hole cards and three board cards, including A2345.
import P from '../src/lab/engine.js';
export const RANKS='23456789TJQKA';
const pairs=a=>a.flatMap((x,i)=>a.slice(i+1).map(y=>[x,y]));
const triples=a=>a.flatMap((x,i)=>a.slice(i+1).flatMap((y,j)=>a.slice(i+j+2).map(z=>[x,y,z])));
function straight(a){const s=new Set(a);if(s.size!==5)return 0;for(let h=14;h>=5;h--){if(Array.from({length:5},(_,i)=>h-i===1?14:h-i).every(r=>s.has(r)))return h;}return 0;}
const count=a=>a.reduce((c,r)=>(c[r]=(c[r]||0)+1,c),{});
export const BLOCKS=['block_oe_any','block_oe_2plus','block_oe_3','block_oe_top','block_oe_bottom','block_nutgut_any','block_gut_any','block_sd_2plus','block_sd_3','block_oe_targets2','block_oe_targets3'];
export const SLOTS=['sd_outs_slot','bdfd_slot','pp_slot','kicker_top','kicker_top2',...BLOCKS];
export const RADIX=Object.fromEntries(SLOTS.map(f=>[f,f==='sd_outs_slot'?8:f==='pp_slot'?5:f==='bdfd_slot'||BLOCKS.includes(f)?3:2]));
export const LABELS={sd_outs_slot:'SD：アウツ数・ナッツ',bdfd_slot:'BDFD / BDFD3枚以上',pp_slot:'PP',kicker_top:'最上位キッカー',kicker_top2:'上位2ランクのキッカー',block_oe_any:'OESD構成カードあり',block_oe_2plus:'OESD構成カード2枚以上',block_oe_3:'OESD構成カード3枚以上',block_oe_top:'最上位OESDコアをブロック',block_oe_bottom:'最下位OESDコアをブロック',block_nutgut_any:'ナッツガット構成カードあり',block_gut_any:'ガット構成カードあり',block_sd_2plus:'SD構成カード2枚以上',block_sd_3:'SD構成カード3枚以上',block_oe_targets2:'OESDを2種類以上ブロック',block_oe_targets3:'OESDを3種類以上ブロック'};
export const STATES={sd_outs_slot:['SDなし','4アウツ・全ナッツ','4アウツ・非ナッツ','8アウツ・全ナッツ','8アウツ・非ナッツ','9+アウツ・全ナッツ','9+アウツ・一部ナッツ','9+アウツ・ナッツなし'],bdfd_slot:['いいえ / いいえ','はい / いいえ','はい / はい'],pp_slot:['PPなし','22','22以外・SDブロックPP','22以外・その他PP','22以外・PP（SDあり）']};
export function stateLabel(f,v){return STATES[f]?.[v]??(v===2?'対象外':v?'はい':'いいえ');}

export function boardModel(text){
 const physical=P.parseBoard(text),b=physical.map(P.rank),bs=physical.map(P.suit);
 if(b.length!==3||new Set(b).size!==3||Math.max(...b)!==14||new Set(bs).size!==3)throw Error('Standard strategy requires an unpaired A-high rainbow flop.');
 const rp=pairs(Array.from({length:13},(_,i)=>i+2));
 if(rp.some(p=>straight([...p,...b])))throw Error('Boards allowing a made straight are outside this report policy.');
 const bc=count(b),turns=new Map();
 for(let t=2;t<=14;t++){
  const ts=triples([...b,t]),list=rp.map(p=>({pair:p,high:Math.max(0,...ts.map(q=>straight([...p,...q])))})).filter(p=>p.high).sort((a,b)=>b.high-a.high);
  turns.set(t,list);
 }
 const cores=rp.map(p=>{
  const outs=[...turns].flatMap(([t,list])=>{const hit=list.find(q=>q.pair[0]===p[0]&&q.pair[1]===p[1]);if(!hit)return [];const pc=count(p);return [{rank:t,nut:!list.some(q=>q.high>hit.high&&q.pair.every(r=>4-(bc[r]||0)-(pc[r]||0)-(r===t?1:0)>0))}];});
  return {pair:p,outs};
 }).filter(p=>p.outs.length);
 const open=p=>pairs(b).some(q=>{const a=[...p,...q].sort((a,b)=>a-b);return new Set(a).size===4&&a[0]>=2&&a[3]<=13&&a[3]-a[0]===3;});
 const oe=cores.filter(p=>p.outs.length>=2&&open(p.pair)).sort((a,b)=>b.pair[1]-a.pair[1]||b.pair[0]-a.pair[0]),gut=cores.filter(p=>p.outs.length===1),nutgut=gut.filter(p=>p.outs.some(o=>o.nut));
 const ranks=ps=>new Set(ps.flatMap(p=>p.pair)),sdRanks=ranks(cores),oeRanks=ranks(oe),gutRanks=ranks(gut),nutGutRanks=ranks(nutgut);
 const kickers=Array.from({length:13},(_,i)=>14-i).filter(r=>!b.includes(r));
 const cache=new Map();
 function rankFeatures(h){
  const key=h.slice().sort((a,b)=>b-a).map(r=>RANKS[r-2]).join('');if(cache.has(key))return cache.get(key);
  const hc=count(h),hp=pairs([...new Set(h)]),out=[];
  for(const [t,list] of turns){const left=4-(bc[t]||0)-(hc[t]||0);if(left<=0)continue;
   const hit=list.find(q=>hp.some(p=>p.includes(q.pair[0])&&p.includes(q.pair[1])));if(!hit)continue;
   const nut=!list.some(q=>q.high>hit.high&&q.pair.every(r=>4-(bc[r]||0)-(hc[r]||0)-(r===t?1:0)>0));
   out.push({rank:t,cards:left,nut});
  }
  const outs=out.reduce((s,x)=>s+x.cards,0),nutOuts=out.filter(x=>x.nut).reduce((s,x)=>s+x.cards,0);
  if(outs>0&&outs!==4&&outs!==8&&outs<9)throw Error(`Unsupported SD out count ${outs}: ${key}`);
  const sd=outs===0?0:outs===4?(outs===nutOuts?1:2):outs===8?(outs===nutOuts?3:4):(outs===nutOuts?5:nutOuts?6:7);
  const pr=Object.keys(hc).map(Number).find(r=>hc[r]>=2),n=rs=>h.filter(r=>rs.has(r)).length;
  const hit=oe.filter(p=>p.pair.some(r=>hc[r])).length;
  const f={sd_outs_slot:sd,pp_slot:pr===undefined?0:pr===2?1:sd?4:sdRanks.has(pr)?2:3,kicker_top:+h.includes(kickers[0]),kicker_top2:+h.some(r=>kickers.slice(0,2).includes(r)),
   block_oe_any:+!!n(oeRanks),block_oe_2plus:+(n(oeRanks)>=2),block_oe_3:+(n(oeRanks)>=3),block_oe_top:+!!oe[0]?.pair.some(r=>hc[r]),block_oe_bottom:+!!oe.at(-1)?.pair.some(r=>hc[r]),block_nutgut_any:+!!n(nutGutRanks),block_gut_any:+!!n(gutRanks),block_sd_2plus:+(n(sdRanks)>=2),block_sd_3:+(n(sdRanks)>=3),block_oe_targets2:+(hit>=2),block_oe_targets3:+(hit>=3)};
  if(sd)for(const f0 of BLOCKS)f[f0]=2;
  const result={features:f,outs,nut_outs:nutOuts,out_ranks:out,pattern:key};cache.set(key,result);return result;
 }
 return {board:physical.map(P.card).join(''),key:b.slice().sort((a,b)=>b-a).map(r=>RANKS[r-2]).join(''),sd_ranks:[...sdRanks].sort((a,b)=>b-a).map(r=>RANKS[r-2]),oesd_cores:oe.map(p=>p.pair.slice().reverse().map(r=>RANKS[r-2]).join('')),rankFeatures,
  handFeatures(text){const h=P.parseHand(text,physical),r=h.map(P.rank);if(r.filter(x=>x===14).length!==1||r.some(x=>x!==14&&b.includes(x)))return null;
   const base=rankFeatures(r),sc=count(h.map(P.suit)),sizes=bs.map(s=>sc[s]||0),bdfd=sizes.some(n=>n>=3)?2:sizes.some(n=>n>=2)?1:0;
   return {...base,features:{...base.features,bdfd_slot:bdfd}};
  }};
}
