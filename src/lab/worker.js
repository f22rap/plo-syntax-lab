importScripts('engine.js');
let engine;
onmessage=async({data})=>{const {id,type}=data;try{
 if(type==='prepare'){engine=new PLO.Engine();const result=await engine.prepare(PLO.parseBoard(data.board),p=>postMessage({type:'progress',...p}));postMessage({id,result});}
 else if(!engine?.ready)throw Error('先にボードを解析してください。');
 else if(type==='query')postMessage({id,result:engine.query(data.filter)});
 else if(type==='syntax')postMessage({id,result:engine.syntax(data.filter)});
 else if(type==='bdsdDetails')postMessage({id,result:engine.bdsdDetails(data.hand)});
 else if(type==='export'){const rows=[];for(const item of data.rows){const result=engine.syntax(item.filter);rows.push({label:item.label,filter:item.filter,count:result.count,syntax:result.syntax});}postMessage({id,result:rows});}
 }catch(error){postMessage({id,error:error.message});}};

