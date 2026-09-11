'use strict';
// Only lifecycle metadata is forwarded. Never send prompts, answers, or tool arguments.
const http=require('node:http');
const states=Object.freeze({SessionStart:'idle',UserPromptSubmit:'thinking',PreToolUse:'working',PostToolUse:'thinking',PostToolUseFailure:'error',PermissionRequest:'notification',Notification:'notification',PreCompact:'working',PostCompact:'thinking',SubagentStart:'building',SubagentStop:'working',Stop:'done',Interrupt:'idle',SessionEnd:'sleeping'});
const clip=(v,n)=>typeof v==='string'?v.slice(0,n):'';
function statusFrom(input,provider='openai'){
 const event=input?.hook_event_name;if(!Object.hasOwn(states,event))return null;
 const result=input.tool_response;
 const failed=result&&typeof result==='object'&&(result.isError===true||result.is_error===true||result.success===false||(typeof result.exit_code==='number'&&result.exit_code!==0));
 return {state:event==='PostToolUse'&&failed?'error':states[event],event,source:provider==='claude'?'claude-code-hooks':'openai-hooks',project:clip(String(input.cwd||'').replace(/[\\/]+$/,'').split(/[\\/]/).pop(),40),tool:clip(input.tool_name,48),session:clip(input.session_id,80),model:clip(input.model,40),effort:clip(input.reasoning_effort||input.effort,16),sent_at:Date.now()};
}
function publish(status){return new Promise(resolve=>{const data=JSON.stringify(status),req=http.request({host:'127.0.0.1',port:17321,path:'/event',method:'POST',headers:{'content-type':'application/json','content-length':Buffer.byteLength(data)},timeout:1000},res=>{res.resume();res.on('end',()=>resolve(res.statusCode===200))});req.on('timeout',()=>req.destroy());req.on('error',()=>resolve(false));req.end(data)})}
async function main(){let size=0,chunks=[];for await(const chunk of process.stdin){size+=chunk.length;if(size<=16*1024*1024)chunks.push(chunk)}if(size>16*1024*1024)return;const input=JSON.parse(Buffer.concat(chunks).toString('utf8').replace(/^\uFEFF/,''));const status=statusFrom(input,process.argv.includes('--claude')?'claude':'openai');if(status)await publish(status)}
if(require.main===module)main().catch(()=>{}).finally(()=>process.stdout.write('{}\n'));
module.exports={states,statusFrom,publish,main};
