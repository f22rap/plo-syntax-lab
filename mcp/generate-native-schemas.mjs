// Development-only generator. The checked-in resource is embedded by csc;
// neither the Windows build nor the installed EXE needs Node.js.
import fs from 'node:fs/promises';
import {z} from 'zod';
import {registerResearchTools} from './research-tools.mjs';
import {researchInstructions} from './research-service.mjs';
const tools=[];
function tool(name,description,shape,handler,readOnly=true){
 tools.push({name,description,inputSchema:z.toJSONSchema(z.object(shape).strict(),{io:'input'}),annotations:{readOnlyHint:readOnly,destructiveHint:false,idempotentHint:readOnly,openWorldHint:false}});
}
tool('get_status','Read native MCP capabilities and configured folders. Local diagnostics do not prove a ChatGPT connection.',{});
tool('get_artifact','Read an exported artifact by session-scoped ID. Files over 1 MiB return their path instead of inline contents.',{artifact_id:z.string().uuid()});
registerResearchTools(tool,{});
const data=JSON.stringify({instructions:researchInstructions,tools},null,2)+'\n';
const target=new URL('./native/ToolDefinitions.json',import.meta.url);
if(process.argv.includes('--check')){
 if(await fs.readFile(target,'utf8')!==data)throw Error('Native tool schemas differ from research-tools.mjs. Regenerate them.');
}else await fs.writeFile(target,data);
