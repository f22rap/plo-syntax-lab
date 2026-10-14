'use strict';
const {parentPort,workerData}=require('node:worker_threads');
const {generateBoard}=require('../src/research-generation.cjs');
generateBoard(workerData.board,workerData.condition_set,{progress:p=>parentPort.postMessage({progress:p})})
 .then(result=>parentPort.postMessage({result})).catch(e=>parentPort.postMessage({error:e.message,code:e.code||'generation_failed'}));
