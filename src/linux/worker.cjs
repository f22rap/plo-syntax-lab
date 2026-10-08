const {parentPort, workerData} = require('node:worker_threads');
(async()=>{
 const P = require('../lab/engine.js');
 if (workerData.action === 'defaults') { const e = new P.Engine(); await e.prepare(P.parseBoard(workerData.input.board)); return require('../defaults.js')(P,e,require('../default-labels.json')); }
 return require('./compare.cjs').compare(workerData.input);
})().then(result=>parentPort.postMessage({result})).catch(e=>parentPort.postMessage({error:e.message}));
