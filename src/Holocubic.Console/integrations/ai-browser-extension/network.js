'use strict';
// Runs in the page world. Observe stream lifecycle without cloning, draining or inspecting its data.
(()=>{
 const routes={
  'chatgpt.com':/\/backend-api\/(?:f\/)?conversation(?:\?|$)/,
  'chat.openai.com':/\/backend-api\/(?:f\/)?conversation(?:\?|$)/,
  'claude.ai':/\/chat_conversations\/[^/]+\/(?:completion|retry_completion)(?:\?|$)/,
  'gemini.google.com':/\/BardFrontendService\/StreamGenerate|\/assistant\.lamda\.BardFrontendService\/StreamGenerate/,
  'chat.deepseek.com':/\/chat\/(?:completion|regenerate)(?:\?|$)/,
  'www.kimi.com':/\/completion\/stream|ChatService\/(?:Chat|Regenerate)/,
  'kimi.com':/\/completion\/stream|ChatService\/(?:Chat|Regenerate)/,
  'kimi.moonshot.cn':/\/completion\/stream|ChatService\/(?:Chat|Regenerate)/,
  'www.doubao.com':/\/chat\/(?:completion|stream)(?:\?|$)/,
  'chat.qwen.ai':/\/chat\/completions(?:\?|$)/,
  'tongyi.aliyun.com':/\/dialog\/conversation|\/chat\/completions/,
  'www.qianwen.com':/\/dialog\/conversation|\/chat\/completions/,
  'grok.com':/\/app-chat\/conversations\/(?:new|[^/]+\/responses)(?:\?|$)/
 };
 const pattern=routes[location.hostname];if(!pattern)return;
 let sequence=0;const prefix=Math.random().toString(36).slice(2),streams=new WeakMap(),readers=new WeakMap(),responses=new WeakMap();
 const notify=(state,id)=>window.postMessage({channel:'holopet-ai-lifecycle-v1',state,id},location.origin);
 const begin=()=>{const token={id:prefix+'-'+(++sequence),ended:false};notify('start',token.id);return token};
 const end=(token,state='done')=>{if(token&&!token.ended){token.ended=true;notify(state,token.id)}};
 const failure=(token,error)=>end(token,error?.name==='AbortError'?'stop':'error');
 const match=(input,options)=>{try{const url=new URL(typeof input==='string'||input instanceof URL?String(input):input.url,location.href);const method=String(options?.method||input?.method||'GET').toUpperCase();return method==='POST'&&pattern.test(url.pathname+url.search)}catch{return false}};
 const originalFetch=window.fetch;
 window.fetch=async function(input,options){if(!match(input,options))return Reflect.apply(originalFetch,this,arguments);const token=begin();try{const response=await Reflect.apply(originalFetch,this,arguments);if(!response.ok){end(token,'error');return response}responses.set(response,token);if(response.body)streams.set(response.body,token);else end(token);return response}catch(error){failure(token,error);throw error}};
 const wrap=(proto,name,make)=>{const old=proto?.[name];if(typeof old==='function')proto[name]=make(old)};
 wrap(ReadableStream.prototype,'getReader',old=>function(){const reader=Reflect.apply(old,this,arguments),token=streams.get(this);if(token)readers.set(reader,token);return reader});
 wrap(ReadableStream.prototype,'pipeThrough',old=>function(){const stream=Reflect.apply(old,this,arguments),token=streams.get(this);if(token)streams.set(stream,token);return stream});
 wrap(ReadableStream.prototype,'pipeTo',old=>function(){const token=streams.get(this),result=Reflect.apply(old,this,arguments);if(!token)return result;return result.then(value=>{end(token);return value},error=>{failure(token,error);throw error})});
 wrap(ReadableStream.prototype,'cancel',old=>function(){end(streams.get(this),'stop');return Reflect.apply(old,this,arguments)});
 for(const method of ['values',Symbol.asyncIterator])wrap(ReadableStream.prototype,method,old=>function(){const iterator=Reflect.apply(old,this,arguments),token=streams.get(this);if(!token)return iterator;return {next(...args){return iterator.next(...args).then(value=>{if(value.done)end(token);return value},error=>{failure(token,error);throw error})},return(...args){end(token,'stop');return iterator.return(...args)},[Symbol.asyncIterator](){return this}}});
 for(const name of ['ReadableStreamDefaultReader','ReadableStreamBYOBReader']){
  const proto=window[name]?.prototype;
  wrap(proto,'read',old=>function(){const token=readers.get(this);const result=Reflect.apply(old,this,arguments);if(!token)return result;return result.then(value=>{if(value.done)end(token);return value},error=>{failure(token,error);throw error})});
  wrap(proto,'cancel',old=>function(){end(readers.get(this),'stop');return Reflect.apply(old,this,arguments)});
 }
 for(const method of ['text','json','arrayBuffer','blob','formData','bytes'])wrap(Response.prototype,method,old=>function(){const token=responses.get(this);const result=Reflect.apply(old,this,arguments);if(!token)return result;return result.then(value=>{end(token);return value},error=>{failure(token,error);throw error})});
 const requests=new WeakMap();
 wrap(Response.prototype,'clone',old=>function(){const response=Reflect.apply(old,this,arguments),token=responses.get(this);if(token){responses.set(response,token);if(response.body)streams.set(response.body,token)}return response});
 wrap(XMLHttpRequest.prototype,'open',old=>function(method,url){requests.set(this,{matches:match(String(url),{method})});return Reflect.apply(old,this,arguments)});
 wrap(XMLHttpRequest.prototype,'send',old=>function(){const request=requests.get(this);if(!request?.matches)return Reflect.apply(old,this,arguments);const token=begin();this.addEventListener('abort',()=>end(token,'stop'),{once:true});this.addEventListener('error',()=>end(token,'error'),{once:true});this.addEventListener('loadend',()=>end(token,this.status>=200&&this.status<300?'done':'error'),{once:true});try{return Reflect.apply(old,this,arguments)}catch(error){failure(token,error);throw error}});
})();
