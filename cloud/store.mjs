import contracts from '../src/mcp/contracts.cjs';
const {ApiError}=contracts;
export class Store {
 constructor(db,files,owner,now=Date.now){this.db=db;this.files=files;this.owner=owner;this.now=now;}
 query(sql,...args){return this.db.prepare(sql).bind(...args);}
 decode(row){if(!row)throw new ApiError('ID_NOT_FOUND','IDが見つからないか有効期限が切れています。');return {...JSON.parse(row.data),revision:row.revision};}
 async get(id,kind,{retained=false}={}){return this.decode(await this.query('SELECT data,revision FROM plo_records WHERE owner=? AND id=? AND kind=? AND '+(retained?'(expires_at>? OR retain_until>?)':'expires_at>?'),this.owner,id,kind,this.now(),...(retained?[this.now()]:[])).first());}
 async request(key){const row=await this.query('SELECT data,revision FROM plo_records WHERE owner=? AND request_key=? AND expires_at>?',this.owner,key,this.now()).first();return row?this.decode(row):null;}
 async list(kind){const rows=await this.query('SELECT data,revision FROM plo_records WHERE owner=? AND kind=? AND expires_at>? ORDER BY created_at,id',this.owner,kind,this.now()).all();return rows.results.map(row=>this.decode(row));}
 insertion(kind,id,data,{count=64,bytes=8*1024*1024,requestKey=null,signature=null,state=null,upload=null,activeJob=false,refs=[]}={}){
  const sql=`INSERT OR IGNORE INTO plo_records (owner,id,kind,data,created_at,expires_at,bytes,request_key,signature,state)
   SELECT ?,?,?,?,?,?,?,?,?,? WHERE
   (SELECT COUNT(*) FROM plo_records WHERE owner=? AND kind=? AND expires_at>?)<? AND
   (SELECT COALESCE(SUM(bytes),0) FROM plo_records WHERE owner=? AND kind=? AND expires_at>?)<=?
   ${upload?'AND EXISTS (SELECT 1 FROM plo_records WHERE owner=? AND id=? AND kind=\'upload\' AND expires_at>? AND revision=?)':''}
   ${activeJob?"AND NOT EXISTS (SELECT 1 FROM plo_records WHERE owner=? AND kind='job' AND state IN ('queued','running') AND expires_at>?)":''}
   ${refs.length?`AND (SELECT COUNT(*) FROM plo_records WHERE owner=? AND id IN (${refs.map(()=>'?').join(',')}) AND (expires_at>? OR retain_until>?))=?`:''}`;
  const args=[this.owner,id,kind,JSON.stringify(data),this.now(),data.expiresAt,data.sizeBytes||0,requestKey,signature,state,
   this.owner,kind,this.now(),count,this.owner,kind,this.now(),bytes-(data.sizeBytes||0)];
  if(upload)args.push(this.owner,upload.uploadId,this.now(),upload.revision);
  if(activeJob)args.push(this.owner,this.now());
  if(refs.length)args.push(this.owner,...refs,this.now(),this.now(),refs.length);
  return this.query(sql,...args);
 }
 async insert(kind,id,data,options){const result=await this.insertion(kind,id,data,options).run();return result.meta.changes===1;}
 async consumeUpload(id,dataset,upload){
  const results=await this.db.batch([
   this.insertion('dataset',id,dataset,{count:32,upload}),
   this.query("DELETE FROM plo_records WHERE owner=? AND id=? AND kind='upload' AND EXISTS (SELECT 1 FROM plo_records WHERE owner=? AND id=? AND kind='dataset')",this.owner,upload.uploadId,this.owner,id)
  ]);return results[0].meta.changes===1;
 }
 async replace(id,kind,data,revision){const result=await this.query('UPDATE plo_records SET data=?,state=?,bytes=?,revision=revision+1 WHERE owner=? AND id=? AND kind=? AND revision=? AND expires_at>?',JSON.stringify(data),data.state||null,data.sizeBytes||0,this.owner,id,kind,revision,this.now()).run();return result.meta.changes===1;}
 async byteSum(kind){const row=await this.query('SELECT COALESCE(SUM(bytes),0) AS total FROM plo_records WHERE owner=? AND kind=? AND expires_at>?',this.owner,kind,this.now()).first();return row.total;}
 async remove(id){await this.query('DELETE FROM plo_records WHERE owner=? AND id=?',this.owner,id).run();}
 async expireRequest(key){const row=await this.query('SELECT id,data FROM plo_records WHERE owner=? AND request_key=? AND expires_at<=?',this.owner,key,this.now()).first();if(row){await this.remove(row.id);const record=JSON.parse(row.data);if(record.resultKey)await this.files.delete(record.resultKey);}}
 pinStatement(ids,until,jobId){return this.query(`UPDATE plo_records SET retain_until=? WHERE owner=? AND id IN (${ids.map(()=>'?').join(',')}) AND EXISTS (SELECT 1 FROM plo_records WHERE owner=? AND id=? AND kind='job')`,until,this.owner,...ids,this.owner,jobId);}
 async release(job){const ids=[...job.datasetIds,...job.conditionIds];if(ids.length)await this.query(`UPDATE plo_records SET retain_until=0 WHERE owner=? AND id IN (${ids.map(()=>'?').join(',')}) AND NOT EXISTS (SELECT 1 FROM plo_records WHERE owner=? AND kind='job' AND state IN ('queued','running') AND expires_at>?)`,this.owner,...ids,this.owner,this.now()).run();}
 async blob(key){const object=await this.files.get(key);if(!object)throw new ApiError('ID_NOT_FOUND','ファイルが見つかりません。');return object;}
 async cleanup(){
  // Expiry is always checked on access. Delete a bounded batch of physical blobs on later visits.
  const rows=await this.query('SELECT id,data FROM plo_records WHERE owner=? AND expires_at<=? AND retain_until<=? LIMIT 16',this.owner,this.now(),this.now()).all();
  for(const row of rows.results){const deleted=await this.query('DELETE FROM plo_records WHERE owner=? AND id=? AND expires_at<=? AND retain_until<=?',this.owner,row.id,this.now(),this.now()).run();if(deleted.meta.changes===1){const data=JSON.parse(row.data);if(data.blobKey)await this.files.delete(data.blobKey);if(data.resultKey)await this.files.delete(data.resultKey);}}
 }
}
