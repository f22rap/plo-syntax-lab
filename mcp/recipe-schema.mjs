import {z} from 'zod';

// Transport shapes are explicit; research-recipes.cjs and the native model also
// validate semantic conflicts before any workspace write or generation.
const text=n=>z.string().min(1).max(n);
const rank=z.union([
 z.object({type:z.literal('fixed'),rank:z.enum('23456789TJQKA'.split(''))}).strict(),
 z.object({type:z.literal('board'),position:z.enum(['top','middle','bottom'])}).strict()
]);
const role=z.union([
 z.object({type:z.literal('all')}).strict(),
 z.object({type:z.literal('category'),category:z.enum(['high_card','one_pair','two_pair','three_of_a_kind','straight','flush','full_house','quads','straight_flush'])}).strict(),
 z.object({type:z.enum(['pair','set','trips','quads']),rank}).strict(),
 z.object({type:z.literal('two_pair'),ranks:z.array(rank).length(2)}).strict(),
 z.object({type:z.literal('full_house'),trips:rank,pair:rank}).strict(),
 z.object({type:z.enum(['straight','straight_flush']),high:rank}).strict(),
 z.object({type:z.literal('flush'),tier:z.enum(['nut','second','third','other'])}).strict()
]);
const filter=z.object({role,
 fd:z.enum(['all','none','fd','fdNut','fdSecond','fdThird','fdLow','fdTriple','dualFd','blocker','blockerNut','blockerSecond','blockerThird','blockerLow','blockerNone']),
 sd:z.enum(['all','none','sd','wrap','nutGut','nutOpen','nonGut','nonOpen','blocker','blockerNut','blockerNone']),
 bdfd:z.enum(['all','none','bdfd','bdNut','bdSecond','bdLow','bdTriple','bdPair']),
 bdsd:z.enum(['all','none','bdsd','bdsd4','bdsd8','bdsd9','bdsdOther']),
 pocket:z.union([z.object({type:z.literal('all')}).strict(),z.object({type:z.literal('rank'),rank}).strict()]),
 clean:z.enum(['','regular','all']),blockers:z.array(z.object({rank,mode:z.enum(['yes','no','one','two'])}).strict()).max(13)
}).strict();
const common={recipe_version:z.number().int().min(1),ruleset:text(100),additional_syntax:text(1000000)};
const generator=z.union([
 z.object({...common,source:z.literal('combination'),groups:z.array(z.array(z.string().regex(/^[a-z][a-z0-9_.-]{0,119}@[1-9][0-9]{0,8}$/)).min(1).max(100)).min(1).max(12)}).strict(),
 z.object({...common,source:z.literal('preset'),preset_id:z.string().regex(/^[a-z][a-z0-9_.-]{0,119}$/),preset_revision:z.number().int().min(1)}).strict(),
 z.object({...common,source:z.literal('filter'),filter}).strict()
]);
const identity={condition_id:z.string().regex(/^[a-zA-Z0-9_-]{1,100}$/),label:text(1000),enabled:z.boolean()};
export const conditionSchema=z.union([
 z.object({...identity,kind:z.literal('literal'),syntax:text(1000000),by_board:z.object({}).strict()}).strict(),
 z.object({...identity,kind:z.literal('per_board'),syntax:z.literal(''),by_board:z.record(z.string(),text(1000000))}).strict(),
 z.object({...identity,kind:z.literal('generated'),syntax:z.literal(''),by_board:z.object({}).strict(),generator}).strict()
]);
