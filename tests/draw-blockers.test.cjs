const assert=require('node:assert/strict'),P=require('../src/lab/engine.js'),generate=require('../src/defaults.js');
(async()=>{
 for(const board of ['As7s2d','Ks7s2d','As7d2c','As7s2s']){
 const e=new P.Engine();await e.prepare(board);
 const id=text=>{const h=P.parseHand(text,e.board).sort((a,b)=>a-b);return Array.from(e.hands).findIndex(x=>P.unpack(x).every((c,i)=>c===h[i]));};
 for(const type of ['sd','fd'])for(const value of (type==='sd'?['blocker','blockerNut','blockerNone']:['blocker','blockerNut','blockerSecond','blockerThird','blockerLow','blockerNone'])){
 const result=e.syntax({[type]:value});assert.ok(result.verified||!result.count);
 for(let i=0;i<e.hands.length;i++)if(e.matches(i,{[type]:value}))assert.equal(e.flags[i]&(type==='sd'?P.F.sd:P.F.fd),0);
 }
 if(board==='As7s2d'){
 assert(e.matches(id('KsQhJh9c'),{fd:'blockerNut'}));
 assert(e.matches(id('QsKhJh9c'),{fd:'blockerSecond'}));
 assert(e.matches(id('JsKhQh9c'),{fd:'blockerThird'}));
 assert(!e.matches(id('KsQsJh9c'),{fd:'blocker'}));
 assert(e.matches(id('5sKhQh9c'),{sd:'blocker'}));
 assert(!e.matches(id('5s4hQh9c'),{sd:'blocker'}));
 const detail=e.blockerDetails('5sKhQh9c');assert(detail.sd.eligible&&detail.sd.blocked>0);
 const rows=await generate(P,e,[{cell:'x',label:'Thit SD SDblocker'},{cell:'y',label:'Thit FD blockerNut'}]);assert(rows.every(r=>r.count>0));
 }
 if(board==='Ks7s2d')assert(e.matches(id('AsQhJh9c'),{fd:'blockerNut'}));
 if(board==='As7d2c'||board==='As7s2s')assert.equal(e.query({fd:'blocker'}).count,0);
 console.log(board,'all blocker syntax verified across',e.hands.length,'hands');
 }
})().catch(e=>{console.error(e);process.exitCode=1});
