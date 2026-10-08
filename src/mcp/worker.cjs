'use strict';
const {parentPort,workerData}=require('node:worker_threads');
const {compute}=require('./compute.cjs');
compute(workerData.action,workerData.input).then(result=>parentPort.postMessage({result})).catch(e=>parentPort.postMessage({error:{code:e.code||'INTERNAL_ERROR',message:e.code?e.message:'計算処理に失敗しました。',details:e.details||{}}}));
