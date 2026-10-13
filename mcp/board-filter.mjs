export const boardRanks=['all',...'AKQJT98765432'];
export function resolveBoardRank(rank,legacyAhi,inherited){
 if(rank!=null&&!boardRanks.includes(rank))throw Error('highest_rank must be all or A, K, Q, J, T, 9...2.');
 if(legacyAhi!=null&&typeof legacyAhi!=='boolean')throw Error('ahi_only must be boolean.');
 const legacy=legacyAhi==null?null:legacyAhi?'A':'all';
 if(rank!=null&&legacy!=null&&rank!==legacy)throw Error('Conflicting highest_rank and legacy Ahi filter.');
 const selected=rank??legacy??inherited??'all';
 if(!boardRanks.includes(selected))throw Error('Invalid stored highest_rank.');
 return selected;
}
export function matchesBoardRank(board,rank){
 rank=resolveBoardRank(rank);
 if(!/^(?:[AKQJT2-9][shdc]){3}$/.test(board)||new Set(board.match(/../g)).size!==3)throw Error('Invalid physical flop.');
 return rank==='all'||'AKQJT98765432'[Math.min(...board.match(/../g).map(c=>'AKQJT98765432'.indexOf(c[0])))]===rank;
}
