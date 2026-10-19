import PDFDocument from 'pdfkit';
import path from 'node:path';
import {root} from './runtime.mjs';
import {LABELS,stateLabel} from './strategy-features.mjs';
const FONT=path.join(root,'assets/fonts/NotoSansJP-Regular.ttf');
const INK='#193348',MUTED='#617587',LINE='#d4dee6',BET='#d8eedf',CHECK='#f5e6d9';
const W=841.89,H=595.28,M=28;
function blend(a,b,t){const n=x=>parseInt(x,16),rgb=x=>[x.slice(1,3),x.slice(3,5),x.slice(5,7)].map(n);return '#'+rgb(a).map((x,i)=>Math.round(x+(rgb(b)[i]-x)*t).toString(16).padStart(2,'0')).join('');}
export async function renderStrategyPdf(report){
 if(report.template_version!=='ahit-landscape-v1'||report.threshold!==0.5)throw Error('Unsupported fixed PDF template or threshold.');
 const doc=new PDFDocument({size:'A4',layout:'landscape',margin:M,bufferPages:true,info:{Title:'Ahit 初手 条件分岐表',Author:'PLO Syntax Lab',Subject:report.policy}}),chunks=[];
 const done=new Promise((resolve,reject)=>{doc.on('data',c=>chunks.push(c));doc.on('end',()=>resolve(Buffer.concat(chunks)));doc.on('error',reject);});
 doc.registerFont('JP',FONT);doc.font('JP');let first=true;
 const text=(s,x,y,width,size=9,color=INK,align='left')=>doc.fillColor(color).fontSize(size).text(String(s),x,y,{width,align,lineBreak:false});
 const line=(x,y,w)=>doc.moveTo(x,y).lineTo(x+w,y).strokeColor(LINE).lineWidth(.5).stroke();
 function header(title,sub){if(first)first=false;else doc.addPage();doc.rect(0,0,W,66).fill(INK);text('PLO SYNTAX LAB   |   Ahit 初手',M,12,W-2*M,10,'#b8d2e5');text(title,M,31,W-2*M,18,'#ffffff');text(sub,M,77,W-2*M,9,MUTED);}
 function paragraph(s,y,size=10){doc.fontSize(size).fillColor(INK).text(s,M,y,{width:W-M*2,lineGap:5});return doc.y+14;}
 header('4条件で読む、ブロック平均の簡易戦略','固定形式 ahit-landscape-v1  |  A-high・rainbow・2ペア／セットなし');
 let y=111;y=paragraph('平均ベット頻度 50%以上 → ベット　／　50%未満 → チェック',y,15);
 y=paragraph('同じ4条件のセルで、該当データがあるボードを等重みで平均。未成立・該当なしは除外し、0%は含めます。判定は丸め前の数値で行います。',y);
 const widths=[50,225,480];doc.rect(M,y,W-2*M,27).fill(INK);let x=M;['ブロック','対象ボード','条件の優先順（左から）'].forEach((s,i)=>{text(s,x+6,y+7,widths[i],9,'#ffffff');x+=widths[i];});y+=27;
 for(const g of report.groups){if(y>455){header('ブロックと共通4条件','各ブロック内ではすべてのボードで条件を統一');y=111;}doc.rect(M,y,W-2*M,47).fill('#f0f5f8');text(g.id,M+6,y+8,45,11);doc.fontSize(9).fillColor(INK).text(g.boards.join(' / '),M+56,y+7,{width:215,height:38});g.selected.forEach((f,i)=>text(`${i+1}. ${LABELS[f]}`,M+280+(i%2)*245,y+7+Math.floor(i/2)*18,240,8.5));y+=48;}
 paragraph('緑：ベット　茶：チェック　平均頻度は青の濃淡で表示。数値の下の n は平均に含むボード数。',Math.min(y+18,528),9);
 function panel(g,rows,title,x,y,w){
  doc.rect(x,y,w,25).fill(INK);text(title,x+6,y+6,w-12,10,'#ffffff');y+=25;
  const fs=g.selected.slice(1),cw=[w*.255,w*.255,w*.255,w*.125,w*.11];let cx=x;
  const headers=[...fs.map(f=>LABELS[f]),'平均','判定'];
  for(let i=0;i<5;i++){doc.rect(cx,y,cw[i],40).fill('#e9f0f5');doc.fontSize(7.4).fillColor(INK).text(headers[i],cx+4,y+6,{width:cw[i]-8,height:32});cx+=cw[i];}y+=40;
  if(!rows.length){text('該当データなし',x+8,y+14,w-16,10,MUTED);return;}
  for(const row of rows){let cx=x;const vals=[...fs.map((f,i)=>stateLabel(f,row.answers[i+1])),(100*row.p).toFixed(1)+'%',row.action==='bet'?'ベット':'チェック'];
   for(let i=0;i<5;i++){const color=i===3?blend('#f4f8fb','#91c1e5',row.p):i===4?(row.action==='bet'?BET:CHECK):row.answers[i+1]===2&&fs[i]?.startsWith('block_')?'#e7e9eb':'#ffffff';doc.rect(cx,y,cw[i],23).fill(color);doc.fontSize(i===4?7.5:7.3).fillColor(INK).text(vals[i],cx+3,y+4,{width:cw[i]-6,height:18});if(i===3)text('n='+row.valid_boards,cx+3,y+14,cw[i]-6,5.5,MUTED);cx+=cw[i];}line(x,y+23,w);y+=23;
  }
 }
 for(const g of report.groups){
  const types=[{title:'SDなし',states:[0]},{title:'SDあり：4アウツ',states:[1,2]},{title:'SDあり：8アウツ',states:[3,4]},{title:'SDあり：9アウツ以上',states:[5,6,7]}];
  for(const type of types){const states=type.states.filter(s=>g.cells.some(c=>c.answers[0]===s));if(!states.length)continue;
   const jobs=[];
   if(type.states.length===1){const rows=g.cells.filter(c=>c.answers[0]===0);for(let i=0;i<rows.length;i+=32)jobs.push([{rows:rows.slice(i,i+16),title:'SDなし'},{rows:rows.slice(i+16,i+32),title:'SDなし（続き）'}]);}
   else{for(let i=0;i<states.length;i+=2){const ss=states.slice(i,i+2),sets=ss.map(s=>g.cells.filter(c=>c.answers[0]===s)),count=Math.max(...sets.map(r=>Math.ceil(r.length/16)));for(let n=0;n<count;n++)jobs.push(ss.map((s,j)=>({rows:sets[j].slice(n*16,(n+1)*16),title:stateLabel('sd_outs_slot',s)})));}}
   for(const parts of jobs){header(`${g.id}  |  ${type.title}`,g.boards.join(' / ')+'　|　SD → '+g.selected.slice(1).map(f=>LABELS[f]).join(' → '));const gap=15,pw=(W-2*M-gap)/2;parts.forEach((p,i)=>panel(g,p.rows,p.title,M+i*(pw+gap),108,pw));text('SDありのSDブロック条件は「対象外」。空欄・未成立の条件を0%やチェックに置き換えません。',M,550,W-2*M,8,MUTED);}
  }
 }
 header('定義と再計算の約束','頻度の説明力と簡易戦略の判断基準');y=108;
 const definitions=[
  'SD：PLOの手札2枚＋ボード3枚で、次の1枚でストレートになる実カードを数えます。4・8アウツは、全アウツがナッツの場合のみナッツ。ナッツ比較はストレート高で行い、フラッシュ等は含みません。',
  'BDFD / BDFD3枚以上：いいえ／いいえ、はい／いいえ、はい／はいの1条件。ボードにあるスートを手札に2枚以上持つとBDFD。2+2の2種類BDFDは、はい／いいえです。',
  'PP：PPなし／22／22以外のSDブロックPP／その他PP。SDありはPPなし／22／22以外のPP。3枚の同ランクもPP扱い。残るサイドカードがブロックするだけではSDブロックPPにしません。',
  'SD構成カード3枚：SDコアの構成ランクを持つ物理カードが3枚以上。重複ランクも枚数に含む。OESDを2種類ブロック：2枚コアの種類数であり、1枚が複数コアを抑える場合も数えます。すべて自分のSDなしで適用。',
  '4条件の選定：SDを必須にした455組を比較し、ボード内の細分セルの頻度分散を最も説明する組を選びます。ボードは等重み。同点は平均絶対誤差、条件名で決めます。SDを最初の入口とし、残り3条件を追加説明力順に並べます。',
  'ブロック：既存5ブロックは合意済みの分類。頻度傾向で再分類するモードは、条件族を均等化した条件別頻度差をコサイン距離・平均連結法でまとめます。どちらも登録サンプルについての分類です。',
  ...report.notes.slice(0,1),
 ];for(const p of definitions)y=paragraph(p,y,9);
 header('ボード別 SDランク・OESDコア','PP自身のランク判定とコア種類数の参照');y=108;
 for(const b of report.board_definitions){if(y>505){header('ボード別 SDランク・OESDコア（続き）','登録ボードだけを表示');y=108;}text(b.key,M,y,60,10);text('SD構成ランク：'+b.sd_ranks.join(' '),M+70,y,280,9.5);text('OESD：'+(b.oesd_cores.join(' / ')||'なし'),M+365,y,W-M*2-365,9.5);line(M,y+20,W-M*2);y+=24;}
 header('ソースと再現性','Report '+report.report_id);y=108;y=paragraph('作成日時：'+report.created_utc+'　／　ポリシー：'+report.policy,y,9);y=paragraph('Catalog：'+report.provenance.catalog_id+' revision '+report.provenance.catalog_revision+'　／　Node：'+report.provenance.node,y,9);y=paragraph('Source commit：'+report.provenance.source_commit,y,9);paragraph('CSVのSHA-256、条件選定上位候補、ボード別セル、ナッツ判定ルールは保存JSONに保持。PDFの再出力は保存した平均・判定を使用します。登録CSVを更新して再集計する場合は、新しいレポートIDを作成します。',y,10);
 const range=doc.bufferedPageRange();for(let i=range.start;i<range.start+range.count;i++){doc.switchToPage(i);doc.page.margins.bottom=0;line(M,H-25,W-2*M);text('50%以上：ベット / 未満：チェック   |   '+report.template_version,M,H-20,W-2*M-50,7,MUTED);text(`${i+1} / ${range.count}`,W-M-48,H-20,48,7,MUTED,'right');}
 doc.end();return done;
}
