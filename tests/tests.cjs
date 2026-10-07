const assert=require('node:assert/strict');
const P=require('../src/lab/engine.js');
let checks=0;const ok=(value,msg)=>{assert.ok(value,msg);checks++};
function cards(text){return text.match(/../g).map(x=>'23456789TJQKA'.indexOf(x[0])*4+'shdc'.indexOf(x[1]));}
function indexOf(e,text){const hs=cards(text).sort((a,b)=>a-b),packed=hs[0]|hs[1]<<6|hs[2]<<12|hs[3]<<18;const i=e.hands.indexOf(packed);assert.ok(i>=0,text+' is legal');return i;}
function truthDraw(hand,board){const available=Array.from({length:52},(_,i)=>i).filter(c=>!hand.includes(c)&&!board.includes(c));function hasStraight(b){for(const p of P.combos(hand,2))for(const t of P.combos(b,3)){let rs=[...new Set([...p,...t].map(P.rank))].sort((a,b)=>a-b);if(rs.length===5&&(rs[4]-rs[0]===4||rs.join(',')==='2,3,4,5,14'))return true;}return false;}
 const made=hasStraight(board);const outs=made||board.length===5?[]:available.filter(c=>hasStraight([...board,c]));return {outs};}
function verifyEnumeration(e,filter){const out=e.syntax(filter);const expected=new Set();for(let i=0;i<e.hands.length;i++)if(e.matches(i,filter))expected.add(e.hands[i]);assert.equal(out.count,expected.size);if(!out.count){assert.equal(out.syntax,'');checks++;return;}
 assert.equal(out.format,'compact');assert.doesNotMatch(out.syntax,/(?:[2-9TJQKA][shdc]){4}/i);const match=P.compile(out.syntax);for(let i=0;i<e.hands.length;i++)assert.equal(match(P.unpack(e.hands[i])),expected.has(e.hands[i]));
 checks++;return out;
}
(async()=>{
 assert.throws(()=>P.parseBoard('As As 7s'));assert.throws(()=>P.parseBoard('As Kd'));assert.throws(()=>P.parseBoard('As Kd 7x'));assert.deepEqual(P.parseBoard('A♠ K♦ 10♣'),cards('AsKdTc'));checks+=4;
 const fiveCases=[['As2d3c4h5s',4],['AsKsQsJsTs',8],['AhAdAcAsKd',7],['AhAdAcKsKd',6],['AsQs9s6s3s',5],['AhAdAcKsQd',3],['AhAdKsKdQc',2],['AhAdKsQdJc',1],['AhKd9s5c2h',0]];for(const [s,c]of fiveCases){assert.equal(P.category(P.five(cards(s))),c);checks++;}
 ok(P.five(cards('As2d3c4h5s'))<P.five(cards('2s3d4c5h6s')),'wheel below six-high');
 const e=new P.Engine();console.time('flop');await e.prepare(P.parseBoard('As Kd 7s'));console.timeEnd('flop');assert.equal(e.hands.length,211876);assert.equal(e.roleCounts.reduce((a,b)=>a+b,0),211876);checks+=2;
 const no=e.syntax({role:'set:14',clean:'regular'});assert.equal(no.count,2851);assert.equal(e.query({role:'set:14'}).count,3151);assert.equal(e.query({role:'set:14',fd:'fd'}).count,165);assert.equal(e.query({role:'set:14',sd:'sd'}).count,144);assert.equal(e.query({role:'set:14',fd:'fd',sd:'sd'}).count,9);checks+=5;
 // Symbolic FD output, including the requested lowercase negation and prefix ! after :.
 for(const [fd,syntax,count] of [['fd','AA:ss',165],['fdNut','AA:Kss',30],['fdSecond','AA:Qss:!ks',27],['fdLow','AA:ss:!(ks,qs)',108]]){const out=verifyEnumeration(e,{role:'set:14',fd});assert.equal(out.syntax,syntax);assert.equal(out.count,count);assert.equal(out.format,'compact');checks+=3;}
 assert.equal(P.compile('AA:Qss:!ks')(cards('AhAdQsJs')),true);assert.equal(P.compile('AA:Qss:!ks')(cards('AhAdQsKs')),false);assert.equal(P.compile('AA:Qss:!ks')(cards('AhAdQsJd')),false);checks+=3;
 for(const value of ['fdNut','fdSecond','fdLow','fdTriple']){const out=verifyEnumeration(e,{role:'all',fd:value});assert.equal(out.format,'compact');checks++;}
 for(const value of ['bdNut','bdSecond','bdLow','bdTriple']){const out=verifyEnumeration(e,{role:'set:14',bdfd:value});assert.equal(out.format,'compact');checks++;}
 const hs=[['AhAdQsJs',P.F.fd|P.F.sd],['AhAcQd9d',P.F.bdfd],['AhAd9c8c',0],['AhAdKc7c',0]];
 for(const [h,bits]of hs){const i=indexOf(e,h);ok((e.flags[i]&bits)===bits,h);const truth=truthDraw(cards(h),e.board);assert.equal(e.outs[i],truth.outs.length);checks++;}
 assert.equal(e.query({role:'set:14',clean:'all'}).count,1950);checks++;
 ok(e.matches(indexOf(e,'AhAd9c8c'),{role:'set:14',clean:'all'}),'two-card straight potential is not excluded');
 ok(!e.matches(indexOf(e,'AhAcQd9d'),{role:'set:14',clean:'all'}),'BDFD remains excluded');
 ok(!Object.hasOwn(e.query().counts,'bdsd'),'removed draw has no output category');
 for(const f of [{role:'set:14',clean:'regular'},{role:'set:14',clean:'all'},{role:'set:14',fd:'fdSecond'},{role:'set:14',bdfd:'bdTriple'},{role:'set:13',blockers:[{rank:14,mode:'no'}]},{role:'two:14:13',sd:'wrap'}])verifyEnumeration(e,f);
 const turn=new P.Engine();console.time('turn');await turn.prepare(P.parseBoard('Ks 8s 5d 2d'));console.timeEnd('turn');assert.equal(turn.hands.length,194580);checks++;const ti=indexOf(turn,'AsQsAdQd');ok(!!(turn.flags[ti]&P.F.dualFd),'dual FD');ok(!!(turn.flags[ti]&P.F.fdNut),'nut FD');ok(!(turn.flags[ti]&P.F.bdfd),'turn no backdoor');verifyEnumeration(turn,{role:'all',fd:'dualFd',sd:'none'});
 for(const fd of ['fdNut','fdSecond','fdLow','fdTriple','dualFd']){const out=verifyEnumeration(turn,{role:'all',fd});assert.equal(out.format,'compact');checks++;}
 verifyEnumeration(turn,{role:'all',clean:'all'});
 const rainbow=new P.Engine();await rainbow.prepare(P.parseBoard('As Kd 7c'));for(const bdfd of ['bdNut','bdSecond','bdLow','bdTriple']){const out=verifyEnumeration(rainbow,{role:'all',bdfd});assert.equal(out.format,'compact');checks++;}
 let sampled=0;for(let i=100;i<turn.hands.length&&sampled<14;i+=13711){const truth=truthDraw(P.unpack(turn.hands[i]),turn.board);assert.equal(turn.outs[i],truth.outs.length,'turn outs '+i);sampled++;checks++;}
 const river=new P.Engine();console.time('river');await river.prepare(P.parseBoard('Ah Ad Ks Qs Js'));console.timeEnd('river');assert.equal(river.hands.length,178365);checks++;ok(river.flags.every(x=>x===0),'river no draws');for(const cat of [8,7,6,5,4,3,2])verifyEnumeration(river,{role:'cat:'+cat,blockers:[{rank:14,mode:'no'}]});
 verifyEnumeration(river,{role:'cat:6',clean:'all'});
 const made=P.evaluate(cards('AsAcKhKc'),P.parseBoard('Ah Ad Ks Qs Js'));assert.equal(P.category(made),7);checks++;
 // Exactly two hole cards: a one-spade hand cannot use four board spades as a flush.
 assert.notEqual(P.category(P.evaluate(cards('AsKhQdJc'),P.parseBoard('2s 5s 8s Ts 3d'))),5);checks++;
 // Board quads require a matching hand card to play quads (impossible: all four on board).
 assert.equal(P.category(P.evaluate(cards('KsKhQdJc'),P.parseBoard('As Ah Ad Ac 2d'))),6);checks++;
 console.log(JSON.stringify({checks,flopTopSet:e.query({role:'set:14'}).count,flopNoRegularDraw:no.count,syntax:no.syntax,turnHands:turn.hands.length,riverHands:river.hands.length,status:'passed'}));
})().catch(e=>{console.error(e);process.exitCode=1;});
