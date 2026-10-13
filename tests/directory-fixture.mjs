import fs from 'node:fs/promises';
import path from 'node:path';
export const layout=[
 ['IP','vs_check','BTN','C',['C','R50']],
 ['IP','vs_donk','BTN','R75',['CALL','F','R75']],
 ['IP','vs_donkraiseAI','BTN','R75-R75-R100',['A','F']],
 ['IP','vs_L5','BTN','C-R50-R75-R100-A',['CALL','F']],
 ['IP','vs_XR','BTN','C-R50-R75',['CALL','F','R100']],
 ['OOP','donk','CO','ROOT',['C','R75']],
 ['OOP','vs_bet','CO','C-R50',['CALL','F','R75']],
 ['OOP','vs_donkraise','CO','R75-R75',['CALL','F','R100']],
 ['OOP','vs_L4','CO','R75-R75-R100-A',['CALL']],
 ['OOP','vs_XRraise','CO','C-R50-R75-R100',['A','CALL','F']],
];
export async function makeDirectoryFixture(root,boards=['AsKd7c','Ah8h3c','Ts9d2c','Ts9d2d']){
 for(const board of boards)for(const [side,node,position,history,actions] of layout){
  const dir=path.join(root,board,side,node);await fs.mkdir(dir,{recursive:true});
  for(const [i,action] of actions.entries())await fs.writeFile(path.join(dir,`${board}_${position}_${history}_${action}.csv`),'hand,frequency,percent,weight,ev,ev_diff,variations\n'+(side==='IP'?'AcAd4c5d':'KcKh6h9d')+`,1,100,${(i+1)/6},1.24,.29,1\n`);
 }
}
export const directoryScenario={game:'PLO4',players:2,hero_position:'BTN',villain_position:'CO',effective_stack_bb:'100',pot_type:'SRP',action_history:'CO checks',solution_context:'Synthetic fixture, not solver data',equity_reference:'Before actions'};
